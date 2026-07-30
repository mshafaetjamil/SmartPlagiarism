using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Dashboards;

/// <summary>
/// What a teacher sees after signing in. Scoped system-wide rather than to the
/// teacher's own department, matching the "list all submissions" teacher module.
/// </summary>
public sealed record TeacherDashboardDto(
    int PendingReviewCount,
    int AnalyzedSubmissionCount,
    decimal? AverageSimilarityPercent,
    IReadOnlyList<TeacherSubmissionSummaryDto> RecentSubmissions);

public sealed record TeacherSubmissionSummaryDto(
    int SubmissionId,
    string Title,
    string StudentName,
    string StudentNumber,
    string CourseCode,
    DateTime SubmittedAtUtc,
    AnalysisStatus AnalysisStatus,
    SubmissionStatus Status,
    decimal? OverallSimilarityPercent);
