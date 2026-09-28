using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using Drawing = DocumentFormat.OpenXml.Drawing;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Builds real .pptx packages in memory.
///
/// Deliberately minimal: a presentation part, a slide-id list and the slides
/// themselves. No slide master or layout, which PowerPoint would want but the
/// extractor does not read - the point is to exercise slide ordering and text.
/// </summary>
internal static class PptxFixture
{
    /// <summary>One entry per slide; each entry lists that slide's text boxes.</summary>
    /// <param name="latinFont">Font set directly on every run, as picking a font in PowerPoint does.</param>
    internal static byte[] Create(IReadOnlyList<IReadOnlyList<string>> slides, string? latinFont = null)
    {
        using var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            var slideIdList = new SlideIdList();
            presentationPart.Presentation = new Presentation(slideIdList);

            var slideId = 256U;

            foreach (var textBoxes in slides)
            {
                var slidePart = presentationPart.AddNewPart<SlidePart>();
                slidePart.Slide = BuildSlide(textBoxes, latinFont);

                slideIdList.Append(new SlideId
                {
                    Id = slideId++,
                    RelationshipId = presentationPart.GetIdOfPart(slidePart),
                });
            }

            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// A one-slide deck whose text box holds the given DrawingML paragraphs
    /// (the a prefix is declared), for constructs such as line breaks.
    /// </summary>
    /// <param name="smartArtParagraphs">Text for a SmartArt diagram on the slide, if any.</param>
    /// <param name="picture">A PNG placed on the slide beside the text box, if any.</param>
    internal static byte[] FromParagraphXml(
        string paragraphsXml,
        IReadOnlyList<string>? smartArtParagraphs = null,
        byte[]? picture = null)
    {
        using var stream = new MemoryStream();

        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            var slidePart = presentationPart.AddNewPart<SlidePart>();

            var pictureXml = picture is null
                ? string.Empty
                : """
                  <p:pic><p:nvPicPr><p:cNvPr id="3" name="Picture"/><p:cNvPicPr/><p:nvPr/></p:nvPicPr>
                    <p:blipFill><a:blip r:embed="rIdPicture"/></p:blipFill><p:spPr/></p:pic>
                  """;

            Write(slidePart, $"""
                <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                  xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                  xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <p:cSld><p:spTree><p:sp><p:txBody><a:bodyPr/>{paragraphsXml}</p:txBody></p:sp>{pictureXml}</p:spTree></p:cSld>
                </p:sld>
                """);

            if (picture is not null)
            {
                using var imageStream = slidePart.AddImagePart(ImagePartType.Png, "rIdPicture").GetStream(FileMode.Create);
                imageStream.Write(picture);
            }

            if (smartArtParagraphs is not null)
            {
                Write(slidePart.AddNewPart<DiagramDataPart>(), SmartArtXml.DataModel(smartArtParagraphs));
            }

            presentationPart.Presentation = new Presentation(new SlideIdList(new SlideId
            {
                Id = 256U,
                RelationshipId = presentationPart.GetIdOfPart(slidePart),
            }));
            presentationPart.Presentation.Save();
        }

        return stream.ToArray();
    }

    private static void Write(OpenXmlPart part, string xml)
    {
        using var writer = new StreamWriter(part.GetStream(FileMode.Create));
        writer.Write(xml);
    }

    private static Slide BuildSlide(IReadOnlyList<string> textBoxes, string? latinFont)
    {
        var shapeTree = new ShapeTree(
            new NonVisualGroupShapeProperties(
                new NonVisualDrawingProperties { Id = 1U, Name = string.Empty },
                new NonVisualGroupShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties()),
            new GroupShapeProperties());

        var shapeId = 2U;

        foreach (var text in textBoxes)
        {
            shapeTree.Append(new Shape(
                new NonVisualShapeProperties(
                    new NonVisualDrawingProperties { Id = shapeId++, Name = "TextBox" },
                    new NonVisualShapeDrawingProperties(),
                    new ApplicationNonVisualDrawingProperties()),
                new ShapeProperties(),
                new TextBody(
                    new Drawing.BodyProperties(),
                    new Drawing.ListStyle(),
                    new Drawing.Paragraph(BuildRun(text, latinFont)))));
        }

        return new Slide(new CommonSlideData(shapeTree), new ColorMapOverride(new Drawing.MasterColorMapping()));
    }

    private static Drawing.Run BuildRun(string text, string? latinFont) =>
        latinFont is null
            ? new Drawing.Run(new Drawing.Text(text))
            : new Drawing.Run(
                new Drawing.RunProperties(new Drawing.LatinFont { Typeface = latinFont }),
                new Drawing.Text(text));
}
