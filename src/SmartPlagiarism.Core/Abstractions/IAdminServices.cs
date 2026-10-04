using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>Accounts: listing, creation with a role and profile, and deactivation.</summary>
public interface IUserAdministrationService
{
    Task<PagedResult<UserListItemDto>> GetUsersAsync(UserListQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates the account, its role and - for students and teachers - its
    /// profile, or nothing at all: a failure part-way removes what was made.
    /// </summary>
    Task<AdminResult> CreateAsync(CreateUserRequest request, string adminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivating blocks sign-in and ends existing sessions; nothing is deleted,
    /// so the user's submissions and the audit trail stay intact. An admin cannot
    /// deactivate themselves.
    /// </summary>
    Task<AdminResult> SetActiveAsync(string userId, bool active, string adminUserId, CancellationToken cancellationToken = default);
}

/// <summary>Departments, courses and semesters. Anything in use cannot be deleted.</summary>
public interface IAcademicStructureService
{
    /// <summary>Every department, for choosing one from a dropdown.</summary>
    Task<IReadOnlyList<DepartmentDto>> GetDepartmentsAsync(CancellationToken cancellationToken = default);

    /// <summary>One page of the department list.</summary>
    Task<PagedResult<DepartmentDto>> GetDepartmentsAsync(int page, CancellationToken cancellationToken = default);

    Task<DepartmentDto?> GetDepartmentAsync(int id, CancellationToken cancellationToken = default);

    Task<AdminResult> CreateDepartmentAsync(DepartmentInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> UpdateDepartmentAsync(int id, DepartmentInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> DeleteDepartmentAsync(int id, string adminUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<CourseDto>> GetCoursesAsync(int page, CancellationToken cancellationToken = default);

    Task<CourseDto?> GetCourseAsync(int id, CancellationToken cancellationToken = default);

    Task<AdminResult> CreateCourseAsync(CourseInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> UpdateCourseAsync(int id, CourseInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> DeleteCourseAsync(int id, string adminUserId, CancellationToken cancellationToken = default);

    Task<PagedResult<SemesterDto>> GetSemestersAsync(int page, CancellationToken cancellationToken = default);

    Task<SemesterDto?> GetSemesterAsync(int id, CancellationToken cancellationToken = default);

    Task<AdminResult> CreateSemesterAsync(SemesterInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> UpdateSemesterAsync(int id, SemesterInput input, string adminUserId, CancellationToken cancellationToken = default);

    Task<AdminResult> DeleteSemesterAsync(int id, string adminUserId, CancellationToken cancellationToken = default);
}

/// <summary>The figures on the admin statistics page.</summary>
public interface IStatisticsService
{
    /// <param name="failedPage">The page of the failed-analyses list to include.</param>
    /// <param name="months">How many months back the submissions-over-time chart goes.</param>
    Task<SystemStatisticsDto> GetStatisticsAsync(int failedPage = 1, int months = 12, CancellationToken cancellationToken = default);
}

/// <summary>Reads the audit trail that <see cref="IAuditService"/> writes.</summary>
public interface IAuditLogReader
{
    Task<PagedResult<AuditLogEntryDto>> GetEntriesAsync(AuditLogQuery query, CancellationToken cancellationToken = default);

    /// <summary>The actions and entity types that occur in the log, for the filter dropdowns.</summary>
    Task<AuditLogFilterOptions> GetFilterOptionsAsync(CancellationToken cancellationToken = default);
}
