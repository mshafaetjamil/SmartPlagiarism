using System.Text;
using FluentAssertions;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Tests.Similarity;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Bijoy (SutonnyMJ) documents from the extractors' side: the font decides what
/// is converted, wherever the document sets it, and a converted Bijoy report
/// must match the same report typed in Unicode.
/// </summary>
public class BijoyExtractionTests
{
    /// <summary>
    /// শিক্ষার্থীদের প্রতিবেদন থেকে লেখা আলাদা করা হয় এবং তারপর তুলনা করা হয়। -
    /// as a SutonnyMJ typist enters it.
    /// </summary>
    private const string Bijoy = "wk¶v_©x‡`i cÖwZ‡e`b ‡_‡K ‡jLv Avjv`v Kiv nq Ges Zvici Zzjbv Kiv nq|";

    private static readonly string s_unicode =
        "শিক্ষার্থীদের প্রতিবেদন থেকে লেখা আলাদা করা হয় এবং তারপর তুলনা করা হয়।".Normalize(NormalizationForm.FormC);

    [Fact]
    public async Task Docx_RunsInASutonnyMjFont_AreConverted()
    {
        var docx = DocxFixture.Create([Bijoy], runFont: "SutonnyMJ");

        var result = await new DocxTextExtractor().ExtractAsync(new MemoryStream(docx));

        result.FullText.Trim().Should().Be(s_unicode);
    }

    [Fact]
    public async Task Docx_FontInheritedFromTheNormalStyle_IsConverted()
    {
        // A Bangladeshi template sets SutonnyMJ once, on Normal; no run names it.
        var docx = DocxFixture.Create([Bijoy], normalStyleFont: "SutonnyMJ");

        var result = await new DocxTextExtractor().ExtractAsync(new MemoryStream(docx));

        result.FullText.Trim().Should().Be(s_unicode);
    }

    [Fact]
    public async Task Docx_EnglishInALatinFont_IsLeftAlone()
    {
        // Avwg is "আমি" in Bijoy - but here it is set in Times New Roman, so it is
        // just letters, and must stay exactly as written.
        var docx = DocxFixture.Create(["Avwg 2026"], runFont: "Times New Roman");

        var result = await new DocxTextExtractor().ExtractAsync(new MemoryStream(docx));

        result.FullText.Trim().Should().Be("Avwg 2026");
    }

    [Fact]
    public async Task Pptx_RunsInASutonnyMjFont_AreConverted()
    {
        var pptx = PptxFixture.Create([[Bijoy]], latinFont: "SutonnyMJ");

        var result = await new PptxTextExtractor().ExtractAsync(new MemoryStream(pptx));

        result.FullText.Trim().Should().Be(s_unicode);
    }

    [Fact]
    public async Task BijoyReport_MatchesTheSameReportTypedInUnicode()
    {
        var bijoyDocx = DocxFixture.Create([Bijoy], runFont: "SutonnyMJ");
        var extracted = await new DocxTextExtractor().ExtractAsync(new MemoryStream(bijoyDocx));

        var corpus = new InMemoryCorpus();
        corpus.Add(1, s_unicode);

        var pipeline = new SimilarityPipeline(
            corpus,
            corpus,
            CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance));

        var report = await pipeline.AnalyzeAsync(new AnalyzedDocument(2, extracted.FullText));

        report.OverallSimilarityPercent.Should().Be(100m, "without conversion this would be 0%");
    }
}
