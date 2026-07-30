using SmartPlagiarism.Core.DTOs.Dashboards;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Read-model queries backing the three role dashboards. Every figure comes from
/// a real query against the real tables - they simply return zero and empty lists
/// until there is data.
/// </summary>
public interface IDashboardService
{
    /// <summary>Statistics for one student, identified by their Identity user id.</summary>
    Task<StudentDashboardDto> GetStudentDashboardAsync(string userId, CancellationToken cancellationToken = default);

    Task<TeacherDashboardDto> GetTeacherDashboardAsync(CancellationToken cancellationToken = default);

    Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default);
}
