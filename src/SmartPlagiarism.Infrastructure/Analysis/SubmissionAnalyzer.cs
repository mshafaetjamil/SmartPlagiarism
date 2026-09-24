using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Engine;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Matching;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Analysis;

/// <inheritdoc cref="ISubmissionAnalyzer"/>
public class SubmissionAnalyzer : ISubmissionAnalyzer
{
    /// <summary>
    /// Shown instead of an exception message, which could expose paths or SQL to
    /// a student. The exception itself goes to the log.
    /// </summary>
    internal const string UnexpectedFailureReason =
        "An unexpected error stopped the analysis. It has been logged; a teacher can re-run it.";

    private readonly AppDbContext _context;
    private readonly IExtractionService _extraction;
    private readonly SimilarityPipeline _pipeline;
    private readonly ILogger<SubmissionAnalyzer> _logger;

    public SubmissionAnalyzer(
        AppDbContext context,
        IExtractionService extraction,
        SimilarityPipeline pipeline,
        ILogger<SubmissionAnalyzer> logger)
    {
        _context = context;
        _extraction = extraction;
        _pipeline = pipeline;
        _logger = logger;
    }

    public async Task AnalyzeAsync(int submissionId, CancellationToken cancellationToken = default)
    {
        var submission = await _context.Submissions
            .FirstOrDefaultAsync(candidate => candidate.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            _logger.LogWarning("Submission {SubmissionId} was queued for analysis but no longer exists.", submissionId);
            return;
        }

        if (submission.AnalysisStatus != AnalysisStatus.Queued)
        {
            // A duplicate queue entry, or one already handled: nothing to do.
            _logger.LogDebug(
                "Skipping submission {SubmissionId}: it is {Status}, not Queued.",
                submissionId,
                submission.AnalysisStatus);
            return;
        }

        submission.MarkProcessing();
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Analysing submission {SubmissionId}.", submissionId);

        string? failure;

        try
        {
            failure = await RunAsync(submissionId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down. The submission stays Processing and is resumed at the
            // next start by ResumePendingAsync.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis of submission {SubmissionId} failed.", submissionId);
            failure = UnexpectedFailureReason;
        }

        await FinishAsync(submissionId, failure, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> ResumePendingAsync(CancellationToken cancellationToken = default)
    {
        var interrupted = await _context.Submissions
            .Where(submission => submission.AnalysisStatus == AnalysisStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var submission in interrupted)
        {
            submission.ResumeInterruptedAnalysis();
        }

        if (interrupted.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Returned {Count} submission(s) interrupted mid-analysis to the queue.",
                interrupted.Count);
        }

        return await _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.AnalysisStatus == AnalysisStatus.Queued)
            .OrderBy(submission => submission.SubmittedAtUtc)
            .Select(submission => submission.Id)
            .ToListAsync(cancellationToken);
    }

    /// <returns>Null when a result was stored; otherwise why none could be.</returns>
    private async Task<string?> RunAsync(int submissionId, CancellationToken cancellationToken)
    {
        var files = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId == submissionId)
            .OrderBy(file => file.Id)
            .Select(file => new { file.Id, file.OriginalFileName, file.Sha256Hash })
            .ToListAsync(cancellationToken);

        if (files.Count == 0)
        {
            return "The submission has no files to analyse.";
        }

        foreach (var file in files)
        {
            // Failures are recorded on the file's own extraction row; the loop goes
            // on so one unreadable file does not sink the others.
            await _extraction.ExtractAndPersistAsync(file.Id, cancellationToken);
        }

        var fileIds = files.Select(file => file.Id).ToList();

        var extracted = await _context.ExtractedTexts
            .AsNoTracking()
            .Where(text => fileIds.Contains(text.UploadedFileId))
            .Select(text => new { text.UploadedFileId, text.FullText, text.Warnings })
            .ToListAsync(cancellationToken);

        var documents = files
            .Join(
                extracted,
                file => file.Id,
                text => text.UploadedFileId,
                (file, text) => new AnalyzedDocument(file.Id, text.FullText, file.Sha256Hash))
            .Where(document => !document.IsEmpty)
            .ToList();

        if (documents.Count == 0)
        {
            return DescribeUnreadable(files.Select(file => file.OriginalFileName).ToList(), extracted.Select(text => text.Warnings));
        }

        await StoreFingerprintsAsync(documents, cancellationToken);

        var reports = new List<(AnalyzedDocument Document, SimilarityReport Report)>(documents.Count);

        foreach (var document in documents)
        {
            var report = await _pipeline.AnalyzeAsync(document, fileIds, cancellationToken);
            reports.Add((document, report));
        }

        await ReplaceResultAsync(submissionId, reports, cancellationToken);

        return null;
    }

    /// <summary>
    /// A submission with no readable text gets a failure, never a 0% score: a
    /// scanned PDF with OCR unavailable would otherwise look perfectly original.
    /// </summary>
    private static string DescribeUnreadable(IReadOnlyList<string> fileNames, IEnumerable<List<string>> warnings)
    {
        var firstWarning = warnings.SelectMany(fileWarnings => fileWarnings).FirstOrDefault();
        var files = string.Join(", ", fileNames);

        return firstWarning is null
            ? $"No readable text was found in {files}."
            : $"No readable text was found in {files}. {firstWarning}";
    }

    /// <summary>
    /// Stores each file's fingerprints, replacing any from an earlier run. Stored
    /// before comparison so later submissions can be shortlisted against this one;
    /// the submission's own files are excluded from its own comparison.
    /// </summary>
    private async Task StoreFingerprintsAsync(IReadOnlyList<AnalyzedDocument> documents, CancellationToken cancellationToken)
    {
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

    /// <summary>
    /// Stores the submission's result, overwriting the previous run's row and
    /// replacing its matches. A single SaveChanges, so it is atomic on its own:
    /// the old result is never gone without the new one in place.
    /// </summary>
    private async Task ReplaceResultAsync(
        int submissionId,
        IReadOnlyList<(AnalyzedDocument Document, SimilarityReport Report)> reports,
        CancellationToken cancellationToken)
    {
        var result = await _context.PlagiarismResults
            .Include(existing => existing.Matches)
            .FirstOrDefaultAsync(existing => existing.SubmissionId == submissionId, cancellationToken);

        if (result is null)
        {
            result = new PlagiarismResult { SubmissionId = submissionId };
            _context.PlagiarismResults.Add(result);
        }
        else
        {
            _context.MatchedDocuments.RemoveRange(result.Matches);
            result.Matches.Clear();
        }

        result.OverallSimilarityPercent = WeightedOverall(reports);
        result.AnalyzedAtUtc = DateTime.UtcNow;
        result.EngineVersion = EngineInfo.Version;

        foreach (var (document, report) in reports)
        {
            foreach (var match in report.Matches)
            {
                result.Matches.Add(new MatchedDocument
                {
                    UploadedFileId = document.Id,
                    MatchedUploadedFileId = match.DocumentId,
                    SimilarityPercent = match.SimilarityPercent,
                    MatchedFragmentsJson = MatchedFragmentSerializer.Serialize(match.Fragments),
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The submission's score is each file's score weighted by its length: a
    /// fully copied one-page appendix should not count as much as the 30-page
    /// report beside it.
    /// </summary>
    private static decimal WeightedOverall(IReadOnlyList<(AnalyzedDocument Document, SimilarityReport Report)> reports)
    {
        var totalWords = reports.Sum(entry => entry.Document.WordCount);

        if (totalWords == 0)
        {
            return 0m;
        }

        var weighted = reports.Sum(entry => entry.Report.OverallSimilarityPercent * entry.Document.WordCount);

        return Math.Round(weighted / totalWords, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Stages the previous run's result for deletion; the caller saves.</summary>
    private async Task RemoveResultAsync(int submissionId, CancellationToken cancellationToken)
    {
        var previous = await _context.PlagiarismResults
            .Include(result => result.Matches)
            .FirstOrDefaultAsync(result => result.SubmissionId == submissionId, cancellationToken);

        if (previous is not null)
        {
            _context.PlagiarismResults.Remove(previous);
        }
    }

    /// <summary>
    /// Moves the submission to its final state. Starts from a clean change
    /// tracker, because after a failure the context may still hold the half-made
    /// changes that caused it.
    /// </summary>
    private async Task FinishAsync(int submissionId, string? failure, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var submission = await _context.Submissions
            .FirstAsync(candidate => candidate.Id == submissionId, cancellationToken);

        if (failure is null)
        {
            submission.MarkCompleted();
            _logger.LogInformation("Analysis of submission {SubmissionId} completed.", submissionId);
        }
        else
        {
            // A stale result beside a Failed status would show numbers the failed
            // run did not produce. Removed in the same save as the status change.
            await RemoveResultAsync(submissionId, cancellationToken);
            submission.MarkFailed(failure);
            _logger.LogWarning("Analysis of submission {SubmissionId} failed: {Reason}", submissionId, failure);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
