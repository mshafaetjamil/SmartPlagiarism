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
    ReviewResultDto? Result);

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

public sealed record ReviewResultDto(
    decimal OverallSimilarityPercent,
    DateTime AnalyzedAtUtc,
    string EngineVersion,
    IReadOnlyList<ReviewMatchDto> Matches);

/// <param name="MatchId">The MatchedDocument, which is what the viewer opens.</param>
/// <param name="OwnFileName">Which of this submission's files contains the passages.</param>
public sealed record ReviewMatchDto(
    int MatchId,
    string OwnFileName,
    string MatchedFileName,
    int MatchedSubmissionId,
    string MatchedSubmissionTitle,
    string MatchedStudentName,
    decimal SimilarityPercent,
    int PassageCount);

/// <summary>Both sides of the match viewer, already cut into render-safe segments.</summary>
/// <param name="IdenticalFiles">
/// The two files are byte-identical, found by hash; there are no passages to
/// highlight because all of it matches.
/// </param>
/// <param name="SourceChangedSinceAnalysis">
/// Either file's text was re-extracted after this analysis ran, so the stored
/// offsets may no longer line up. Re-running the analysis fixes it.
/// </param>
public sealed record ComparisonDto(
    int MatchId,
    int SubmissionId,
    string SubmissionTitle,
    decimal SimilarityPercent,
    int PassageCount,
    bool IdenticalFiles,
    bool SourceChangedSinceAnalysis,
    ComparisonSideDto Submitted,
    ComparisonSideDto Source);

public sealed record ComparisonSideDto(
    string FileName,
    string SubmissionTitle,
    string StudentName,
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
