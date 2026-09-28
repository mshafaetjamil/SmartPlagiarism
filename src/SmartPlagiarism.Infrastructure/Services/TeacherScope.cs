using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <summary>
/// What a teacher may see: submissions to courses of the teacher's own
/// department. Everything a teacher reads or acts on goes through this, so a
/// teacher in one department cannot browse another's students' work.
/// </summary>
internal static class TeacherScope
{
    public static IQueryable<Submission> VisibleToTeacher(
        this IQueryable<Submission> submissions,
        AppDbContext context,
        string teacherUserId) =>
        submissions.Where(submission => context.TeacherProfiles.Any(teacher =>
            teacher.UserId == teacherUserId && teacher.DepartmentId == submission.Course.DepartmentId));

    /// <summary>Everything for an admin; for a teacher, <see cref="VisibleToTeacher"/>.</summary>
    public static IQueryable<Submission> VisibleToStaff(
        this IQueryable<Submission> submissions,
        AppDbContext context,
        SubmissionViewer staff) =>
        staff.IsAdmin ? submissions : submissions.VisibleToTeacher(context, staff.UserId);
}
