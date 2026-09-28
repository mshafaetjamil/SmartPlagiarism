using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>One file after indexing.</summary>
/// <param name="Document">The file's text, ready to compare; null when no readable text was found.</param>
/// <param name="Warnings">What extraction could not do for the file.</param>
public sealed record IndexedFile(int FileId, string FileName, AnalyzedDocument? Document, IReadOnlyList<string> Warnings);

/// <summary>
/// Makes files findable by later comparisons: extracts each file's text and
/// stores its fingerprints, replacing any from an earlier run.
///
/// Shared by submission analysis, which then compares the files, and archive
/// import, which only needs them indexed - so an archived 2012 thesis is
/// matched exactly as a submission from last week would be.
/// </summary>
public class DocumentIndexer
{
    private readonly AppDbContext _context;
    private readonly IExtractionService _extraction;
    private readonly SimilarityPipeline _pipeline;

    public DocumentIndexer(AppDbContext context, IExtractionService extraction, SimilarityPipeline pipeline)
    {
        _context = context;
        _extraction = extraction;
        _pipeline = pipeline;
    }

    public async Task<IReadOnlyList<IndexedFile>> IndexAsync(IReadOnlyList<int> fileIds, CancellationToken cancellationToken = default)
    {
        foreach (var fileId in fileIds)
        {
            // Failures are recorded on the file's own extraction row; the loop goes
            // on so one unreadable file does not sink the others.
            await _extraction.ExtractAndPersistAsync(fileId, cancellationToken);
        }

        var files = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => fileIds.Contains(file.Id))
            .OrderBy(file => file.Id)
            .Select(file => new
            {
                file.Id,
                file.OriginalFileName,
                file.Sha256Hash,
                Text = file.ExtractedText == null ? null : file.ExtractedText.FullText,
                Warnings = file.ExtractedText == null ? null : file.ExtractedText.Warnings,
            })
            .ToListAsync(cancellationToken);

        var indexed = files
            .Select(file =>
            {
                var document = new AnalyzedDocument(file.Id, file.Text ?? string.Empty, file.Sha256Hash);
                return new IndexedFile(file.Id, file.OriginalFileName, document.IsEmpty ? null : document, file.Warnings ?? []);
            })
            .ToList();

        await StoreFingerprintsAsync(
            indexed.Where(file => file.Document is not null).Select(file => file.Document!).ToList(),
            cancellationToken);

        return indexed;
    }

    private async Task StoreFingerprintsAsync(IReadOnlyList<AnalyzedDocument> documents, CancellationToken cancellationToken)
    {
        if (documents.Count == 0)
        {
            return;
        }

        var fileIds = documents.Select(document => document.Id).ToList();

        var existing = await _context.DocumentFingerprints
            .Where(fingerprint => fileIds.Contains(fingerprint.UploadedFileId))
            .ToDictionaryAsync(fingerprint => fingerprint.UploadedFileId, cancellationToken);

        foreach (var document in documents)
        {
            var set = _pipeline.Fingerprint(document);

            if (!existing.TryGetValue(document.Id, out var row))
            {
                row = new DocumentFingerprint { UploadedFileId = document.Id };
                _context.DocumentFingerprints.Add(row);
            }

            row.Fingerprints = FingerprintSerializer.Pack(set.DistinctHashes);
            row.FingerprintCount = set.DistinctHashes.Count;
            row.NGramSize = set.NGramSize;
            row.WindowSize = set.WindowSize;
            row.GeneratedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
