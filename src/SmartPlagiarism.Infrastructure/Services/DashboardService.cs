using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Dashboards;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <summary>
/// <inheritdoc cref="IDashboardService"/>
///
/// Every query projects straight into a DTO, so no Include is needed and no EF
/// entity escapes this class. All queries are AsNoTracking - nothing here is
/// ever written back.
/// </summary>
public class DashboardService : IDashboardService
{
    private const int RecentSubmissionCount = 5;

    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<StudentDashboardDto> GetStudentDashboardAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var studentProfileId = await _context.StudentProfiles
            .AsNoTracking()
            .Where(profile => profile.UserId == userId)
            .Select(profile => (int?)profile.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // A user in the Student role who has no StudentProfile yet cannot own any
        // submissions, so there is nothing to count.
        if (studentProfileId is null)
        {
            return StudentDashboardDto.Empty;
        }

        var submissions = _context.Submissions
            .AsNoTracking()
            .Where(submission => submission.StudentProfileId == studentProfileId);

        var counts = await submissions
            .GroupBy(_ => 1)
            .Select(group => new
            {
                Total = group.Count(),
                AwaitingAnalysis = group.Count(submission =>
                    submission.AnalysisStatus == AnalysisStatus.Queued
                    || submission.AnalysisStatus == AnalysisStatus.Processing),
                PendingReview = group.Count(submission => submission.Status == SubmissionStatus.PendingReview),
                Approved = group.Count(submission => submission.Status == SubmissionStatus.Approved),
                Rejected = group.Count(submission => submission.Status == SubmissionStatus.Rejected),
            })
            .FirstOrDefaultAsync(cancellationToken);

        var latestSubmissions = await submissions
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .Take(RecentSubmissionCount)
            .Select(submission => new StudentSubmissionSummaryDto(
                submission.Id,
                submission.Title,
                submission.Course.Code,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.Status,
                submission.PlagiarismResult != null
                    ? submission.PlagiarismResult.OverallSimilarityPercent
                    : null))
            .ToListAsync(cancellationToken);

        return new StudentDashboardDto(
            counts?.Total ?? 0,
            counts?.AwaitingAnalysis ?? 0,
            counts?.PendingReview ?? 0,
            counts?.Approved ?? 0,
            counts?.Rejected ?? 0,
            latestSubmissions);
    }

    public async Task<TeacherDashboardDto> GetTeacherDashboardAsync(CancellationToken cancellationToken = default)
    {
        var pendingReviewCount = await _context.Submissions
            .AsNoTracking()
            .CountAsync(submission => submission.Status == SubmissionStatus.PendingReview, cancellationToken);

        var analyzedResults = _context.PlagiarismResults.AsNoTracking();

        var analyzedSubmissionCount = await analyzedResults.CountAsync(cancellationToken);

        // Projecting to decimal? first means an empty table yields null rather than
        // throwing on a NULL average.
        var averageSimilarity = await analyzedResults
            .Select(result => (decimal?)result.OverallSimilarityPercent)
            .AverageAsync(cancellationToken);

        var recentSubmissions = await _context.Submissions
            .AsNoTracking()
            .OrderByDescending(submission => submission.SubmittedAtUtc)
            .Take(RecentSubmissionCount)
            .Select(submission => new TeacherSubmissionSummaryDto(
                submission.Id,
                submission.Title,
                submission.StudentProfile.User.FullName,
                submission.StudentProfile.StudentNumber,
                submission.Course.Code,
                submission.SubmittedAtUtc,
                submission.AnalysisStatus,
                submission.Status,
                submission.PlagiarismResult != null
                    ? submission.PlagiarismResult.OverallSimilarityPercent
                    : null))
            .ToListAsync(cancellationToken);

        return new TeacherDashboardDto(
            pendingReviewCount,
            analyzedSubmissionCount,
            averageSimilarity,
            recentSubmissions);
    }

    public async Task<AdminDashboardDto> GetAdminDashboardAsync(CancellationToken cancellationToken = default)
    {
        var totalUsers = await _context.Users.AsNoTracking().CountAsync(cancellationToken);

        var roleCounts = await (
                from userRole in _context.UserRoles.AsNoTracking()
                join role in _context.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                group userRole by role.Name into grouped
                select new { Role = grouped.Key, Count = grouped.Count() })
            .ToListAsync(cancellationToken);

        // Driven from ApplicationRoles rather than from the query so a role with no
        // users still shows up, as a zero.
        var usersByRole = ApplicationRoles.All
            .Select(role => new RoleUserCountDto(
                role,
                roleCounts.FirstOrDefault(count => count.Role == role)?.Count ?? 0))
            .ToList();

        var totalSubmissions = await _context.Submissions.AsNoTracking().CountAsync(cancellationToken);

        var statusCounts = await _context.Submissions
            .AsNoTracking()
            .GroupBy(submission => submission.AnalysisStatus)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        var submissionsByAnalysisStatus = Enum.GetValues<AnalysisStatus>()
            .Select(status => new AnalysisStatusCountDto(
                status,
                statusCounts.FirstOrDefault(count => count.Status == status)?.Count ?? 0))
            .ToList();

        var averageSimilarity = await _context.PlagiarismResults
            .AsNoTracking()
            .Select(result => (decimal?)result.OverallSimilarityPercent)
            .AverageAsync(cancellationToken);

        var totalUploadedFiles = await _context.UploadedFiles.AsNoTracking().CountAsync(cancellationToken);

        var totalStoredBytes = await _context.UploadedFiles
            .AsNoTracking()
            .SumAsync(file => (long?)file.SizeBytes, cancellationToken) ?? 0L;

        return new AdminDashboardDto(
            totalUsers,
            usersByRole,
            totalSubmissions,
            submissionsByAnalysisStatus,
            averageSimilarity,
            totalUploadedFiles,
            totalStoredBytes);
    }
}
