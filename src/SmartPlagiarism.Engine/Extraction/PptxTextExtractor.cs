using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Text;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Extracts text from PowerPoint decks with the Open XML SDK, one segment per
/// slide.
///
/// Slides are walked in the order of the presentation's slide-id list rather than
/// the order the parts happen to sit in the package, so segment numbers match what
/// the student sees.
///
/// Speaker notes live in separate parts and are not read - a similarity hit
/// against notes the audience never saw would be misleading in a report. SmartArt
/// text also lives in separate parts, but the audience does see it, so it is read
/// as part of its slide.
/// </summary>
public sealed class PptxTextExtractor : ITextExtractor
{
    private readonly IOcrService _ocr;
    private readonly ExtractionLimits _limits;

    /// <param name="ocr">Reads the text in pictures on the slides - a slide of screenshots.</param>
    public PptxTextExtractor(IOcrService ocr, ExtractionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(ocr);

        _ocr = ocr;
        _limits = limits ?? new ExtractionLimits();
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pptx"];

    public async Task<TextExtractionResult> ExtractAsync(
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var bytes = await StreamHelpers.ReadAllBytesAsync(document, cancellationToken);
        var builder = new TextExtractionResultBuilder();
        var settings = OfficePackage.CheckedOpenSettings(bytes, _limits);
        var budget = new OcrBudget(_limits.OcrTimeBudget);
        var pictures = new PictureReader(_ocr, budget);

        using var buffer = new MemoryStream(bytes, writable: false);
        using var presentation = PresentationDocument.Open(buffer, isEditable: false, settings);

        var presentationPart = presentation.PresentationPart;

        if (presentationPart is null)
        {
            builder.AddWarning("The presentation has no presentation part.");
            return builder.Build();
        }

        var slideNumber = 1;

        foreach (var slidePart in EnumerateSlidesInOrder(presentationPart, builder))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var themeFonts = ThemeFonts.Of(slidePart);

            // SmartArt keeps its text in a diagram part of its own, not on the slide.
            var paragraphs = (slidePart.Slide?.Descendants<Drawing.Paragraph>() ?? [])
                .Concat(slidePart.DiagramDataParts.SelectMany(diagram =>
                    diagram.DataModelRoot?.Descendants<Drawing.Paragraph>() ?? []));

            var lines = paragraphs.Select(paragraph => ParagraphText(paragraph, themeFonts)).ToList();
            var typed = lines.Any(line => !string.IsNullOrWhiteSpace(line));
            var method = TextExtractionMethod.Native;

            foreach (var relationshipId in PictureIds(slidePart))
            {
                var part = slidePart.TryGetPartById(relationshipId, out var found) ? found : null;

                if (await pictures.ReadAsync(part, $"slide {slideNumber}", cancellationToken) is { } recognized)
                {
                    lines.Add(recognized);
                    method = typed ? TextExtractionMethod.Mixed : TextExtractionMethod.Ocr;
                }
            }

            builder.AddSegment(slideNumber++, SegmentKind.Slide, string.Join(Environment.NewLine, lines), method);
        }

        if (slideNumber == 1)
        {
            builder.AddWarning("The presentation contains no slides.");
        }

        foreach (var warning in pictures.Warnings())
        {
            builder.AddWarning(warning);
        }

        if (budget.Warning() is { } unread)
        {
            builder.AddWarning(unread);
        }

        return builder.Build();
    }

    /// <summary>Relationship ids of the pictures on the slide, each once, in order.</summary>
    private static IEnumerable<string> PictureIds(SlidePart slidePart) =>
        (slidePart.Slide?.Descendants<Drawing.Blip>() ?? [])
            .Select(blip => blip.Embed?.Value)
            .OfType<string>()
            .Distinct();

    /// <summary>
    /// One paragraph's text, with Bijoy-font runs converted to Unicode. Runs are
    /// joined without separators: a run boundary can fall mid-word, so putting a
    /// line break there would split the word. A line break inside the paragraph
    /// (Shift+Enter) is an element of its own, not a character, and has to be
    /// turned back into one or the words either side of it are glued together.
    /// </summary>
    private static string ParagraphText(Drawing.Paragraph paragraph, ThemeFonts themeFonts)
    {
        var paragraphFont = LatinFontOf(paragraph.ParagraphProperties?.GetFirstChild<Drawing.DefaultRunProperties>());

        return BijoyConverter.Join(paragraph.Descendants()
            .Where(element => element is Drawing.Text or Drawing.Break)
            .Select(element =>
            {
                if (element is Drawing.Break)
                {
                    return new FontRun(Environment.NewLine, null);
                }

                var font = LatinFontOf(element.Parent?.GetFirstChild<Drawing.RunProperties>()) ?? paragraphFont;
                return new FontRun(((Drawing.Text)element).Text, themeFonts.Resolve(font));
            }));
    }

    private static string? LatinFontOf(OpenXmlElement? runProperties) =>
        runProperties?.GetFirstChild<Drawing.LatinFont>()?.Typeface?.Value;

    /// <summary>
    /// The theme's heading and body fonts. Text with no font of its own is drawn
    /// in one of these, and "+mj-lt" / "+mn-lt" refer to them by name - so a
    /// template whose theme font is SutonnyMJ makes all of its text Bijoy.
    ///
    /// Text with no font at all is taken to be body text. Titles inherit the
    /// heading font through the slide master, which is not resolved here; the
    /// cost is that a Bijoy title in a theme whose only Bijoy font is the heading
    /// font stays unconverted, which is far better than converting English body
    /// text that was never Bijoy.
    /// </summary>
    private sealed record ThemeFonts(string? Major, string? Minor)
    {
        public static ThemeFonts Of(SlidePart slidePart)
        {
            var scheme = slidePart.SlideLayoutPart?.SlideMasterPart?.ThemePart?.Theme?.ThemeElements?.FontScheme;

            return new ThemeFonts(
                scheme?.MajorFont?.LatinFont?.Typeface?.Value,
                scheme?.MinorFont?.LatinFont?.Typeface?.Value);
        }

        public string? Resolve(string? typeface) => typeface switch
        {
            null => Minor,
            _ when typeface.StartsWith("+mj", StringComparison.Ordinal) => Major,
            _ when typeface.StartsWith("+mn", StringComparison.Ordinal) => Minor,
            _ => typeface,
        };
    }

    private static IEnumerable<SlidePart> EnumerateSlidesInOrder(
        PresentationPart presentationPart,
        TextExtractionResultBuilder builder)
    {
        var slideIds = presentationPart.Presentation?.SlideIdList?.Elements<SlideId>().ToList();

        if (slideIds is null || slideIds.Count == 0)
        {
            // No slide-id list: fall back to package order, which is usually right
            // but is not guaranteed to be presentation order.
            foreach (var slidePart in presentationPart.SlideParts)
            {
                yield return slidePart;
            }

            yield break;
        }

        foreach (var slideId in slideIds)
        {
            if (slideId.RelationshipId?.Value is not { } relationshipId)
            {
                continue;
            }

            SlidePart? slidePart = null;

            try
            {
                slidePart = presentationPart.GetPartById(relationshipId) as SlidePart;
            }
            catch (ArgumentOutOfRangeException)
            {
                builder.AddWarning($"A slide referenced as '{relationshipId}' is missing from the package.");
            }

            if (slidePart is not null)
            {
                yield return slidePart;
            }
        }
    }
}
