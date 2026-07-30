using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Dashboards;

/// <summary>What a student sees after signing in.</summary>
public sealed record StudentDashboardDto(
    int TotalSubmissions,
    int AwaitingAnalysis,
    int PendingReview,
    int Approved,
    int Rejected,
    IReadOnlyList<StudentSubmissionSummaryDto> LatestSubmissions)
{
    /// <summary>State for a student who has no submissions yet - or no profile.</summary>
    public static StudentDashboardDto Empty { get; } = new(0, 0, 0, 0, 0, []);
}

public sealed record StudentSubmissionSummaryDto(
    int SubmissionId,
    string Title,
    string CourseCode,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    SubmissionStatus Status,
    decimal? OverallSimilarityPercent);
