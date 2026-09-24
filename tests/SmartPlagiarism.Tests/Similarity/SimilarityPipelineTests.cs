using FluentAssertions;
using SmartPlagiarism.Engine.Similarity;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// The three-stage pipeline end to end: exact hash, fingerprint shortlist,
/// pairwise scoring. The tests assert not only the scores but that the expensive
/// stage was skipped when it should have been — that skipping is the entire point
/// of the design.
/// </summary>
public class SimilarityPipelineTests
{
    private const string OriginalReport =
        "The fingerprint index shortlists candidate documents so that the expensive pairwise "
        + "comparison only runs against a handful of documents rather than the entire corpus. "
        + "Winnowing selects the minimum hash value within each sliding window of consecutive "
        + "fingerprints, which guarantees that any sufficiently long shared passage will be found.";

    private const string UnrelatedReport =
        "Photosynthesis converts light energy into chemical energy stored in carbohydrate molecules. "
        + "The process occurs inside chloroplasts and depends on chlorophyll absorbing particular "
        + "wavelengths, driving carbon fixation through a cycle of enzyme catalysed reactions.";

    private static SimilarityPipeline Pipeline(InMemoryCorpus corpus, SimilarityOptions? options = null) =>
        new(corpus,
            corpus,
            CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance),
            options);

    // ---------------- stage 1: exact duplicates ----------------

    [Fact]
    public async Task IdenticalFile_IsCaughtByHashWithoutAnyComparison()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport, contentHash: "abc123");

        var resubmission = new AnalyzedDocument(2, OriginalReport, contentHash: "abc123");

        var report = await Pipeline(corpus).AnalyzeAsync(resubmission);

        report.OverallSimilarityPercent.Should().Be(100m);
        report.Matches.Should().ContainSingle();
        report.Matches[0].Kind.Should().Be(MatchKind.ExactDuplicate);

        corpus.FindCandidatesCallCount.Should().Be(
            0, "an identical hash settles it — fingerprinting and scoring are wasted work");
        corpus.LoadedDocumentIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ExcludedDocuments_AreNotReportedAsDuplicatesOfThemselves()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport, contentHash: "abc123");

        var itself = new AnalyzedDocument(1, OriginalReport, contentHash: "abc123");

        var report = await Pipeline(corpus).AnalyzeAsync(itself, excludeDocumentIds: [1]);

        report.Matches.Should().BeEmpty();
    }

    // ---------------- stage 2: shortlisting ----------------

    [Fact]
    public async Task OnlyShortlistedDocumentsAreEverLoaded()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);

        for (var id = 2; id <= 20; id++)
        {
            corpus.Add(id, $"{UnrelatedReport} Document number {id} discusses distinct laboratory findings.");
        }

        var suspicious = new AnalyzedDocument(99, $"A new opening paragraph. {OriginalReport}");

        var report = await Pipeline(corpus).AnalyzeAsync(suspicious);

        report.Matches.Should().ContainSingle().Which.DocumentId.Should().Be(1);
        corpus.LoadedDocumentIds.Should().Equal([1],
            "the other nineteen documents must never be loaded, let alone compared");
    }

    [Fact]
    public async Task NothingSharedWithTheCorpus_ScoresZero()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);

        var report = await Pipeline(corpus).AnalyzeAsync(new AnalyzedDocument(2, UnrelatedReport));

        report.OverallSimilarityPercent.Should().Be(0m);
        report.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task CandidateCap_BoundsTheExpensiveStage()
    {
        var corpus = new InMemoryCorpus();

        for (var id = 1; id <= 10; id++)
        {
            corpus.Add(id, OriginalReport);
        }

        var options = new SimilarityOptions { MaxCandidates = 3 };
        var report = await Pipeline(corpus, options).AnalyzeAsync(new AnalyzedDocument(99, OriginalReport));

        report.CandidatesConsidered.Should().Be(3);
        corpus.LoadedDocumentIds.Should().HaveCount(3, "the cap must hold however many documents match");
    }

    [Fact]
    public async Task FingerprintsFromDifferentWinnowingParameters_AreNeverCompared()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);

        // The corpus was fingerprinted with the default 5-grams; this pipeline was
        // reconfigured to 4-grams. The index must not treat those sets as comparable.
        var options = new SimilarityOptions { FingerprintNGramSize = 4 };
        var report = await Pipeline(corpus, options).AnalyzeAsync(new AnalyzedDocument(99, OriginalReport));

        report.CandidatesConsidered.Should().Be(0);
        corpus.LoadedDocumentIds.Should().BeEmpty();
    }

    // ---------------- stage 3: scoring ----------------

    [Fact]
    public async Task IdenticalTextWithADifferentHash_ScoresOneHundred()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport, contentHash: "hash-of-the-pdf");

        // Same words, different file — a DOCX of the same report, say.
        var retyped = new AnalyzedDocument(2, OriginalReport, contentHash: "hash-of-the-docx");

        var report = await Pipeline(corpus).AnalyzeAsync(retyped);

        report.OverallSimilarityPercent.Should().Be(100m);
        report.Matches[0].Kind.Should().Be(MatchKind.Analyzed, "this was found by analysis, not by hash");
        report.Matches[0].SimilarityPercent.Should().Be(100m);
    }

    [Fact]
    public async Task OneCopiedParagraph_ScoresBetweenAndLocatesTheParagraph()
    {
        const string ownWork =
            "This dissertation begins with an entirely original discussion of the problem domain, "
            + "covering motivation, scope and the constraints imposed by the university timetable, "
            + "before turning to a survey of existing tools and their respective shortcomings.";

        var corpus = new InMemoryCorpus();
        var source = corpus.Add(1, OriginalReport);

        var partialCopy = new AnalyzedDocument(2, $"{ownWork} {OriginalReport}");

        var report = await Pipeline(corpus).AnalyzeAsync(partialCopy);

        report.Matches.Should().ContainSingle();
        var match = report.Matches[0];

        match.SimilarityPercent.Should().BeInRange(20m, 95m,
            "a document that is part original and part copied is neither clean nor identical");

        // The located fragment must read back as the copied paragraph in both documents.
        match.Fragments.Should().NotBeEmpty();
        var fragment = match.Fragments[0];

        partialCopy.Text[fragment.TargetStart..fragment.TargetEnd].Should().Contain("fingerprint index shortlists");
        source.Text[fragment.SourceStart..fragment.SourceEnd].Should().Contain("fingerprint index shortlists");

        fragment.TargetStart.Should().BeGreaterThan(
            ownWork.Length - 10, "the copied passage starts after the student's own writing");
    }

    [Fact]
    public async Task OverallScoreIsCoverageAcrossEveryMatchNotJustTheHighest()
    {
        const string firstSource = "the fingerprint index shortlists candidate documents before expensive "
            + "pairwise comparison begins against the corpus of stored student work";
        const string secondSource = "winnowing selects the minimum hash value within each sliding window of "
            + "consecutive fingerprints taken across the whole normalised document";

        var corpus = new InMemoryCorpus();
        corpus.Add(1, firstSource);
        corpus.Add(2, secondSource);

        // Assembled entirely from two sources — neither alone accounts for it.
        var assembled = new AnalyzedDocument(99, $"{firstSource} {secondSource}");

        var report = await Pipeline(corpus).AnalyzeAsync(assembled);

        report.Matches.Should().HaveCount(2);

        var highestSingleMatch = report.Matches.Max(match => match.SimilarityPercent);

        report.OverallSimilarityPercent.Should().BeGreaterThan(highestSingleMatch,
            "coverage across both sources must exceed either source on its own");
        report.OverallSimilarityPercent.Should().BeGreaterThan(90m);
    }

    [Fact]
    public async Task MatchesAreOrderedBySimilarityDescending()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);
        corpus.Add(2, $"{OriginalReport} {UnrelatedReport} plus a great deal of additional unrelated padding text");

        var report = await Pipeline(corpus).AnalyzeAsync(new AnalyzedDocument(99, OriginalReport));

        report.Matches.Should().HaveCountGreaterThan(1);
        report.Matches.Should().BeInDescendingOrder(match => match.SimilarityPercent);
    }

    [Fact]
    public async Task EmptyDocument_IsHandledWithoutAnyWork()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);

        var report = await Pipeline(corpus).AnalyzeAsync(new AnalyzedDocument(2, ""));

        report.OverallSimilarityPercent.Should().Be(0m);
        report.Matches.Should().BeEmpty();
        corpus.FindCandidatesCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ComponentScoresAreCarriedThroughForExplaining()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);

        var report = await Pipeline(corpus).AnalyzeAsync(new AnalyzedDocument(2, OriginalReport));

        report.Matches[0].ComponentScores.Should().ContainKeys("ngram-3", "tfidf-cosine", "jaccard");
    }

    [Fact]
    public async Task CandidateWithNoExtractedText_IsSkippedNotCounted()
    {
        var corpus = new InMemoryCorpus();
        corpus.Add(1, OriginalReport);
        corpus.Add(2, string.Empty);

        var report = await Pipeline(corpus).AnalyzeAsync(new AnalyzedDocument(99, OriginalReport));

        report.Matches.Should().OnlyContain(match => match.DocumentId == 1);
        report.CandidatesCompared.Should().BeLessThanOrEqualTo(report.CandidatesConsidered);
    }
}
