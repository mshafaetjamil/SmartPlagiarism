using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Similarity;

/// <summary>
/// Fingerprint shortlisting over the DocumentFingerprint table.
///
/// <para><b>Why this reads every row.</b> Phase 1 stores each document's
/// fingerprints as one packed varbinary blob rather than a row per fingerprint.
/// That keeps the corpus to one row per document instead of thousands, but it
/// means SQL cannot intersect fingerprint sets itself - the intersection happens
/// here, in memory.</para>
///
/// <para>The cost is one sequential scan of the blob column per analysis, which at
/// this project's scale is far cheaper than maintaining a table of millions of
/// fingerprint rows. Only the id and the blob are read, never the document text.
/// If the corpus ever outgrows this, an inverted-index table can be added
/// alongside the blob and swapped in behind <see cref="IFingerprintIndex"/>
/// without the Engine noticing.</para>
///
/// <para>Rows generated with different winnowing parameters are filtered out in
/// SQL: their hashes cannot match, so reading them would be wasted IO.</para>
/// </summary>
public class EfFingerprintIndex : IFingerprintIndex
{
    private readonly AppDbContext _context;

    public EfFingerprintIndex(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<FingerprintCandidate>> FindCandidatesAsync(
        FingerprintQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Fingerprints.Count == 0)
        {
            return [];
        }

        var excluded = query.ExcludeDocumentIds as ISet<int> ?? query.ExcludeDocumentIds.ToHashSet();

        var stored = _context.DocumentFingerprints
            .AsNoTracking()
            .Where(fingerprint => fingerprint.NGramSize == query.NGramSize
                && fingerprint.WindowSize == query.WindowSize
                && !excluded.Contains(fingerprint.UploadedFileId))
            .Select(fingerprint => new
            {
                fingerprint.UploadedFileId,
                fingerprint.Fingerprints,
            })
            .AsAsyncEnumerable();

        var candidates = new List<FingerprintCandidate>();

        await foreach (var row in stored.WithCancellation(cancellationToken))
        {
            var shared = FingerprintSerializer.CountShared(row.Fingerprints, query.Fingerprints);

            if (shared >= query.MinimumSharedFingerprints)
            {
                candidates.Add(new FingerprintCandidate(row.UploadedFileId, shared));
            }
        }

        // Most-shared first, so the cap keeps the most promising candidates.
        candidates.Sort((left, right) => right.SharedFingerprintCount.CompareTo(left.SharedFingerprintCount));

        return query.MaxCandidates > 0 && candidates.Count > query.MaxCandidates
            ? candidates[..query.MaxCandidates]
            : candidates;
    }
}
