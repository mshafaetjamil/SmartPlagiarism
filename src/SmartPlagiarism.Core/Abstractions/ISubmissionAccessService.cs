namespace SmartPlagiarism.Core.Abstractions;

/// <summary>Who is asking to see a submission.</summary>
/// <param name="UserId">The signed-in user's id.</param>
/// <param name="IsStaff">Teacher or admin - may see every submission.</param>
public sealed record SubmissionViewer(string UserId, bool IsStaff);

/// <summary>
/// The one rule for who may see a submission and anything derived from it - its
/// details, its analysis, its report: staff may see any submission, a student
/// only their own.
///
/// The web layer enforces it through an authorization handler, and services that
/// hand out submission data enforce it again themselves, so a controller that
/// forgets the check still cannot leak another student's work.
/// </summary>
public interface ISubmissionAccessService
{
    /// <summary>
    /// False both when the submission does not exist and when the viewer may not
    /// see it, so a student cannot probe for other students' submission ids.
    /// </summary>
    Task<bool> CanViewAsync(int submissionId, SubmissionViewer viewer, CancellationToken cancellationToken = default);
}
