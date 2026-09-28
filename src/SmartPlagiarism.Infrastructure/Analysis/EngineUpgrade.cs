using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Engine;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>
/// Brings stored work up to date after the engine changes how it reads documents
/// or scores them (<see cref="EngineInfo.Version"/>). Run by the analysis worker
/// at start-up, before anything else is analysed.
///
/// Archive documents are read again and re-indexed first, since nothing else
/// would ever re-read them; then every submission whose result an older engine
/// produced goes back on the queue, and is re-read and re-scored in submission
/// order. Results therefore never mix the old and new rules for long, and the
/// engine version on each result says which rules produced it.
/// </summary>
public class EngineUpgrade
{
    private readonly AppDbContext _context;
    private readonly DocumentIndexer _indexer;
    private readonly ILogger<EngineUpgrade> _logger;

    public EngineUpgrade(AppDbContext context, DocumentIndexer indexer, ILogger<EngineUpgrade> logger)
    {
        _context = context;
        _indexer = indexer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var staleArchiveFiles = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.ArchiveDocumentId != null
                && (file.ExtractedText == null || file.ExtractedText.EngineVersion != EngineInfo.Version))
            .OrderBy(file => file.Id)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);

        foreach (var fileId in staleArchiveFiles)
        {
            await _indexer.IndexAsync([fileId], cancellationToken);

            // A large archive touches thousands of rows; tracking them all would
            // slow every later save down.
            _context.ChangeTracker.Clear();
        }

        var outdated = await _context.Submissions
            .Where(submission => submission.AnalysisStatus == AnalysisStatus.Completed
                && submission.PlagiarismResult != null
                && submission.PlagiarismResult.EngineVersion != EngineInfo.Version)
            .ToListAsync(cancellationToken);

        foreach (var submission in outdated)
        {
            submission.RequeueForAnalysis();
        }

        await _context.SaveChangesAsync(cancellationToken);

        if (staleArchiveFiles.Count > 0 || outdated.Count > 0)
        {
            _logger.LogInformation(
                "Engine {Version}: re-read {ArchiveCount} archive document(s) and queued {SubmissionCount} submission(s) to be analysed again.",
                EngineInfo.Version,
                staleArchiveFiles.Count,
                outdated.Count);
        }
    }
}
