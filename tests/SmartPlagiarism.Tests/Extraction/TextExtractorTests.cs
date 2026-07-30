using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Extraction against real documents built by the fixtures. OCR is deliberately
/// unavailable throughout, which is exactly the situation on a machine without
/// Tesseract installed - the engine must still deliver text and report the gap.
/// </summary>
public class TextExtractorTests
{
    private static readonly IOcrService s_noOcr = new NullOcrService("no tessdata in tests");

    // ---------------- PDF ----------------

    [Fact]
    public async Task Pdf_ExtractsTextFromEveryPage()
    {
        var pdf = PdfFixture.Create(
            "Winnowing based similarity detection",
            "Chapter two discusses the fingerprint index");
        var extractor = new PdfTextExtractor(s_noOcr);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(2);
        result.Segments.Select(segment => segment.Number).Should().Equal(1, 2);
        result.Segments.Should().OnlyContain(segment => segment.Kind == SegmentKind.Page);
        result.FullText.Should().Contain("Winnowing").And.Contain("fingerprint");
        result.Method.Should().Be(TextExtractionMethod.Native);
        result.WordCount.Should().BeGreaterThan(5);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Pdf_SegmentOffsetsPointAtTheRightPlaceInFullText()
    {
        var pdf = PdfFixture.Create("First page body", "Second page body");
        var extractor = new PdfTextExtractor(s_noOcr);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        foreach (var segment in result.Segments)
        {
            result.FullText.Substring(segment.StartOffset, segment.Length)
                .Should().Be(segment.Text, "an offset that does not round-trip would misplace every highlight");
        }
    }

    [Fact]
    public async Task Pdf_PageWithNoTextLayerAndNoOcr_WarnsInsteadOfThrowing()
    {
        // An empty page is what a scanned page looks like: no text layer at all.
        var pdf = PdfFixture.Create(string.Empty);
        var extractor = new PdfTextExtractor(s_noOcr);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Method.Should().Be(TextExtractionMethod.Native);
        result.IsEmpty.Should().BeTrue();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("OCR is unavailable").And.Contain("no tessdata in tests");
    }

    [Fact]
    public async Task Pdf_SparsePageIsTreatedAsHavingNoTextLayer()
    {
        // Comfortably under the twenty-character threshold.
        var pdf = PdfFixture.Create("p. 7");
        var extractor = new PdfTextExtractor(s_noOcr);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("little or no embedded text");
    }

    [Fact]
    public async Task Pdf_MixedPages_OnlyWarnsAboutTheEmptyOne()
    {
        var pdf = PdfFixture.Create("A page with a genuine paragraph of body text on it", string.Empty);
        var extractor = new PdfTextExtractor(s_noOcr);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(2);
        result.Segments[0].Text.Should().NotBeEmpty();
        result.Segments[1].Text.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Page 2");
    }

    // ---------------- DOCX ----------------

    [Fact]
    public async Task Docx_ExtractsEveryParagraph()
    {
        var docx = DocxFixture.Create(
        [
            "Chapter 1: Introduction",
            "This draft describes the detection pipeline.",
            "It closes with an evaluation.",
        ]);
        var extractor = new DocxTextExtractor();

        using var stream = new MemoryStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.FullText.Should().Contain("Introduction")
            .And.Contain("detection pipeline")
            .And.Contain("evaluation");
        result.Method.Should().Be(TextExtractionMethod.Native);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Docx_WithoutPageBreaks_IsOneSegment()
    {
        var docx = DocxFixture.Create(["Only paragraph."]);
        var extractor = new DocxTextExtractor();

        using var stream = new MemoryStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().ContainSingle(
            "a .docx records no pagination, so inventing page numbers would be a lie");
        result.Segments[0].Number.Should().Be(1);
    }

    [Fact]
    public async Task Docx_ExplicitPageBreak_StartsANewSegment()
    {
        var docx = DocxFixture.Create(
            ["Text before the break.", "Text after the break."],
            pageBreakAfterParagraph: 0);
        var extractor = new DocxTextExtractor();

        using var stream = new MemoryStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(2);
        result.Segments[0].Text.Should().Contain("before");
        result.Segments[1].Text.Should().Contain("after");
    }

    // ---------------- PPTX ----------------

    [Fact]
    public async Task Pptx_ExtractsOneSegmentPerSlideInOrder()
    {
        var pptx = PptxFixture.Create(
        [
            ["Title slide", "Winnowing based detection"],
            ["Method", "N-gram fingerprints"],
            ["Results"],
        ]);
        var extractor = new PptxTextExtractor();

        using var stream = new MemoryStream(pptx);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(3);
        result.Segments.Should().OnlyContain(segment => segment.Kind == SegmentKind.Slide);
        result.Segments.Select(segment => segment.Number).Should().Equal(1, 2, 3);
        result.Segments[0].Text.Should().Contain("Title slide").And.Contain("Winnowing");
        result.Segments[1].Text.Should().Contain("N-gram");
        result.Segments[2].Text.Should().Contain("Results");
        result.Method.Should().Be(TextExtractionMethod.Native);
    }

    [Fact]
    public async Task Pptx_WithNoSlides_WarnsAndReturnsEmpty()
    {
        var pptx = PptxFixture.Create([]);
        var extractor = new PptxTextExtractor();

        using var stream = new MemoryStream(pptx);
        var result = await extractor.ExtractAsync(stream);

        result.IsEmpty.Should().BeTrue();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("no slides");
    }

    // ---------------- shared behaviour ----------------

    [Fact]
    public async Task NormalizedTextIsFoldedWhileFullTextKeepsTheOriginal()
    {
        var docx = DocxFixture.Create(["The Cost Was $40, Wasn't It?"]);
        var extractor = new DocxTextExtractor();

        using var stream = new MemoryStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.FullText.Should().Contain("The Cost Was $40, Wasn't It?",
            "highlighting in Phase 7 needs the text exactly as written");
        result.NormalizedText.Should().Be("the cost was 40 wasn t it");
    }

    [Fact]
    public async Task ExtractorsAcceptAForwardOnlyStream()
    {
        // The real caller hands over a FileStream, not a MemoryStream.
        var docx = DocxFixture.Create(["Body text."]);
        var extractor = new DocxTextExtractor();

        using var stream = new ForwardOnlyStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.FullText.Should().Contain("Body text.");
    }

    /// <summary>A non-seekable stream, like a network or pipe source.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
