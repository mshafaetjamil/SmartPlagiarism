using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Extraction;

public class ExtractorFactoryTests
{
    private readonly IExtractorFactory _factory = ExtractorFactory.CreateDefault(new NullOcrService());

    [Theory]
    [InlineData("report.pdf", typeof(PdfTextExtractor))]
    [InlineData("thesis.docx", typeof(DocxTextExtractor))]
    [InlineData("defence.pptx", typeof(PptxTextExtractor))]
    public void Find_SelectsTheExtractorForTheExtension(string fileName, Type expected)
    {
        _factory.Find(fileName).Should().BeOfType(expected);
    }

    [Theory]
    [InlineData("REPORT.PDF")]
    [InlineData("Report.Pdf")]
    public void Find_IgnoresExtensionCasing(string fileName)
    {
        _factory.Find(fileName).Should().BeOfType<PdfTextExtractor>();
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData("pdf")]
    public void Find_AcceptsABareExtension(string extension)
    {
        _factory.Find(extension).Should().BeOfType<PdfTextExtractor>();
    }

    [Fact]
    public void Find_AcceptsAStoragePath()
    {
        // This is what ExtractionService actually passes.
        _factory.Find("ab/abcdef0123456789.docx").Should().BeOfType<DocxTextExtractor>();
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("noextension")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_UnsupportedFormat_ReturnsNull(string? fileName)
    {
        _factory.Find(fileName).Should().BeNull("the caller reports an unsupported format rather than guessing");
    }

    [Fact]
    public void SupportedExtensions_CoverTheUploadWhitelist()
    {
        _factory.SupportedExtensions.Should().BeEquivalentTo([".pdf", ".docx", ".pptx"]);
    }
}
