using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Text;
using Drawing = DocumentFormat.OpenXml.Drawing;
using Vml = DocumentFormat.OpenXml.Vml;
using WordText = DocumentFormat.OpenXml.Wordprocessing.Text;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Extracts text from Word documents with the Open XML SDK.
///
/// On segmentation: a .docx stores no pagination - Word decides where pages break
/// when it lays the document out, and that information is not in the file. What is
/// in the file is explicit page breaks, so those are used as segment boundaries.
/// A document with no explicit breaks comes back as a single segment, which is
/// honest about what the format can tell us.
///
/// Body text includes paragraphs inside tables and text boxes, and SmartArt text,
/// which lives in a part of its own and is added after the body. Headers,
/// footers and footnotes are separate parts and are not read: they carry
/// boilerplate rather than the student's own prose.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    private readonly IOcrService _ocr;
    private readonly ExtractionLimits _limits;

    /// <param name="ocr">Reads the text in pictures pasted into the document.</param>
    public DocxTextExtractor(IOcrService ocr, ExtractionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(ocr);

        _ocr = ocr;
        _limits = limits ?? new ExtractionLimits();
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx"];

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
        using var word = WordprocessingDocument.Open(buffer, isEditable: false, settings);

        var mainPart = word.MainDocumentPart;

        if (mainPart?.Document?.Body is not { } body)
        {
            builder.AddWarning("The document has no body part.");
            return builder.Build();
        }

        var fonts = new FontResolver(mainPart.StyleDefinitionsPart?.Styles);
        var pageNumber = 1;
        var page = new PageBuilder();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Word saves every text box twice: the drawing itself, and inside
            // mc:Fallback a VML copy for Word 2007. Only the first is read, or
            // text-box text would count double.
            if (paragraph.Ancestors<AlternateContentFallback>().Any())
            {
                continue;
            }

            page.AddNative(ParagraphText(paragraph, fonts));

            foreach (var relationshipId in PictureIds(paragraph))
            {
                var part = mainPart.TryGetPartById(relationshipId, out var found) ? found : null;
                page.AddRecognized(await pictures.ReadAsync(part, $"page {pageNumber}", cancellationToken));
            }

            if (!EndsPage(paragraph))
            {
                continue;
            }

            page.AddTo(builder, pageNumber++);
        }

        // SmartArt keeps its text in a part of its own, not in the body.
        foreach (var diagram in mainPart.DiagramDataParts)
        {
            AppendDiagramText(page, diagram);
        }

        // Whatever follows the last page break - or the whole body when there were none.
        if (!page.IsEmpty || pageNumber == 1)
        {
            page.AddTo(builder, pageNumber);
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

    /// <summary>
    /// Relationship ids of the pictures in the paragraph's own runs: DrawingML
    /// pictures, and VML images in documents from older versions of Word. Those
    /// inside a text box belong to the text box's paragraphs, and those in
    /// mc:Fallback are copies of a drawing already counted.
    /// </summary>
    private static IEnumerable<string> PictureIds(Paragraph paragraph) =>
        paragraph.Descendants()
            .Where(element => element is Drawing.Blip or Vml.ImageData
                && element.Ancestors<Paragraph>().FirstOrDefault() == paragraph
                && !element.Ancestors<AlternateContentFallback>().Any())
            .Select(element => element switch
            {
                Drawing.Blip blip => blip.Embed?.Value,
                Vml.ImageData image => image.RelationshipId?.Value,
                _ => null,
            })
            .OfType<string>();

    /// <summary>
    /// The paragraph's visible text, with Bijoy-font runs converted to Unicode.
    ///
    /// Only the paragraph's own runs are read. A text box's paragraphs nest
    /// inside the paragraph that anchors it and are visited in their own right,
    /// so reading them here too would count their text twice. Deleted
    /// tracked-change text and field codes are separate element types and are
    /// never read.
    /// </summary>
    private static string ParagraphText(Paragraph paragraph, FontResolver fonts) =>
        BijoyConverter.Join(paragraph.Descendants<Run>()
            .Where(run => run.Ancestors<Paragraph>().FirstOrDefault() == paragraph)
            .SelectMany(run => RunContent(run, fonts.Resolve(run, paragraph))));

    /// <summary>
    /// A run's text in order. Line breaks (Shift+Enter), carriage returns and
    /// tabs are elements of their own rather than characters in the text, so
    /// they have to be turned back into whitespace here - dropping them would
    /// glue the words on either side together.
    /// </summary>
    private static IEnumerable<FontRun> RunContent(Run run, string? font)
    {
        foreach (var element in run.ChildElements)
        {
            switch (element)
            {
                case WordText text:
                    yield return new FontRun(text.Text, font);
                    break;
                case Break or CarriageReturn:
                    yield return new FontRun(Environment.NewLine, null);
                    break;
                case TabChar or PositionalTab:
                    yield return new FontRun("\t", null);
                    break;
                case NoBreakHyphen:
                    yield return new FontRun("-", null);
                    break;
            }
        }
    }

    private static void AppendDiagramText(PageBuilder page, DiagramDataPart diagram)
    {
        foreach (var paragraph in diagram.DataModelRoot?.Descendants<Drawing.Paragraph>() ?? [])
        {
            page.AddNative(string.Concat(paragraph.Descendants<Drawing.Text>().Select(text => text.Text)));
        }
    }

    /// <summary>
    /// One segment's text as it is gathered, and whether it came from the
    /// document's text, from pictures read by OCR, or both.
    /// </summary>
    private sealed class PageBuilder
    {
        private readonly StringBuilder _text = new();
        private bool _native;
        private bool _recognized;

        public bool IsEmpty => _text.Length == 0;

        public void AddNative(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _text.AppendLine(line);
                _native = true;
            }
        }

        public void AddRecognized(string? line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                _text.AppendLine(line);
                _recognized = true;
            }
        }

        public void AddTo(TextExtractionResultBuilder builder, int pageNumber)
        {
            var method = _recognized
                ? _native ? TextExtractionMethod.Mixed : TextExtractionMethod.Ocr
                : TextExtractionMethod.Native;

            builder.AddSegment(pageNumber, SegmentKind.Page, _text.ToString(), method);
            _text.Clear();
            _native = _recognized = false;
        }
    }

    private static bool EndsPage(Paragraph paragraph) =>
        paragraph.Descendants<Break>().Any(@break =>
            @break.Type is not null && @break.Type.Value == BreakValues.Page);

    /// <summary>
    /// Works out which font a run is displayed in, following Word's inheritance:
    /// the run's own properties, then its character style, then its paragraph
    /// style and that style's ancestors, then the document defaults. Bijoy
    /// documents usually get their font from the Normal style of a Bangladeshi
    /// template, not from each run, so stopping at the run would miss most of them.
    /// </summary>
    private sealed class FontResolver
    {
        /// <summary>Style chains are short; this only guards against a malformed cycle.</summary>
        private const int MaximumStyleDepth = 16;

        private readonly Dictionary<string, Style> _styles;
        private readonly string? _defaultParagraphStyleId;
        private readonly string? _documentDefaultFont;

        public FontResolver(Styles? styles)
        {
            var all = styles?.Elements<Style>().ToList() ?? [];

            _styles = all
                .Where(style => style.StyleId?.Value is not null)
                .GroupBy(style => style.StyleId!.Value!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            _defaultParagraphStyleId = all
                .FirstOrDefault(style => style.Type?.Value == StyleValues.Paragraph && style.Default?.Value == true)
                ?.StyleId?.Value;

            _documentDefaultFont = FontOf(styles?.DocDefaults?.RunPropertiesDefault?.RunPropertiesBaseStyle?.RunFonts);
        }

        public string? Resolve(Run run, Paragraph paragraph) =>
            FontOf(run.RunProperties?.RunFonts)
            ?? StyleFont(run.RunProperties?.RunStyle?.Val?.Value)
            ?? StyleFont(paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraphStyleId)
            ?? _documentDefaultFont;

        private string? StyleFont(string? styleId)
        {
            for (var depth = 0; styleId is not null && depth < MaximumStyleDepth; depth++)
            {
                if (!_styles.TryGetValue(styleId, out var style))
                {
                    return null;
                }

                if (FontOf(style.StyleRunProperties?.RunFonts) is { } font)
                {
                    return font;
                }

                styleId = style.BasedOn?.Val?.Value;
            }

            return null;
        }

        /// <summary>
        /// Word draws ASCII with the "ascii" font and the Windows-1252 symbols
        /// Bijoy also uses (‡, ©, ¨) with the "hAnsi" font, so either naming a
        /// Bijoy font means the run is Bijoy.
        /// </summary>
        private static string? FontOf(RunFonts? fonts)
        {
            var ascii = fonts?.Ascii?.Value;
            var highAnsi = fonts?.HighAnsi?.Value;

            return BijoyConverter.IsBijoyFont(highAnsi) && !BijoyConverter.IsBijoyFont(ascii)
                ? highAnsi
                : ascii ?? highAnsi;
        }
    }
}
