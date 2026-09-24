using SmartPlagiarism.Engine.Matching;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>How a match was arrived at, so a report can explain itself.</summary>
public enum MatchKind
{
    /// <summary>Byte-identical file, found by SHA-256 before any analysis ran.</summary>
    ExactDuplicate = 0,

    /// <summary>Shortlisted by fingerprints, then scored by the algorithms.</summary>
    Analyzed = 1,
}

/// <param name="DocumentId">The matched document.</param>
/// <param name="SimilarityPercent">Combined score against it, 0-100.</param>
/// <param name="SharedFingerprintCount">Fingerprints in common; 0 for exact duplicates.</param>
/// <param name="ComponentScores">Each algorithm's own ratio, for explaining the number.</param>
/// <param name="Fragments">The passages the two documents share.</param>
public sealed record DocumentMatch(
    int DocumentId,
    decimal SimilarityPercent,
    MatchKind Kind,
    int SharedFingerprintCount,
    IReadOnlyDictionary<string, double> ComponentScores,
    IReadOnlyList<MatchedFragment> Fragments);

/// <summary>
/// The outcome of analysing one document against the corpus.
/// </summary>
/// <param name="OverallSimilarityPercent">
/// How much of this document was found elsewhere, 0-100. This is coverage across
/// <em>all</em> matches, not the single highest score - a document assembled from
/// three sources at 30% each has been almost entirely copied, and reporting 30%
/// would understate that badly.
/// </param>
/// <param name="Matches">Per-document matches, highest similarity first.</param>
/// <param name="CandidatesConsidered">Documents the fingerprint index shortlisted.</param>
/// <param name="CandidatesCompared">Shortlisted documents that had text to compare.</param>
public sealed record SimilarityReport(
    decimal OverallSimilarityPercent,
    IReadOnlyList<DocumentMatch> Matches,
    int CandidatesConsidered,
    int CandidatesCompared)
{
    public static SimilarityReport Empty { get; } = new(0m, [], 0, 0);
}
