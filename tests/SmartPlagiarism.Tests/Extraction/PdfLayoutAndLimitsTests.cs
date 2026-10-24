using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Tests.Ocr;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// How a PDF page is laid out and routed: columns, running headers, pages that
/// are mostly a picture, and the limits that keep one document from costing the
/// server more than it should.
/// </summary>
public class PdfLayoutAndLimitsTests
{
    private const string PictureWords =
        "The survey was carried out in three districts and the results show that most households "
        + "rely on groundwater for drinking while a smaller share use rainwater harvesting systems";

    private static readonly IOcrService s_noOcr = new NullOcrService("no tessdata in tests");
    private static readonly IPdfPageRenderer s_renderer = new PdfiumPageRenderer();

    [Fact]
    public async Task TwoColumnsDrawnLineByLineAcross_AreReadOneColumnAtATime()
    {
        var pdf = PdfFixture.CreateTwoColumnsDrawnAcross(
            ["the left column starts", "and its sentence runs", "down to this line"],
            ["the right column holds", "a separate sentence", "that ends here"]);

        var result = await Extract(pdf);

        result.NormalizedText.Should().Be(
            "the left column starts and its sentence runs down to this line "
            + "the right column holds a separate sentence that ends here");
    }

    [Fact]
    public async Task SingleColumnPage_KeepsItsOrder()
    {
        var pdf = PdfFixture.CreateLines("first line of the page", "second line of the page", "third line of the page");

        var result = await Extract(pdf);

        result.NormalizedText.Should().Be("first line of the page second line of the page third line of the page");
    }

    [Fact]
    public async Task RunningHeaderAndPageNumbers_AreRemoved()
    {
        var pdf = PdfFixture.CreatePages(
        [
            ["Final Year Report - Department of CSE", "Chapter one introduces the problem.", "Page 1 of 4"],
            ["Final Year Report - Department of CSE", "Chapter two reviews earlier work.", "Page 2 of 4"],
            ["Final Year Report - Department of CSE", "Chapter three presents the method.", "Page 3 of 4"],
            ["Final Year Report - Department of CSE", "Chapter four evaluates it.", "Page 4 of 4"],
        ]);

        var result = await Extract(pdf);

        result.NormalizedText.Should().Be(
            "chapter one introduces the problem chapter two reviews earlier work "
            + "chapter three presents the method chapter four evaluates it");
    }

    [Fact]
    public async Task LinesRepeatedOnTooFewPages_AreKept()
    {
        var pdf = PdfFixture.CreatePages(
        [
            ["Summary", "The first page."],
            ["Summary", "The second page."],
        ]);

        var result = await Extract(pdf);

        result.NormalizedText.Should().Be("summary the first page summary the second page");
    }

    [Fact]
    public async Task PageThatIsMostlyAPicture_IsReadByOcrDespiteItsHeading()
    {
        // The heading is a text layer long enough to pass for a text page.
        var pdf = PdfFixture.CreatePicturePage("Figure 3: Results of the household survey", PngFixture.Create());
        var ocr = new ScriptedOcrService("Figure 3: Results of the household survey " + PictureWords);

        var result = await new PdfTextExtractor(ocr, s_renderer).ExtractAsync(new MemoryStream(pdf));

        ocr.CallCount.Should().Be(1);
        result.Method.Should().Be(TextExtractionMethod.Ocr);
        result.FullText.Should().Contain("groundwater");
    }

    [Fact]
    public async Task PictureWhereOcrFindsLittle_KeepsTheTextLayer()
    {
        var pdf = PdfFixture.CreatePicturePage("Figure 4: Photograph of the field site", PngFixture.Create());
        var ocr = new ScriptedOcrService("Figure 4: Photograph of the field site");

        var result = await new PdfTextExtractor(ocr, s_renderer).ExtractAsync(new MemoryStream(pdf));

        result.Method.Should().Be(TextExtractionMethod.Native);
        result.NormalizedText.Should().Be("figure 4 photograph of the field site");
    }

    [Fact]
    public async Task PicturePageWithoutOcr_WarnsThatThePictureWasNotRead()
    {
        var pdf = PdfFixture.CreatePicturePage("Figure 3: Results of the household survey", PngFixture.Create());

        var result = await Extract(pdf);

        result.Warnings.Should().ContainSingle().Which.Should().Contain("mostly a picture").And.Contain("OCR is unavailable");
    }

    [Fact]
    public async Task OcrTimeBudgetUsedUp_SkipsThePageAndSaysSo()
    {
        var pdf = PdfFixture.Create(string.Empty, string.Empty, string.Empty);
        var ocr = new ScriptedOcrService(PictureWords);
        var extractor = new PdfTextExtractor(ocr, s_renderer, new ExtractionLimits { OcrTimeBudget = TimeSpan.Zero });

        var result = await extractor.ExtractAsync(new MemoryStream(pdf));

        ocr.CallCount.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("OCR stopped").And.Contain("pages 1-3");
    }

    [Fact]
    public async Task PagesBeyondTheLimit_AreNotReadAndTheWarningSaysHowMany()
    {
        var pdf = PdfFixture.Create("A first page with enough words on it", "A second page with enough words on it", "A third page with enough words on it");
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer, new ExtractionLimits { MaxPdfPages = 2 });

        var result = await extractor.ExtractAsync(new MemoryStream(pdf));

        result.Segments.Should().HaveCount(2);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("3 pages").And.Contain("first 2");
    }

    [Fact]
    public async Task ScanThatCannotBeDrawn_IsReadFromItsEmbeddedImage()
    {
        var pdf = PdfFixture.CreatePicturePage("Scan", PngFixture.Create());
        var ocr = new ScriptedOcrService(PictureWords);

        var result = await new PdfTextExtractor(ocr, new UnrenderableRenderer()).ExtractAsync(new MemoryStream(pdf));

        ocr.CallCount.Should().Be(1);
        result.FullText.Should().Contain("groundwater");
    }

    [Fact]
    public async Task EmbeddedImageOverThePixelLimit_IsNotDecodedForOcr()
    {
        var pdf = PdfFixture.CreatePicturePage("Scan", PngFixture.Create(width: 120, height: 120));
        var ocr = new ScriptedOcrService(PictureWords);
        var extractor = new PdfTextExtractor(ocr, new UnrenderableRenderer(), new ExtractionLimits { MaxPicturePixels = 10_000 });

        var result = await extractor.ExtractAsync(new MemoryStream(pdf));

        ocr.CallCount.Should().Be(0);
        result.Warnings.Should().Contain(warning => warning.Contains("120 x 120 pixels is larger than the 0.01-megapixel limit"));
    }

    [Fact]
    public void Renderer_DrawsALargePageSmallerRatherThanAtFullResolution()
    {
        var pdf = PdfFixture.Create("An A4 page is 3508 pixels tall at 300 DPI");

        var rendered = new PdfiumPageRenderer(maxPixels: 500).RenderPng(pdf, 1);

        rendered.Succeeded.Should().BeTrue(rendered.Error);
        var (width, height) = PngFixture.SizeOf(rendered.Png!);
        Math.Max(width, height).Should().BeLessThanOrEqualTo(500);
    }

    private static Task<TextExtractionResult> Extract(byte[] pdf) =>
        new PdfTextExtractor(s_noOcr, s_renderer).ExtractAsync(new MemoryStream(pdf));

    /// <summary>Stands in for PDFium failing, which sends a scan to its embedded images.</summary>
    private sealed class UnrenderableRenderer : IPdfPageRenderer
    {
        public PageRenderResult RenderPng(byte[] pdf, int pageNumber) =>
            PageRenderResult.Failure("the page could not be rendered in this test.");
    }
}
