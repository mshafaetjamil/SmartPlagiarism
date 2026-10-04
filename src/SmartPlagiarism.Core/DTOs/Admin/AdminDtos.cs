using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Core.DTOs.Admin;

/// <summary>What an admin change returns: done, or the reasons it was refused.</summary>
public sealed record AdminResult(bool Succeeded, IReadOnlyList<string> Errors)
{
    public static AdminResult Success { get; } = new(true, []);

    public static AdminResult Failure(params string[] errors) => new(false, errors);
}

// ---------------- users ----------------

public sealed record UserListQuery(string? Search = null, string? Role = null, bool? Active = null, int Page = 1)
{
    public const int PageSize = 25;
}

/// <param name="Identifier">Student or employee number; null for admins.</param>
/// <param name="LockedOutUntilUtc">
/// Set while locked out by failed sign-ins, which clears itself - unlike
/// deactivation, which only an admin reverses.
/// </param>
public sealed record UserListItemDto(
    string Id,
    string Email,
    string FullName,
    string? Role,
    string? Identifier,
    string? DepartmentCode,
    bool IsDeactivated,
    DateTimeOffset? LockedOutUntilUtc);

/// <summary>
/// A new account. Students need a department, student number and enrolment year;
/// teachers a department and employee number; admins neither.
/// </summary>
public sealed record CreateUserRequest(
    string Email,
    string FullName,
    string Password,
    string Role,
    int? DepartmentId,
    string? StudentNumber,
    int? EnrollmentYear,
    string? EmployeeNumber);

// ---------------- academic structure ----------------

public sealed record DepartmentDto(int Id, string Code, string Name, int CourseCount);

public sealed record DepartmentInput(string Code, string Name);

public sealed record CourseDto(int Id, int DepartmentId, string DepartmentCode, string Code, string Title, int SubmissionCount);

public sealed record CourseInput(int DepartmentId, string Code, string Title);

public sealed record SemesterDto(int Id, string Name, DateTime StartDateUtc, DateTime EndDateUtc, int SubmissionCount);

public sealed record SemesterInput(string Name, DateTime StartDateUtc, DateTime EndDateUtc);

// ---------------- statistics ----------------

public sealed record SystemStatisticsDto(
    int TotalSubmissions,
    int AnalysedSubmissions,
    double? AverageSimilarityPercent,
    IReadOnlyList<MonthlySubmissionsDto> SubmissionsByMonth,
    IReadOnlyList<ExtractionMethodCountDto> ExtractionMethods,
    IReadOnlyList<SimilarityBandCountDto> SimilarityBands,
    PagedResult<FailedAnalysisDto> FailedAnalyses);

public sealed record MonthlySubmissionsDto(int Year, int Month, int Count, double? AverageSimilarityPercent);

public sealed record ExtractionMethodCountDto(ExtractionMethod Method, int FileCount);

public sealed record SimilarityBandCountDto(SimilarityBand Band, int Count);

public sealed record FailedAnalysisDto(int SubmissionId, string Title, string StudentName, DateTime SubmittedAtUtc, string? Reason);

// ---------------- audit log ----------------

/// <param name="User">Matched against the email of the user who acted.</param>
/// <param name="FromDate">Inclusive, whole days.</param>
/// <param name="ToDate">Inclusive, whole days.</param>
public sealed record AuditLogQuery(
    string? Action = null,
    string? EntityType = null,
    string? User = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    int Page = 1)
{
    public const int PageSize = 50;
}

/// <param name="UserEmail">Null for system actions, or when the user has since been deleted.</param>
public sealed record AuditLogEntryDto(
    long Id,
    DateTime TimestampUtc,
    string? UserId,
    string? UserEmail,
    string Action,
    string EntityType,
    string? EntityId,
    string? Details);

public sealed record AuditLogFilterOptions(IReadOnlyList<string> Actions, IReadOnlyList<string> EntityTypes);
