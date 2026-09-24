using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Engine.Text;
using SmartPlagiarism.Tests.Ocr;
using SmartPlagiarism.Tests.Similarity;
using Xunit.Abstractions;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// A Bangla PDF produced by a real shaping engine (macOS CoreText), whose
/// embedded text is damaged the way Bangla PDFs usually are: vowel signs in
/// drawing order and conjunct glyphs with no Unicode mapping.
///
/// The requirement these pin down is that a Bangla PDF never silently scores 0%:
/// it is either read correctly (by OCR) or the teacher is told why it was not.
/// </summary>
public class BanglaPdfExtractionTests(ITestOutputHelper output)
{
    /// <summary>What the fixture PDF says, as typed.</summary>
    private const string TypedText =
        "প্লেজিয়ারিজম শনাক্তকরণ ব্যবস্থা শিক্ষার্থীদের জমা দেওয়া প্রতিবেদনগুলোকে পূর্বের সকল জমার সাথে "
        + "তুলনা করে এবং মিলে যাওয়া অংশগুলো চিহ্নিত করে। এই পদ্ধতিতে প্রথমে লেখা থেকে শব্দ আলাদা করা হয় "
        + "এবং তারপর প্রতিটি শব্দগুচ্ছের জন্য একটি হ্যাশ মান তৈরি করা হয়।";

    private const string UnrelatedBangla =
        "নদীমাতৃক এই দেশের কৃষি ব্যবস্থা বর্ষাকালের বৃষ্টির উপর অনেকাংশে নির্ভরশীল। "
        + "ধান চাষের জন্য প্রচুর পানির প্রয়োজন হয় এবং কৃষকেরা সেচের উপর নির্ভর করেন।";

    private static readonly IPdfPageRenderer s_renderer = new PdfiumPageRenderer();

    private static Stream Fixture() =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Extraction", "Fixtures", "bangla-coretext.pdf"));

    [Fact]
    public async Task DamagedBanglaLayer_WithoutOcr_KeepsRepairedTextAndWarns()
    {
        await using var pdf = Fixture();

        var result = await new PdfTextExtractor(new NullOcrService("not installed"), s_renderer).ExtractAsync(pdf);

        result.Method.Should().Be(TextExtractionMethod.Native);
        result.FullText.Should().NotBeNullOrWhiteSpace();
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Bangla").And.Contain("OCR is unavailable").And.Contain("DOCX");
    }

    [Fact]
    public async Task DamagedBanglaLayer_IsReplacedByCleanerOcrText()
    {
        var ocr = new ScriptedOcrService(TypedText);
        await using var pdf = Fixture();

        var result = await new PdfTextExtractor(ocr, s_renderer).ExtractAsync(pdf);

        ocr.CallCount.Should().Be(1, "the page must be drawn and read once");
        result.Method.Should().Be(TextExtractionMethod.Ocr);
        result.FullText.Trim().Should().Be(TypedText);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("read by OCR");
    }

    [Fact]
    public async Task OcrThatReadsWorse_DoesNotReplaceTheEmbeddedText()
    {
        // Every word starts with a vowel sign: worse than anything the PDF holds.
        var garbage = string.Join(' ', Enumerable.Repeat("িা", 20));
        await using var pdf = Fixture();

        var result = await new PdfTextExtractor(new ScriptedOcrService(garbage), s_renderer).ExtractAsync(pdf);

        result.Method.Should().Be(TextExtractionMethod.Native);
        result.Warnings.Should().ContainSingle().Which.Should().Contain("OCR did no better");
    }

    [Fact]
    public async Task CleanBanglaDocx_IsNeverSentToOcr()
    {
        // DOCX stores Unicode as typed, so nothing about Bangla there needs repair.
        var docx = DocxFixture.Create([TypedText]);

        var result = await new DocxTextExtractor().ExtractAsync(new MemoryStream(docx));

        result.FullText.Trim().Should().Be(TypedText);
        BengaliText.Assess(result.FullText).IsDamaged.Should().BeFalse();
    }

    /// <summary>
    /// End to end with the real Tesseract, when this machine has it: the copied
    /// Bangla paragraph in the PDF must be found in the typed original. Without
    /// Tesseract and the ben/eng data this returns early - the scripted tests
    /// above still cover every decision.
    /// </summary>
    [Fact]
    public async Task WithRealOcr_CopiedBanglaParagraphInPdf_MatchesTheTypedOriginal()
    {
        var tessData = FindTessData();
        var options = new OcrOptions { TessDataPath = tessData ?? string.Empty, Language = "ben+eng" };
        using var ocr = new FallbackOcrService([new TesseractOcrService(options), new TesseractCliOcrService(options)]);

        if (!ocr.IsAvailable)
        {
            output.WriteLine($"Skipped: OCR unavailable here ({ocr.UnavailableReason}).");
            return;
        }

        await using var pdf = Fixture();
        var extracted = await new PdfTextExtractor(ocr, s_renderer).ExtractAsync(pdf);

        output.WriteLine($"method={extracted.Method}");
        output.WriteLine(extracted.FullText);

        var corpus = new InMemoryCorpus();
        corpus.Add(1, $"{UnrelatedBangla} {TypedText}");
        corpus.Add(2, UnrelatedBangla);

        var pipeline = new SimilarityPipeline(
            corpus,
            corpus,
            CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance));

        var query = new AnalyzedDocument(99, extracted.FullText);
        var report = await pipeline.AnalyzeAsync(query);

        output.WriteLine($"overall={report.OverallSimilarityPercent}%");

        foreach (var match in report.Matches)
        {
            output.WriteLine($"doc {match.DocumentId}: {match.SimilarityPercent}% fragments={match.Fragments.Count}");
        }

        extracted.Method.Should().Be(TextExtractionMethod.Ocr);
        report.Matches.Should().ContainSingle().Which.DocumentId.Should().Be(1);

        // The whole PDF is the copied paragraph. Read from its embedded text this
        // scored 0%; OCR measured 80% with Tesseract 5.5 and tessdata_best. The
        // margin absorbs small differences between Tesseract versions.
        report.OverallSimilarityPercent.Should().BeGreaterThan(50m);
        report.Matches[0].Fragments.Should().NotBeEmpty("the copied passage must be highlightable");
    }

    /// <summary>The Web project's tessdata, found by walking up to the solution root.</summary>
    private static string? FindTessData()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SmartPlagiarism.sln")))
            {
                var tessData = Path.Combine(directory.FullName, "src", "SmartPlagiarism.Web", "App_Data", "tessdata");
                return Directory.Exists(tessData) ? tessData : null;
            }
        }

        return null;
    }
}
