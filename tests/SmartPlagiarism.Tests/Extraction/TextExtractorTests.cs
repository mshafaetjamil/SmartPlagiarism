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
    private static readonly IPdfPageRenderer s_renderer = new PdfiumPageRenderer();

    // ---------------- PDF ----------------

    [Fact]
    public async Task Pdf_ExtractsTextFromEveryPage()
    {
        var pdf = PdfFixture.Create(
            "Winnowing based similarity detection",
            "Chapter two discusses the fingerprint index");
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

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
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

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
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

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
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("little or no embedded text");
    }

    [Fact]
    public async Task Pdf_MixedPages_OnlyWarnsAboutTheEmptyOne()
    {
        var pdf = PdfFixture.Create("A page with a genuine paragraph of body text on it", string.Empty);
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(2);
        result.Segments[0].Text.Should().NotBeEmpty();
        result.Segments[1].Text.Should().BeEmpty();
        result.Warnings.Should().ContainSingle().Which.Should().Contain("Page 2");
    }

    [Fact]
    public async Task Pdf_LineBreak_KeepsTheWordsOnEitherSideApart()
    {
        // Nothing in the file separates the lines; "report" and "body" were glued.
        var pdf = PdfFixture.CreateLines("my final year report", "body text continues here");
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.NormalizedText.Should().Be("my final year report body text continues here");
        result.WordCount.Should().Be(8);
    }

    [Fact]
    public async Task Pdf_WordsPlacedWithoutSpaceGlyphs_AreStillSeparated()
    {
        // LaTeX output: every word positioned on its own, no space characters at all.
        var pdf = PdfFixture.CreateWithoutSpaceGlyphs("the quick brown fox jumps over the lazy dog");
        var extractor = new PdfTextExtractor(s_noOcr, s_renderer);

        using var stream = new MemoryStream(pdf);
        var result = await extractor.ExtractAsync(stream);

        result.NormalizedText.Should().Be("the quick brown fox jumps over the lazy dog");
        result.WordCount.Should().Be(9);
    }

    [Fact]
    public async Task Pdf_AndDocx_OfTheSameText_ReadTheSameWords()
    {
        // Copying a report between formats must not change what is compared.
        string[] lines =
        [
            "Winnowing selects a subset of the hashes",
            "so that matching passages share fingerprints",
            "even when the surrounding text differs.",
        ];

        using var pdfStream = new MemoryStream(PdfFixture.CreateLines(lines));
        using var docxStream = new MemoryStream(DocxFixture.Create(lines));

        var fromPdf = await new PdfTextExtractor(s_noOcr, s_renderer).ExtractAsync(pdfStream);
        var fromDocx = await new DocxTextExtractor(s_noOcr).ExtractAsync(docxStream);

        fromPdf.NormalizedText.Should().Be(fromDocx.NormalizedText);
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
        var extractor = new DocxTextExtractor(s_noOcr);

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
        var extractor = new DocxTextExtractor(s_noOcr);

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
        var extractor = new DocxTextExtractor(s_noOcr);

        using var stream = new MemoryStream(docx);
        var result = await extractor.ExtractAsync(stream);

        result.Segments.Should().HaveCount(2);
        result.Segments[0].Text.Should().Contain("before");
        result.Segments[1].Text.Should().Contain("after");
    }

    [Fact]
    public async Task Docx_LineBreaksCarriageReturnsAndTabs_SeparateWords()
    {
        // Shift+Enter, a carriage return and a tab are elements, not characters.
        var docx = DocxFixture.FromBodyXml("""
            <w:p><w:r><w:t>breakleft</w:t></w:r><w:r><w:br/></w:r><w:r><w:t>breakright</w:t></w:r></w:p>
            <w:p><w:r><w:t>returnleft</w:t><w:cr/><w:t>returnright</w:t></w:r></w:p>
            <w:p><w:r><w:t>tableft</w:t></w:r><w:r><w:tab/></w:r><w:r><w:t>tabright</w:t></w:r></w:p>
            """);

        using var stream = new MemoryStream(docx);
        var result = await new DocxTextExtractor(s_noOcr).ExtractAsync(stream);

        result.NormalizedText.Should().Be("breakleft breakright returnleft returnright tableft tabright");
    }

    [Fact]
    public async Task Docx_TextBox_IsReadOnceNotFromItsVmlFallbackToo()
    {
        var docx = DocxFixture.FromBodyXml("""
            <w:p><w:r><w:t>anchor paragraph</w:t></w:r><w:r>
              <mc:AlternateContent>
                <mc:Choice Requires="wps"><w:drawing><wp:anchor><a:graphic><a:graphicData uri="http://schemas.microsoft.com/office/word/2010/wordprocessingShape"><wps:wsp><wps:txbx><w:txbxContent><w:p><w:r><w:t>text box sentence</w:t></w:r></w:p></w:txbxContent></wps:txbx></wps:wsp></a:graphicData></a:graphic></wp:anchor></w:drawing></mc:Choice>
                <mc:Fallback><w:pict><v:shape><v:textbox><w:txbxContent><w:p><w:r><w:t>text box sentence</w:t></w:r></w:p></w:txbxContent></v:textbox></v:shape></w:pict></mc:Fallback>
              </mc:AlternateContent>
            </w:r></w:p>
            """);

        using var stream = new MemoryStream(docx);
        var result = await new DocxTextExtractor(s_noOcr).ExtractAsync(stream);

        result.NormalizedText.Should().Be("anchor paragraph text box sentence");
    }

    [Fact]
    public async Task Docx_SmartArtText_IsRead()
    {
        var docx = DocxFixture.FromBodyXml(
            "<w:p><w:r><w:t>Body text.</w:t></w:r></w:p>",
            smartArtParagraphs: ["Collect submissions", "Compare fingerprints"]);

        using var stream = new MemoryStream(docx);
        var result = await new DocxTextExtractor(s_noOcr).ExtractAsync(stream);

        result.NormalizedText.Should().Be("body text collect submissions compare fingerprints");
    }

    // ---------------- PPTX ----------------

    [Fact]
    public async Task Pptx_LineBreakInsideATextBox_SeparatesWords()
    {
        var pptx = PptxFixture.FromParagraphXml(
            "<a:p><a:r><a:t>breakleft</a:t></a:r><a:br/><a:r><a:t>breakright</a:t></a:r></a:p>");

        using var stream = new MemoryStream(pptx);
        var result = await new PptxTextExtractor(s_noOcr).ExtractAsync(stream);

        result.NormalizedText.Should().Be("breakleft breakright");
    }

    [Fact]
    public async Task Pptx_SmartArtText_IsReadWithItsSlide()
    {
        var pptx = PptxFixture.FromParagraphXml(
            "<a:p><a:r><a:t>Method</a:t></a:r></a:p>",
            smartArtParagraphs: ["Extract text", "Winnow fingerprints"]);

        using var stream = new MemoryStream(pptx);
        var result = await new PptxTextExtractor(s_noOcr).ExtractAsync(stream);

        result.Segments.Should().ContainSingle();
        result.NormalizedText.Should().Be("method extract text winnow fingerprints");
    }

    [Fact]
    public async Task Pptx_ExtractsOneSegmentPerSlideInOrder()
    {
        var pptx = PptxFixture.Create(
        [
            ["Title slide", "Winnowing based detection"],
            ["Method", "N-gram fingerprints"],
            ["Results"],
        ]);
        var extractor = new PptxTextExtractor(s_noOcr);

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
        var extractor = new PptxTextExtractor(s_noOcr);

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
        var extractor = new DocxTextExtractor(s_noOcr);

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
        var extractor = new DocxTextExtractor(s_noOcr);

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
