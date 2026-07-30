using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>Builds real .docx packages in memory.</summary>
internal static class DocxFixture
{
    /// <param name="pageBreakAfterParagraph">
    /// Zero-based index of the paragraph to follow with an explicit page break, or
    /// null for a document with no breaks at all.
    /// </param>
    internal static byte[] Create(IReadOnlyList<string> paragraphs, int? pageBreakAfterParagraph = null)
    {
        using var stream = new MemoryStream();

        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = word.AddMainDocumentPart();
            var body = new Body();

            for (var index = 0; index < paragraphs.Count; index++)
            {
                var children = new List<OpenXmlElement>
                {
                    new Run(new Text(paragraphs[index]) { Space = SpaceProcessingModeValues.Preserve }),
                };

                if (pageBreakAfterParagraph == index)
                {
                    children.Add(new Run(new Break { Type = BreakValues.Page }));
                }

                body.AppendChild(new Paragraph(children));
            }

            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
