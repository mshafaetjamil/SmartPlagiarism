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

    /// <summary>
    /// A document whose body is the given WordprocessingML, for constructs the
    /// SDK builders make awkward - text boxes with their VML fallback, breaks,
    /// tabs. The w, mc, wps, wp, a, pic, r and v prefixes are declared.
    /// </summary>
    /// <param name="smartArtParagraphs">Text for a SmartArt diagram part, if any.</param>
    /// <param name="picture">A PNG the body can show with <see cref="PictureXml"/>.</param>
    internal static byte[] FromBodyXml(string bodyXml, IReadOnlyList<string>? smartArtParagraphs = null, byte[]? picture = null)
    {
        using var stream = new MemoryStream();

        using (var word = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = word.AddMainDocumentPart();
            Write(mainPart, $"""
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                  xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                  xmlns:wps="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"
                  xmlns:wp="http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:v="urn:schemas-microsoft-com:vml"
                  xmlns:pic="http://schemas.openxmlformats.org/drawingml/2006/picture"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"
                  mc:Ignorable="wps"><w:body>{bodyXml}</w:body></w:document>
                """);

            if (picture is not null)
            {
                using var imageStream = mainPart.AddImagePart(ImagePartType.Png, PictureId).GetStream(FileMode.Create);
                imageStream.Write(picture);
            }

            if (smartArtParagraphs is not null)
            {
                Write(mainPart.AddNewPart<DiagramDataPart>(), SmartArtXml.DataModel(smartArtParagraphs));
            }
        }

        return stream.ToArray();
    }

    /// <summary>A run showing the fixture's picture, as Insert > Pictures makes one.</summary>
    internal const string PictureXml = $"""
        <w:r><w:drawing><wp:inline><wp:extent cx="3000000" cy="2000000"/><wp:docPr id="1" name="Picture 1"/>
          <a:graphic><a:graphicData uri="http://schemas.openxmlformats.org/drawingml/2006/picture">
            <pic:pic><pic:nvPicPr><pic:cNvPr id="0" name="picture.png"/><pic:cNvPicPr/></pic:nvPicPr>
              <pic:blipFill><a:blip r:embed="{PictureId}"/></pic:blipFill><pic:spPr/></pic:pic>
          </a:graphicData></a:graphic></wp:inline></w:drawing></w:r>
        """;

    private const string PictureId = "rIdPicture";

    private static void Write(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create));
        writer.Write(xml);
    }
}

/// <summary>The part SmartArt keeps its text in, shared by the DOCX and PPTX fixtures.</summary>
internal static class SmartArtXml
{
    internal static string DataModel(IReadOnlyList<string> paragraphs)
    {
        var points = string.Concat(paragraphs.Select((text, index) =>
            $"""<dgm:pt modelId="{index + 1}"><dgm:t><a:bodyPr/><a:p><a:r><a:t>{text}</a:t></a:r></a:p></dgm:t></dgm:pt>"""));

        return $"""
            <dgm:dataModel xmlns:dgm="http://schemas.openxmlformats.org/drawingml/2006/diagram"
              xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <dgm:ptLst>{points}</dgm:ptLst><dgm:cxnLst/><dgm:bg/><dgm:whole/>
            </dgm:dataModel>
            """;
    }
}
