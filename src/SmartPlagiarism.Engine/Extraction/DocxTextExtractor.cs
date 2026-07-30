using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

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

        var pageNumber = 1;
        var page = new StringBuilder();

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var text = paragraph.InnerText;

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

    private static bool EndsPage(Paragraph paragraph) =>
        paragraph.Descendants<Break>().Any(@break =>
            @break.Type is not null && @break.Type.Value == BreakValues.Page);
}
