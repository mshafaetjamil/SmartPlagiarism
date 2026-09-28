using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Engine;
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

    internal const string RepeatedlyInterruptedReason =
        "The analysis stopped part-way every time it was tried, so it was given up. The file may be damaged "
        + "or far larger inside than it looks; re-saving it (for example, exporting it again as PDF) usually helps.";

    private readonly AppDbContext _context;
    private readonly DocumentIndexer _indexer;
    private readonly SimilarityPipeline _pipeline;
    private readonly CommonTextFinder _commonText;
    private readonly EngineUpgrade _upgrade;
    private readonly ILogger<SubmissionAnalyzer> _logger;

    public SubmissionAnalyzer(
        AppDbContext context,
        DocumentIndexer indexer,
        SimilarityPipeline pipeline,
        CommonTextFinder commonText,
        EngineUpgrade upgrade,
        ILogger<SubmissionAnalyzer> logger)
    {
        _context = context;
        _indexer = indexer;
        _pipeline = pipeline;
        _commonText = commonText;
        _upgrade = upgrade;
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
        // Results an older engine produced go back on the queue with the rest.
        await _upgrade.RunAsync(cancellationToken);
        _context.ChangeTracker.Clear();

        var interrupted = await _context.Submissions
            .Where(submission => submission.AnalysisStatus == AnalysisStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var submission in interrupted)
        {
            if (submission.CanResumeAnalysis)
            {
                submission.ResumeInterruptedAnalysis();
                continue;
            }

            // Interrupted at every attempt: most likely the file itself brings
            // the server down, and queuing it again would do so at every start
            // while everything behind it waits.
            await RemoveResultAsync(submission.Id, cancellationToken);
            submission.MarkFailed(RepeatedlyInterruptedReason);
            _logger.LogError(
                "Submission {SubmissionId} was interrupted {Attempts} times mid-analysis and has been failed.",
                submission.Id,
                submission.AnalysisAttempts);
        }

        if (interrupted.Count > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning(
                "Found {Count} submission(s) interrupted mid-analysis.",
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
        var fileIds = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId == submissionId)
            .OrderBy(file => file.Id)
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);

        if (fileIds.Count == 0)
        {
            return "The submission has no files to analyse.";
        }

        // Fingerprints are stored before comparison so later submissions can be
        // shortlisted against this one; its own files, and anything submitted
        // after it, are excluded from its own comparison below.
        var indexed = await _indexer.IndexAsync(fileIds, cancellationToken);
        var documents = indexed.Where(file => file.Document is not null).Select(file => file.Document!).ToList();

        if (documents.Count == 0)
        {
            return DescribeUnreadable(indexed);
        }

        var owner = await _context.Submissions
            .Where(submission => submission.Id == submissionId)
            .Select(submission => new { submission.StudentProfileId, submission.CourseId, submission.SubmittedAtUtc })
            .FirstAsync(cancellationToken);
        var studentProfileId = owner.StudentProfileId;

        // A submission is compared only with work that existed before it - the
        // archive and earlier submissions. Otherwise re-running an original after
        // someone copied it would score the original as the copy. Later work that
        // matches it is shown on its page as matched-by instead.
        var laterWork = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId != submissionId
                && (file.Submission!.SubmittedAtUtc > owner.SubmittedAtUtc
                    || (file.Submission.SubmittedAtUtc == owner.SubmittedAtUtc && file.SubmissionId > submissionId)))
            .Select(file => file.Id)
            .ToListAsync(cancellationToken);

        var excluded = fileIds.Concat(laterWork).ToList();

        // The student's earlier submissions are listed but not counted: a
        // resubmission is not copying, though reuse across courses is worth seeing.
        var ownEarlierWork = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId != submissionId && file.Submission!.StudentProfileId == studentProfileId)
            .Select(file => file.Id)
            .ToHashSetAsync(cancellationToken);

        var commonText = await _commonText.FindAsync(owner.CourseId, studentProfileId, documents, cancellationToken);

        var reports = new List<(AnalyzedDocument Document, SimilarityReport Report)>(documents.Count);

        foreach (var document in documents)
        {
            var report = await _pipeline.AnalyzeAsync(document, excluded, ownEarlierWork, commonText, cancellationToken);
            reports.Add((document, report));
        }

        await ReplaceResultAsync(submissionId, reports, cancellationToken);

        return null;
    }

    /// <summary>
    /// A submission with no readable text gets a failure, never a 0% score: a
    /// scanned PDF with OCR unavailable would otherwise look perfectly original.
    /// </summary>
    private static string DescribeUnreadable(IReadOnlyList<IndexedFile> files)
    {
        var firstWarning = files.SelectMany(file => file.Warnings).FirstOrDefault();
        var names = string.Join(", ", files.Select(file => file.FileName));

        return firstWarning is null
            ? $"No readable text was found in {names}."
            : $"No readable text was found in {names}. {firstWarning}";
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

        result.OverallSimilarityPercent = WeightedByLength(reports, report => report.OverallSimilarityPercent);
        result.CommonTextPercent = WeightedByLength(reports, report => report.CommonTextPercent);
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
                    ComponentScoresJson = ComponentScoreSerializer.Serialize(match.ComponentScores),
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
    private static decimal WeightedByLength(
        IReadOnlyList<(AnalyzedDocument Document, SimilarityReport Report)> reports,
        Func<SimilarityReport, decimal> percent)
    {
        var totalWords = reports.Sum(entry => entry.Document.WordCount);

        if (totalWords == 0)
        {
            return 0m;
        }

        var weighted = reports.Sum(entry => percent(entry.Report) * entry.Document.WordCount);

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
