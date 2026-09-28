using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Core.DTOs.Review;

public enum ReviewSortField
{
    SubmittedAt,
    Similarity,
    Title,
    Student,
    Course,
}

/// <summary>Filters, sort and page for the teacher's submission list. Every filter is optional.</summary>
public sealed record ReviewSubmissionQuery(
    int? CourseId = null,
    int? SemesterId = null,
    SubmissionStatus? ReviewStatus = null,
    AnalysisStatus? AnalysisStatus = null,
    decimal? MinSimilarityPercent = null,
    decimal? MaxSimilarityPercent = null,
    ReviewSortField Sort = ReviewSortField.SubmittedAt,
    bool Descending = true,
    int Page = 1)
{
    public const int PageSize = 20;
}

public sealed record ReviewSubmissionListItemDto(
    int Id,
    string Title,
    string StudentName,
    string StudentNumber,
    string CourseCode,
    string SemesterName,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    SubmissionStatus Status,
    decimal? OverallSimilarityPercent,
    int MatchCount);

/// <summary>Everything the teacher's submission page shows.</summary>
public sealed record ReviewSubmissionDetailDto(
    int Id,
    string Title,
    SubmissionType Type,
    string CourseCode,
    string CourseTitle,
    string SemesterName,
    string StudentName,
    string StudentNumber,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    string? AnalysisError,
    SubmissionStatus Status,
    string? TeacherComment,
    IReadOnlyList<ReviewFileDto> Files,
    ReviewResultDto? Result,
    IReadOnlyList<ReviewReverseMatchDto> MatchedBy);

/// <summary>
/// A later submission whose analysis found text from this one. A submission is
/// compared only with work that existed before it - so re-running it gives the
/// same result, and an original is never scored as the copy of its copier - so
/// the first of two students sharing work never has the second in its own
/// results; this is where the teacher sees it.
/// </summary>
/// <param name="MatchId">The later submission's match, for the viewer.</param>
/// <param name="SimilarityPercent">
/// Share of the <em>later</em> submission found in this one.
/// </param>
/// <param name="CanOpen">The later submission is in the teacher's department, so it can be opened.</param>
public sealed record ReviewReverseMatchDto(
    int MatchId,
    int SubmissionId,
    string SubmissionTitle,
    string StudentName,
    DateTime SubmittedAtUtc,
    decimal SimilarityPercent,
    bool IsSameStudent,
    bool CanOpen);

/// <param name="ExtractionMethod">Null until the file has been extracted.</param>
/// <param name="Warnings">What extraction could not do - the reason a score may be low.</param>
public sealed record ReviewFileDto(
    int Id,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    ExtractionMethod? ExtractionMethod,
    int? WordCount,
    IReadOnlyList<string> Warnings);

/// <param name="CommonTextPercent">Share set aside as text most of the course shares.</param>
public sealed record ReviewResultDto(
    decimal OverallSimilarityPercent,
    decimal CommonTextPercent,
    DateTime AnalyzedAtUtc,
    string EngineVersion,
    IReadOnlyList<ReviewMatchDto> Matches);

/// <param name="MatchId">The MatchedDocument, which is what the viewer opens.</param>
/// <param name="OwnFileName">Which of this submission's files contains the passages.</param>
/// <param name="MatchedSubmissionId">The matched submission; null when the source is an archive document.</param>
/// <param name="SourceTitle">The matched submission's or archive document's title.</param>
/// <param name="SourceOwner">The student, or the archive author and year.</param>
/// <param name="ArchiveYear">Set when the source is from the archive.</param>
public sealed record ReviewMatchDto(
    int MatchId,
    string OwnFileName,
    string MatchedFileName,
    int? MatchedSubmissionId,
    string SourceTitle,
    string SourceOwner,
    int? ArchiveYear,
    decimal SimilarityPercent,
    int PassageCount,
    bool IsOwnEarlierWork,
    bool CanOpenSource);

/// <summary>Both sides of the match viewer, already cut into render-safe segments.</summary>
/// <param name="IdenticalFiles">
/// The two files are byte-identical, found by hash; there are no passages to
/// highlight because all of it matches.
/// </param>
/// <param name="SourceChangedSinceAnalysis">
/// Either file's text was re-extracted after this analysis ran, so the stored
/// offsets may no longer line up. Re-running the analysis fixes it.
/// </param>
/// <param name="SimilarityPercent">Share of the submission in passages found in the source.</param>
/// <param name="SupportingScores">
/// Each similarity measure's ratio, keyed by measure name - evidence beside the
/// headline, notably for reworded copying. Empty for exact duplicates and older results.
/// </param>
public sealed record ComparisonDto(
    int MatchId,
    int SubmissionId,
    string SubmissionTitle,
    decimal SimilarityPercent,
    IReadOnlyDictionary<string, double> SupportingScores,
    int PassageCount,
    bool IdenticalFiles,
    bool SourceChangedSinceAnalysis,
    ComparisonSideDto Submitted,
    ComparisonSideDto Source);

/// <param name="Title">The submission's or archive document's title.</param>
/// <param name="Owner">The student, or the archive author and year.</param>
public sealed record ComparisonSideDto(
    string FileName,
    string Title,
    string Owner,
    IReadOnlyList<HighlightSegment> Segments);

public enum ReviewDecisionOutcome
{
    Recorded,
    NotFound,

    /// <summary>Only a submission pending review can be approved or rejected.</summary>
    AlreadyReviewed,

    /// <summary>A rejection must say why.</summary>
    CommentRequired,

    CommentTooLong,
}
