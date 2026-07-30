using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
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
/// against notes the audience never saw would be misleading in a report.
/// </summary>
public sealed class PptxTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pptx"];

    public async Task<TextExtractionResult> ExtractAsync(
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var bytes = await StreamHelpers.ReadAllBytesAsync(document, cancellationToken);
        var builder = new TextExtractionResultBuilder();

        using var buffer = new MemoryStream(bytes, writable: false);
        using var presentation = PresentationDocument.Open(buffer, isEditable: false);

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

            var text = string.Join(
                Environment.NewLine,
                slidePart.Slide?.Descendants<Drawing.Text>().Select(node => node.Text) ?? []);

            builder.AddSegment(slideNumber++, SegmentKind.Slide, text, TextExtractionMethod.Native);
        }

        if (slideNumber == 1)
        {
            builder.AddWarning("The presentation contains no slides.");
        }

        return builder.Build();
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
