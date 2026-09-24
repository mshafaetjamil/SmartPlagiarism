using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Similarity;

/// <summary>
/// Gives the Engine's pipeline read access to stored documents.
///
/// Documents are keyed by UploadedFile id throughout, which is what the
/// fingerprint index returns and what MatchedDocument records.
/// </summary>
public class EfCorpusReader : ICorpusReader
{
    private readonly AppDbContext _context;

    public EfCorpusReader(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<int>> FindByContentHashAsync(
        string contentHash,
        IReadOnlyCollection<int> excludeDocumentIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contentHash))
        {
            return [];
        }

        var excluded = excludeDocumentIds.ToHashSet();

        // Backed by the index on Sha256Hash added in Phase 1, so this stays a seek
        // however large the corpus grows.
        return await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.Sha256Hash == contentHash && !excluded.Contains(file.Id))
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<AnalyzedDocument?> LoadAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var stored = await _context.ExtractedTexts
            .AsNoTracking()
            .Where(text => text.UploadedFileId == documentId)
            .Select(text => new
            {
                text.FullText,
                text.UploadedFile.Sha256Hash,
            })
            .FirstOrDefaultAsync(cancellationToken);

        // No extraction yet, or extraction produced nothing: the caller treats this
        // as "nothing to compare" rather than as a failure.
        return stored is null
            ? null
            : new AnalyzedDocument(documentId, stored.FullText, stored.Sha256Hash);
    }
}
