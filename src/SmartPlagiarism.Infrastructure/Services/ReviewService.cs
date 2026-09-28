using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Review;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;
using SmartPlagiarism.Engine.Matching;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <inheritdoc cref="IReviewService"/>
public class ReviewService : IReviewService
{
    private readonly AppDbContext _context;
    private readonly IAuditService _audit;

    public ReviewService(AppDbContext context, IAuditService audit)
    {
        _context = context;
        _audit = audit;
    }

    public async Task<PagedResult<ReviewSubmissionListItemDto>> GetSubmissionsAsync(
        ReviewSubmissionQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var filtered = Filter(_context.Submissions.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(cancellationToken);
        var page = PagedResult<ReviewSubmissionListItemDto>.ClampPage(query.Page, totalCount, ReviewSubmissionQuery.PageSize);

        var items = await Sort(filtered, query.Sort, query.Descending)
            .Skip((page - 1) * ReviewSubmissionQuery.PageSize)
            .Take(ReviewSubmissionQuery.PageSize)
            .Select(submission => new ReviewSubmissionListItemDto(
                submission.Id,
                submission.Title,
                submission.StudentProfile.User.FullName,
                submission.StudentProfile.StudentNumber,
                submission.Course.Code,
                submission.Semester.Name,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.Status,
                submission.PlagiarismResult != null ? submission.PlagiarismResult.OverallSimilarityPercent : null,
                submission.PlagiarismResult != null ? submission.PlagiarismResult.Matches.Count : 0))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReviewSubmissionListItemDto>(items, page, ReviewSubmissionQuery.PageSize, totalCount);
    }

    public async Task<ReviewSubmissionDetailDto?> GetSubmissionAsync(
        int submissionId,
        CancellationToken cancellationToken = default)
    {
        var submission = await _context.Submissions
            .AsNoTracking()
            .Where(candidate => candidate.Id == submissionId)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.Title,
                candidate.Type,
                CourseCode = candidate.Course.Code,
                CourseTitle = candidate.Course.Title,
                SemesterName = candidate.Semester.Name,
                StudentName = candidate.StudentProfile.User.FullName,
                candidate.StudentProfile.StudentNumber,
                candidate.SubmittedAtUtc,
                candidate.AnalysisStatus,
                candidate.AnalysisError,
                candidate.Status,
                candidate.TeacherComment,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (submission is null)
        {
            return null;
        }

        var files = await _context.UploadedFiles
            .AsNoTracking()
            .Where(file => file.SubmissionId == submissionId)
            .OrderBy(file => file.OriginalFileName)
            .Select(file => new
            {
                file.Id,
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                Extraction = file.ExtractedText,
            })
            .ToListAsync(cancellationToken);

        return new ReviewSubmissionDetailDto(
            submission.Id,
            submission.Title,
            submission.Type,
            submission.CourseCode,
            submission.CourseTitle,
            submission.SemesterName,
            submission.StudentName,
            submission.StudentNumber,
            submission.SubmittedAtUtc,
            submission.AnalysisStatus,
            submission.AnalysisError,
            submission.Status,
            submission.TeacherComment,
            files.Select(file => new ReviewFileDto(
                file.Id,
                file.OriginalFileName,
                file.ContentType,
                file.SizeBytes,
                file.Extraction?.ExtractionMethod,
                file.Extraction?.WordCount,
                file.Extraction?.Warnings ?? []))
                .ToList(),
            await GetResultAsync(submissionId, cancellationToken));
    }

    public async Task<ComparisonDto?> GetComparisonAsync(int matchId, CancellationToken cancellationToken = default)
    {
        var match = await _context.MatchedDocuments
            .AsNoTracking()
            .Where(candidate => candidate.Id == matchId && candidate.MatchedUploadedFileId != null)
            .Select(candidate => new
            {
                candidate.Id,
                candidate.SimilarityPercent,
                candidate.MatchedFragmentsJson,
                candidate.PlagiarismResult.SubmissionId,
                candidate.PlagiarismResult.AnalyzedAtUtc,
                SubmissionTitle = candidate.PlagiarismResult.Submission.Title,
                SubmittedStudent = candidate.PlagiarismResult.Submission.StudentProfile.User.FullName,
                SubmittedFileName = candidate.UploadedFile.OriginalFileName,
                SubmittedHash = candidate.UploadedFile.Sha256Hash,
                SubmittedText = candidate.UploadedFile.ExtractedText!.FullText,
                SubmittedExtractedAt = (DateTime?)candidate.UploadedFile.ExtractedText!.ExtractedAtUtc,
                SourceFileName = candidate.MatchedUploadedFile!.OriginalFileName,
                SourceHash = candidate.MatchedUploadedFile.Sha256Hash,
                Source = new MatchSource(
                    candidate.MatchedUploadedFile.SubmissionId,
                    candidate.MatchedUploadedFile.Submission!.Title,
                    (int?)candidate.MatchedUploadedFile.Submission!.StudentProfileId,
                    candidate.MatchedUploadedFile.Submission!.StudentProfile.User.FullName,
                    candidate.MatchedUploadedFile.Submission!.StudentProfile.StudentNumber,
                    candidate.MatchedUploadedFile.Submission!.Course.Code,
                    (int?)candidate.MatchedUploadedFile.ArchiveDocument!.Year,
                    candidate.MatchedUploadedFile.ArchiveDocument!.Title,
                    candidate.MatchedUploadedFile.ArchiveDocument!.Author),
                SourceText = candidate.MatchedUploadedFile.ExtractedText!.FullText,
                SourceExtractedAt = (DateTime?)candidate.MatchedUploadedFile.ExtractedText!.ExtractedAtUtc,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return null;
        }

        var fragments = MatchedFragmentSerializer.Deserialize(match.MatchedFragmentsJson);

        // A text re-extracted after the analysis may have shifted, and the stored
        // offsets with it; the teacher is told rather than shown wrong highlights.
        var changedSince = match.SubmittedExtractedAt > match.AnalyzedAtUtc
            || match.SourceExtractedAt > match.AnalyzedAtUtc;

        return new ComparisonDto(
            match.Id,
            match.SubmissionId,
            match.SubmissionTitle,
            match.SimilarityPercent,
            fragments.Count,
            string.Equals(match.SubmittedHash, match.SourceHash, StringComparison.OrdinalIgnoreCase),
            changedSince,
            new ComparisonSideDto(
                match.SubmittedFileName,
                match.SubmissionTitle,
                match.SubmittedStudent,
                HighlightSegmenter.Segment(
                    match.SubmittedText,
                    fragments.Select((fragment, index) => new HighlightSpan(fragment.TargetStart, fragment.TargetEnd, index)))),
            new ComparisonSideDto(
                match.SourceFileName,
                match.Source.Title,
                match.Source.Owner,
                HighlightSegmenter.Segment(
                    match.SourceText,
                    fragments.Select((fragment, index) => new HighlightSpan(fragment.SourceStart, fragment.SourceEnd, index)))));
    }

    public Task<ReviewDecisionOutcome> ApproveAsync(
        int submissionId,
        string? comment,
        string teacherUserId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(submissionId, comment, teacherUserId, approve: true, cancellationToken);

    public Task<ReviewDecisionOutcome> RejectAsync(
        int submissionId,
        string? comment,
        string teacherUserId,
        CancellationToken cancellationToken = default) =>
        DecideAsync(submissionId, comment, teacherUserId, approve: false, cancellationToken);

    private async Task<ReviewDecisionOutcome> DecideAsync(
        int submissionId,
        string? comment,
        string teacherUserId,
        bool approve,
        CancellationToken cancellationToken)
    {
        var trimmed = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

        if (!approve && trimmed is null)
        {
            return ReviewDecisionOutcome.CommentRequired;
        }

        if (trimmed?.Length > Submission.MaxTeacherCommentLength)
        {
            return ReviewDecisionOutcome.CommentTooLong;
        }

        var submission = await _context.Submissions
            .FirstOrDefaultAsync(candidate => candidate.Id == submissionId, cancellationToken);

        if (submission is null)
        {
            return ReviewDecisionOutcome.NotFound;
        }

        if (submission.Status != SubmissionStatus.PendingReview)
        {
            return ReviewDecisionOutcome.AlreadyReviewed;
        }

        if (approve)
        {
            submission.Approve(trimmed);
        }
        else
        {
            submission.Reject(trimmed!);
        }

        await _context.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntry(
                approve ? AuditActions.SubmissionApproved : AuditActions.SubmissionRejected,
                AuditEntityTypes.Submission,
                submissionId.ToString(CultureInfo.InvariantCulture),
                teacherUserId,
                trimmed),
            cancellationToken);

        return ReviewDecisionOutcome.Recorded;
    }

    private async Task<ReviewResultDto?> GetResultAsync(int submissionId, CancellationToken cancellationToken)
    {
        var result = await _context.PlagiarismResults
            .AsNoTracking()
            .Where(candidate => candidate.SubmissionId == submissionId)
            .Select(candidate => new { candidate.Id, candidate.OverallSimilarityPercent, candidate.AnalyzedAtUtc, candidate.EngineVersion })
            .FirstOrDefaultAsync(cancellationToken);

        if (result is null)
        {
            return null;
        }

        var matches = await _context.MatchedDocuments
            .AsNoTracking()
            .Where(match => match.PlagiarismResultId == result.Id && match.MatchedUploadedFileId != null)
            .Select(match => new
            {
                match.Id,
                OwnFileName = match.UploadedFile.OriginalFileName,
                MatchedFileName = match.MatchedUploadedFile!.OriginalFileName,
                Source = new MatchSource(
                    match.MatchedUploadedFile.SubmissionId,
                    match.MatchedUploadedFile.Submission!.Title,
                    (int?)match.MatchedUploadedFile.Submission!.StudentProfileId,
                    match.MatchedUploadedFile.Submission!.StudentProfile.User.FullName,
                    match.MatchedUploadedFile.Submission!.StudentProfile.StudentNumber,
                    match.MatchedUploadedFile.Submission!.Course.Code,
                    (int?)match.MatchedUploadedFile.ArchiveDocument!.Year,
                    match.MatchedUploadedFile.ArchiveDocument!.Title,
                    match.MatchedUploadedFile.ArchiveDocument!.Author),
                match.SimilarityPercent,
                match.MatchedFragmentsJson,
            })
            .ToListAsync(cancellationToken);

        return new ReviewResultDto(
            result.OverallSimilarityPercent,
            result.AnalyzedAtUtc,
            result.EngineVersion,
            matches
                .OrderByDescending(match => match.SimilarityPercent)
                .Select(match => new ReviewMatchDto(
                    match.Id,
                    match.OwnFileName,
                    match.MatchedFileName,
                    match.Source.SubmissionId,
                    match.Source.Title,
                    match.Source.Owner,
                    match.Source.ArchiveYear,
                    match.SimilarityPercent,
                    MatchedFragmentSerializer.Deserialize(match.MatchedFragmentsJson).Count))
                .ToList());
    }

    private static IQueryable<Submission> Filter(IQueryable<Submission> submissions, ReviewSubmissionQuery query)
    {
        if (query.CourseId is { } courseId)
        {
            submissions = submissions.Where(submission => submission.CourseId == courseId);
        }

        if (query.SemesterId is { } semesterId)
        {
            submissions = submissions.Where(submission => submission.SemesterId == semesterId);
        }

        if (query.ReviewStatus is { } reviewStatus)
        {
            submissions = submissions.Where(submission => submission.Status == reviewStatus);
        }

        if (query.AnalysisStatus is { } analysisStatus)
        {
            submissions = submissions.Where(submission => submission.AnalysisStatus == analysisStatus);
        }

        var (minimum, maximum) = NormalizeRange(query.MinSimilarityPercent, query.MaxSimilarityPercent);

        // A similarity range only makes sense for analysed work, so it excludes
        // anything without a result.
        if (minimum is { } min)
        {
            submissions = submissions.Where(submission =>
                submission.PlagiarismResult != null && submission.PlagiarismResult.OverallSimilarityPercent >= min);
        }

        if (maximum is { } max)
        {
            submissions = submissions.Where(submission =>
                submission.PlagiarismResult != null && submission.PlagiarismResult.OverallSimilarityPercent <= max);
        }

        return submissions;
    }

    /// <summary>Clamps to 0-100 and swaps a range entered backwards.</summary>
    private static (decimal? Minimum, decimal? Maximum) NormalizeRange(decimal? minimum, decimal? maximum)
    {
        minimum = minimum is { } min ? Math.Clamp(min, 0m, 100m) : null;
        maximum = maximum is { } max ? Math.Clamp(max, 0m, 100m) : null;

        return minimum > maximum ? (maximum, minimum) : (minimum, maximum);
    }

    private static IQueryable<Submission> Sort(IQueryable<Submission> submissions, ReviewSortField field, bool descending)
    {
        var ordered = field switch
        {
            // Cast to double for ordering: lossless for a decimal(5,2) score, and
            // unlike decimal it can be ordered by every provider, SQLite included.
            ReviewSortField.Similarity => OrderBy(
                submissions,
                submission => (double?)submission.PlagiarismResult!.OverallSimilarityPercent,
                descending),
            ReviewSortField.Title => OrderBy(submissions, submission => submission.Title, descending),
            ReviewSortField.Student => OrderBy(submissions, submission => submission.StudentProfile.User.FullName, descending),
            ReviewSortField.Course => OrderBy(submissions, submission => submission.Course.Code, descending),
            _ => OrderBy(submissions, submission => submission.SubmittedAtUtc, descending),
        };

        // A unique tie-breaker, so paging never shows a row twice or skips one.
        return ordered.ThenByDescending(submission => submission.Id);
    }

    private static IOrderedQueryable<Submission> OrderBy<TKey>(
        IQueryable<Submission> submissions,
        Expression<Func<Submission, TKey>> key,
        bool descending) =>
        descending ? submissions.OrderByDescending(key) : submissions.OrderBy(key);
}
