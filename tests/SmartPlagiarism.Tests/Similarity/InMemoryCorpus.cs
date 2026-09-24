using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Similarity;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// A corpus held in memory, standing in for the EF-backed index and reader.
///
/// It implements the same two ports the real pipeline uses and applies the same
/// rules — minimum shared fingerprints, candidate cap, exclusions — so pipeline
/// tests exercise the real orchestration without a database.
/// </summary>
internal sealed class InMemoryCorpus : IFingerprintIndex, ICorpusReader
{
    private readonly Dictionary<int, AnalyzedDocument> _documents = [];
    private readonly Dictionary<int, FingerprintSet> _fingerprints = [];

    /// <summary>Documents the fingerprint index was asked about, for asserting on shortlisting.</summary>
    public int FindCandidatesCallCount { get; private set; }

    /// <summary>Documents actually loaded for detailed comparison.</summary>
    public List<int> LoadedDocumentIds { get; } = [];

    public AnalyzedDocument Add(int id, string text, string? contentHash = null)
    {
        var document = new AnalyzedDocument(id, text, contentHash);

        _documents[id] = document;
        _fingerprints[id] = Winnower.Fingerprint(document.Tokens);

        return document;
    }

    public Task<IReadOnlyList<FingerprintCandidate>> FindCandidatesAsync(
        FingerprintQuery query,
        CancellationToken cancellationToken = default)
    {
        FindCandidatesCallCount++;

        var excluded = query.ExcludeDocumentIds.ToHashSet();
        var candidates = new List<FingerprintCandidate>();

        foreach (var (documentId, set) in _fingerprints)
        {
            if (excluded.Contains(documentId)
                || set.NGramSize != query.NGramSize
                || set.WindowSize != query.WindowSize)
            {
                continue;
            }

            var shared = set.DistinctHashes.Count(query.Fingerprints.Contains);

            if (shared >= query.MinimumSharedFingerprints)
            {
                candidates.Add(new FingerprintCandidate(documentId, shared));
            }
        }

        candidates.Sort((left, right) => right.SharedFingerprintCount.CompareTo(left.SharedFingerprintCount));

        IReadOnlyList<FingerprintCandidate> result = query.MaxCandidates > 0 && candidates.Count > query.MaxCandidates
            ? candidates[..query.MaxCandidates]
            : candidates;

        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<int>> FindByContentHashAsync(
        string contentHash,
        IReadOnlyCollection<int> excludeDocumentIds,
        CancellationToken cancellationToken = default)
    {
        var excluded = excludeDocumentIds.ToHashSet();

        IReadOnlyList<int> matches = _documents.Values
            .Where(document => document.ContentHash == contentHash && !excluded.Contains(document.Id))
            .Select(document => document.Id)
            .ToList();

        return Task.FromResult(matches);
    }

    public Task<AnalyzedDocument?> LoadAsync(int documentId, CancellationToken cancellationToken = default)
    {
        LoadedDocumentIds.Add(documentId);

        return Task.FromResult(_documents.GetValueOrDefault(documentId));
    }
}
