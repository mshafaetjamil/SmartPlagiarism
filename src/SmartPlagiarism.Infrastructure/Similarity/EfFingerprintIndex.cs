using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Similarity;

/// <summary>
/// Fingerprint shortlisting over the inverted index (<c>FingerprintHashes</c>).
///
/// The earlier version read every document's packed fingerprint blob and
/// intersected the sets in memory, so every analysis read the whole corpus. Now
/// the database looks up the query's hashes - a seek each, on the index keyed by
/// hash - and counts them per document, returning only the documents worth
/// comparing. Rows made with other winnowing parameters are filtered out in the
/// same query: their hashes cannot match.
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

        // Stored signed: SQL Server has no unsigned 64-bit type, and the cast
        // keeps every bit.
        var hashes = query.Fingerprints.Select(hash => unchecked((long)hash)).ToList();
        var excluded = query.ExcludeDocumentIds.ToList();
        var limit = query.MaxCandidates > 0 ? query.MaxCandidates : int.MaxValue;

        // One seek per hash in the inverted index, counted per document - the
        // database does the shortlisting, however large the corpus. Most-shared
        // first, so the cap keeps the most promising candidates.
        var shortlist = await _context.FingerprintHashes
            .AsNoTracking()
            .Where(row => hashes.Contains(row.Hash)
                && row.DocumentFingerprint.NGramSize == query.NGramSize
                && row.DocumentFingerprint.WindowSize == query.WindowSize
                && !excluded.Contains(row.DocumentFingerprint.UploadedFileId))
            .GroupBy(row => row.DocumentFingerprint.UploadedFileId)
            .Select(group => new { UploadedFileId = group.Key, Shared = group.Count() })
            .Where(candidate => candidate.Shared >= query.MinimumSharedFingerprints)
            .OrderByDescending(candidate => candidate.Shared)
            .ThenBy(candidate => candidate.UploadedFileId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. shortlist.Select(candidate => new FingerprintCandidate(candidate.UploadedFileId, candidate.Shared))];
    }
}
