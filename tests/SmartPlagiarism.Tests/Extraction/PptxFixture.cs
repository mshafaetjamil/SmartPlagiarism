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
    internal static byte[] Create(IReadOnlyList<IReadOnlyList<string>> slides)
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
                slidePart.Slide = BuildSlide(textBoxes);

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

    private static Slide BuildSlide(IReadOnlyList<string> textBoxes)
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
                    new Drawing.Paragraph(new Drawing.Run(new Drawing.Text(text))))));
        }

        return new Slide(new CommonSlideData(shapeTree), new ColorMapOverride(new Drawing.MasterColorMapping()));
    }
}
