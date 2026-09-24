namespace SmartPlagiarism.Engine.Fingerprinting;

/// <param name="DocumentId">Host's identifier for the candidate document.</param>
/// <param name="SharedFingerprintCount">How many fingerprints it has in common.</param>
public readonly record struct FingerprintCandidate(int DocumentId, int SharedFingerprintCount);

/// <param name="Fingerprints">Distinct hashes of the document being analysed.</param>
/// <param name="NGramSize">
/// Winnowing parameters the fingerprints were generated with. Only stored sets
/// generated with the same parameters are comparable; hashes of 5-grams and of
/// 4-grams never coincide, so mixing them would silently find nothing.
/// </param>
/// <param name="WindowSize">Winnowing window, matched the same way as <paramref name="NGramSize"/>.</param>
/// <param name="MinimumSharedFingerprints">
/// Ignore documents sharing fewer than this. Filters out coincidental single-hash
/// collisions before anything expensive happens.
/// </param>
/// <param name="MaxCandidates">
/// Cap on the shortlist, most-shared first. Bounds the cost of the pairwise stage
/// no matter how large the corpus grows.
/// </param>
/// <param name="ExcludeDocumentIds">
/// Documents to leave out - the submission's own files, which would otherwise
/// match themselves perfectly.
/// </param>
public sealed record FingerprintQuery(
    IReadOnlySet<ulong> Fingerprints,
    int NGramSize,
    int WindowSize,
    int MinimumSharedFingerprints,
    int MaxCandidates,
    IReadOnlyCollection<int> ExcludeDocumentIds);

/// <summary>
/// Finds documents worth comparing in detail.
///
/// This is the reason the system scales. Without it, analysing one submission
/// against a corpus of N documents means N full pairwise comparisons; with it,
/// only documents that already share fingerprints are compared, which is a small
/// fraction of the corpus.
///
/// The Engine defines the contract and stays free of any storage concern; the
/// Infrastructure implementation reads DocumentFingerprint rows.
/// </summary>
public interface IFingerprintIndex
{
    /// <returns>Candidates ordered by shared-fingerprint count, descending.</returns>
    Task<IReadOnlyList<FingerprintCandidate>> FindCandidatesAsync(
        FingerprintQuery query,
        CancellationToken cancellationToken = default);
}
