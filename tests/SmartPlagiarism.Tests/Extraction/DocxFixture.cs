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
    /// <param name="runFont">Font set directly on every run, as picking a font in Word does.</param>
    /// <param name="normalStyleFont">
    /// Font of the default paragraph style, as a template sets it; runs then carry
    /// no font of their own.
    /// </param>
    internal static byte[] Create(
        IReadOnlyList<string> paragraphs,
        int? pageBreakAfterParagraph = null,
        string? runFont = null,
        string? normalStyleFont = null)
    {
        using var stream = new MemoryStream();

        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = word.AddMainDocumentPart();
            var body = new Body();

            if (normalStyleFont is not null)
            {
                mainPart.AddNewPart<StyleDefinitionsPart>().Styles = new Styles(new Style(
                    new StyleName { Val = "Normal" },
                    new StyleRunProperties(new RunFonts { Ascii = normalStyleFont, HighAnsi = normalStyleFont }))
                {
                    Type = StyleValues.Paragraph,
                    StyleId = "Normal",
                    Default = true,
                });
            }

            for (var index = 0; index < paragraphs.Count; index++)
            {
                var run = new Run(new Text(paragraphs[index]) { Space = SpaceProcessingModeValues.Preserve });

                if (runFont is not null)
                {
                    run.PrependChild(new RunProperties(new RunFonts { Ascii = runFont, HighAnsi = runFont }));
                }

                var children = new List<OpenXmlElement> { run };

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
