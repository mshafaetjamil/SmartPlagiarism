using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SmartPlagiarism.Engine.Text;
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
/// Body text includes paragraphs inside tables. Headers, footers and footnotes are
/// separate parts and are not read: they carry boilerplate rather than the
/// student's own prose.
/// </summary>
public sealed class DocxTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".docx"];

    public async Task<TextExtractionResult> ExtractAsync(
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var bytes = await StreamHelpers.ReadAllBytesAsync(document, cancellationToken);
        var builder = new TextExtractionResultBuilder();

        using var buffer = new MemoryStream(bytes, writable: false);
        using var word = WordprocessingDocument.Open(buffer, isEditable: false);

        var body = word.MainDocumentPart?.Document?.Body;

        if (body is null)
        {
            builder.AddWarning("The document has no body part.");
            return builder.Build();
        }

        var fonts = new FontResolver(word.MainDocumentPart!.StyleDefinitionsPart?.Styles);
        var pageNumber = 1;
        var page = new StringBuilder();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = ParagraphText(paragraph, fonts);

            if (!string.IsNullOrWhiteSpace(text))
            {
                page.AppendLine(text);
            }

            if (!EndsPage(paragraph))
            {
                continue;
            }

            builder.AddSegment(pageNumber++, SegmentKind.Page, page.ToString(), TextExtractionMethod.Native);
            page.Clear();
        }

        // Whatever follows the last page break - or the whole body when there were none.
        if (page.Length > 0 || pageNumber == 1)
        {
            builder.AddSegment(pageNumber, SegmentKind.Page, page.ToString(), TextExtractionMethod.Native);
        }

        return builder.Build();
    }

    /// <summary>
    /// The paragraph's visible text, with Bijoy-font runs converted to Unicode.
    ///
    /// Only the paragraph's own text elements are read. A text box's paragraphs
    /// nest inside the paragraph that anchors it and are visited in their own
    /// right, so reading them here too would count their text twice. Deleted
    /// tracked-change text and field codes are separate element types and are
    /// never read.
    /// </summary>
    private static string ParagraphText(Paragraph paragraph, FontResolver fonts) =>
        BijoyConverter.Join(paragraph.Descendants<WordText>()
            .Where(text => text.Ancestors<Paragraph>().FirstOrDefault() == paragraph)
            .Select(text => new FontRun(
                text.Text,
                text.Parent is Run run ? fonts.Resolve(run, paragraph) : null)));

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
