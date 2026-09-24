using FluentAssertions;
using SmartPlagiarism.Engine.Similarity;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// Each algorithm on its own. The three headline cases — identical, disjoint, and
/// partially copied — are asserted for every one of them, because an algorithm
/// that fails any of those is not measuring similarity.
/// </summary>
public class SimilarityAlgorithmTests
{
    public static TheoryData<ISimilarityAlgorithm> AllAlgorithms() =>
    [
        new JaccardSimilarity(),
        new NGramOverlapSimilarity(),
        new TfIdfCosineSimilarity(UniformInverseDocumentFrequency.Instance),
        CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance),
    ];

    private static AnalyzedDocument Doc(string text, int id = 1) => new(id, text);

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void IdenticalDocuments_ScoreOne(ISimilarityAlgorithm algorithm)
    {
        const string text = "The winnowing algorithm selects the minimum hash in each window of the document.";

        algorithm.Compute(Doc(text), Doc(text, 2))
            .Should().BeApproximately(1.0, 0.0001, $"{algorithm.Name} must score a document against itself as identical");
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void DisjointDocuments_ScoreZero(ISimilarityAlgorithm algorithm)
    {
        var first = Doc("Photosynthesis converts sunlight into chemical energy inside chloroplasts.");
        var second = Doc("Quarterly revenue exceeded forecasts despite unfavourable currency movements.", 2);

        algorithm.Compute(first, second)
            .Should().BeApproximately(0.0, 0.0001, $"{algorithm.Name} must find nothing in common between unrelated texts");
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void PartiallyCopiedDocument_ScoresBetween(ISimilarityAlgorithm algorithm)
    {
        const string shared = "the fingerprint index shortlists candidate documents before any expensive comparison runs";

        var original = Doc($"{shared} and the remainder of this original report discusses evaluation methodology in detail");
        var copy = Doc($"{shared} while the rest of this second paper covers entirely unrelated laboratory procedures", 2);

        var score = algorithm.Compute(original, copy);

        score.Should().BeGreaterThan(0.05, $"{algorithm.Name} must notice the shared passage");
        score.Should().BeLessThan(0.95, $"{algorithm.Name} must not treat a partial copy as identical");
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void IsSymmetric(ISimilarityAlgorithm algorithm)
    {
        var first = Doc("shared opening words followed by material unique to the first document entirely");
        var second = Doc("shared opening words followed by different content belonging to the second one", 2);

        algorithm.Compute(first, second)
            .Should().BeApproximately(algorithm.Compute(second, first), 0.0001);
    }

    [Theory]
    [MemberData(nameof(AllAlgorithms))]
    public void EmptyDocument_ScoresZeroRatherThanNaN(ISimilarityAlgorithm algorithm)
    {
        var score = algorithm.Compute(Doc(""), Doc("some genuine content here", 2));

        score.Should().Be(0);
        double.IsNaN(score).Should().BeFalse("a NaN would poison every average built on top of it");
    }

    // ---------------- algorithm-specific behaviour ----------------

    [Fact]
    public void Jaccard_IgnoresWordOrder()
    {
        var forwards = Doc("alpha beta gamma delta");
        var backwards = Doc("delta gamma beta alpha", 2);

        new JaccardSimilarity().Compute(forwards, backwards)
            .Should().Be(1.0, "Jaccard compares vocabularies, not sequences");
    }

    [Fact]
    public void NGramOverlap_CaresAboutWordOrder()
    {
        var forwards = Doc("alpha beta gamma delta epsilon zeta");
        var backwards = Doc("zeta epsilon delta gamma beta alpha", 2);

        new NGramOverlapSimilarity().Compute(forwards, backwards)
            .Should().Be(0, "reversing the words destroys every three-word run");
    }

    [Fact]
    public void NGramOverlap_ShortQuotationInsideALongerDocumentScoresHigh()
    {
        // The documented consequence of using the overlap coefficient: the question
        // is how much of the smaller document appears in the larger.
        var quotation = Doc("winnowing selects the minimum hash in each window");
        var longer = Doc(
            "An introduction covering related work at some length. "
            + "winnowing selects the minimum hash in each window. "
            + "A conclusion covering evaluation and future work at similar length.",
            2);

        new NGramOverlapSimilarity().Compute(quotation, longer).Should().Be(1.0);
    }

    [Fact]
    public void TfIdf_WeightsRareSharedWordsAboveCommonOnes()
    {
        var corpus = new[]
        {
            Doc("the report describes the system", 10),
            Doc("the report describes the method", 11),
            Doc("the report describes the results", 12),
        };

        var idf = CorpusInverseDocumentFrequency.FromDocuments(corpus);

        // "the report describes" appears in every corpus document, so it is nearly
        // uninformative; "winnowing" is unique and should carry the match.
        var sharingRareTerm = new TfIdfCosineSimilarity(idf).Compute(
            Doc("winnowing fingerprints"),
            Doc("winnowing fingerprints", 2));

        var sharingCommonTerms = new TfIdfCosineSimilarity(idf).Compute(
            Doc("the report describes"),
            Doc("the report describes something else entirely different", 2));

        sharingRareTerm.Should().BeGreaterThan(sharingCommonTerms);
    }

    [Fact]
    public void Combined_ExplainReportsEveryComponentAndAgreesWithCompute()
    {
        var algorithm = CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance);
        var first = Doc("shared opening words followed by material unique to the first document");
        var second = Doc("shared opening words followed by other material in the second document", 2);

        var breakdown = algorithm.Explain(first, second);

        breakdown.Components.Should().ContainKeys("ngram-3", "tfidf-cosine", "jaccard");
        breakdown.Combined.Should().BeApproximately(algorithm.Compute(first, second), 0.0001);
    }

    [Fact]
    public void Combined_DefaultWeightsFavourVerbatimCopyingOverSharedVocabulary()
    {
        var algorithm = CombinedSimilarity.CreateDefault(UniformInverseDocumentFrequency.Instance);

        const string original = "the quick brown fox jumps over the lazy dog near the river bank";

        // Same words, order destroyed: vocabulary matches, verbatim runs do not.
        var reordered = Doc("bank river the near dog lazy the over jumps fox brown quick the", 2);

        // A different sentence that happens to contain a verbatim run.
        var verbatimRun = Doc("the quick brown fox jumps over the lazy dog while researchers observed", 3);

        var reorderedScore = algorithm.Compute(Doc(original), reordered);
        var verbatimScore = algorithm.Compute(Doc(original), verbatimRun);

        verbatimScore.Should().BeGreaterThan(
            reorderedScore,
            "the weights deliberately treat verbatim copying as the stronger evidence");
    }
}
