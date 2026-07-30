using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Dashboards;

/// <summary>System-wide statistics for the administrator.</summary>
public sealed record AdminDashboardDto(
    int TotalUsers,
    IReadOnlyList<RoleUserCountDto> UsersByRole,
    int TotalSubmissions,
    IReadOnlyList<AnalysisStatusCountDto> SubmissionsByAnalysisStatus,
    decimal? AverageSimilarityPercent,
    int TotalUploadedFiles,
    long TotalStoredBytes);

public sealed record RoleUserCountDto(string Role, int UserCount);

public sealed record AnalysisStatusCountDto(AnalysisStatus Status, int Count);
