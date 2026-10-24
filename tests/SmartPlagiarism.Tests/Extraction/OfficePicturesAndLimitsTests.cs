using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Tests.Ocr;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Text inside pictures in DOCX and PPTX files, and the size limits that keep a
/// ZIP-based file from unpacking into more than the server can hold.
/// </summary>
public class OfficePicturesAndLimitsTests
{
    private const string ScreenshotText =
        "Winnowing selects the minimum hash in every window of consecutive hashes so that matching passages share fingerprints";

    private static readonly IOcrService s_noOcr = new NullOcrService("no tessdata in tests");

    [Fact]
    public async Task Docx_PictureOfText_IsReadWithTheParagraphAroundIt()
    {
        var docx = DocxFixture.FromBodyXml(
            $"<w:p><w:r><w:t>The screenshot below shows the algorithm.</w:t></w:r>{DocxFixture.PictureXml}</w:p>",
            picture: PngFixture.Create());
        var ocr = new ScriptedOcrService(ScreenshotText);

        var result = await new DocxTextExtractor(ocr).ExtractAsync(new MemoryStream(docx));

        ocr.CallCount.Should().Be(1);
        result.FullText.Should().Contain("The screenshot below").And.Contain("minimum hash");
        result.Method.Should().Be(TextExtractionMethod.Mixed);
    }

    [Fact]
    public async Task Docx_PictureWithoutOcr_IsReportedAsUnread()
    {
        var docx = DocxFixture.FromBodyXml(
            $"<w:p><w:r><w:t>Scanned page follows.</w:t></w:r>{DocxFixture.PictureXml}</w:p>",
            picture: PngFixture.Create());

        var result = await new DocxTextExtractor(s_noOcr).ExtractAsync(new MemoryStream(docx));

        result.Warnings.Should().ContainSingle().Which.Should().Contain("1 picture(s)").And.Contain("OCR is unavailable");
    }

    [Fact]
    public async Task Docx_PictureWithOnlyAFewWords_IsTreatedAsNoise()
    {
        var docx = DocxFixture.FromBodyXml(
            $"<w:p><w:r><w:t>Our logo.</w:t></w:r>{DocxFixture.PictureXml}</w:p>",
            picture: PngFixture.Create());

        var result = await new DocxTextExtractor(new ScriptedOcrService("ACME Ltd")).ExtractAsync(new MemoryStream(docx));

        result.NormalizedText.Should().Be("our logo");
        result.Method.Should().Be(TextExtractionMethod.Native);
    }

    [Fact]
    public async Task Pptx_SlideOfScreenshots_IsReadByOcr()
    {
        var pptx = PptxFixture.FromParagraphXml(string.Empty, picture: PngFixture.Create());
        var ocr = new ScriptedOcrService(ScreenshotText);

        var result = await new PptxTextExtractor(ocr).ExtractAsync(new MemoryStream(pptx));

        result.FullText.Should().Contain("minimum hash");
        result.Method.Should().Be(TextExtractionMethod.Ocr);
    }

    [Fact]
    public async Task Docx_PictureOverThePixelLimit_IsNotDecodedForOcr()
    {
        var docx = DocxFixture.FromBodyXml(
            $"<w:p><w:r><w:t>The diagram below shows the algorithm.</w:t></w:r>{DocxFixture.PictureXml}</w:p>",
            picture: PngFixture.Create(width: 120, height: 120));
        var ocr = new ScriptedOcrService(ScreenshotText);
        var extractor = new DocxTextExtractor(ocr, new ExtractionLimits { MaxPicturePixels = 10_000 });

        var result = await extractor.ExtractAsync(new MemoryStream(docx));

        ocr.CallCount.Should().Be(0);
        result.FullText.Should().NotContain("minimum hash");
        result.Warnings.Should().ContainSingle().Which.Should().Contain("1 picture(s) are larger than the 0.01-megapixel limit");
    }

    [Fact]
    public async Task Pptx_PictureOverThePixelLimit_IsNotDecodedForOcr()
    {
        var pptx = PptxFixture.FromParagraphXml(string.Empty, picture: PngFixture.Create(width: 120, height: 120));
        var ocr = new ScriptedOcrService(ScreenshotText);
        var extractor = new PptxTextExtractor(ocr, new ExtractionLimits { MaxPicturePixels = 10_000 });

        var result = await extractor.ExtractAsync(new MemoryStream(pptx));

        ocr.CallCount.Should().Be(0);
        result.Warnings.Should().Contain(warning => warning.Contains("1 picture(s) are larger than the 0.01-megapixel limit"));
    }

    [Fact]
    public async Task Docx_PictureThatCannotBeMeasured_IsNotDecodedForOcr()
    {
        // Big enough not to be taken for an icon, but in no format a header can be read from.
        var notAPicture = new byte[20 * 1024];
        new Random(7).NextBytes(notAPicture);
        notAPicture[0] = 0;

        var docx = DocxFixture.FromBodyXml(
            $"<w:p><w:r><w:t>The diagram below shows the algorithm.</w:t></w:r>{DocxFixture.PictureXml}</w:p>",
            picture: notAPicture);
        var ocr = new ScriptedOcrService(ScreenshotText);

        var result = await new DocxTextExtractor(ocr).ExtractAsync(new MemoryStream(docx));

        ocr.CallCount.Should().Be(0);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("damaged or in an unrecognised format");
    }

    [Fact]
    public async Task Docx_ThatUnpacksBeyondTheLimit_IsRefusedWithAReason()
    {
        var docx = DocxFixture.Create(["Enough text to unpack to more than a hundred bytes of XML."]);
        var extractor = new DocxTextExtractor(s_noOcr, new ExtractionLimits { MaxUnpackedOfficeBytes = 100 });

        var extract = () => extractor.ExtractAsync(new MemoryStream(docx));

        (await extract.Should().ThrowAsync<DocumentTooLargeException>()).Which.Message.Should().Contain("unpacks to");
    }

    [Fact]
    public async Task Pptx_WithTooManyParts_IsRefusedWithAReason()
    {
        var pptx = PptxFixture.Create([["One slide"]]);
        var extractor = new PptxTextExtractor(s_noOcr, new ExtractionLimits { MaxOfficePackageEntries = 2 });

        var extract = () => extractor.ExtractAsync(new MemoryStream(pptx));

        (await extract.Should().ThrowAsync<DocumentTooLargeException>()).Which.Message.Should().Contain("internal parts");
    }
}
