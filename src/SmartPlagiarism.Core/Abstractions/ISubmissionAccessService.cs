namespace SmartPlagiarism.Core.Abstractions;

/// <summary>Who is asking to see a submission.</summary>
/// <param name="UserId">The signed-in user's id.</param>
/// <param name="IsStaff">
/// Teacher or admin: sees the staff copy of a report, which names and quotes
/// every source.
/// </param>
/// <param name="ApiKeyId">
/// Set when the viewer is a REST API client, which may see only the submissions
/// made through its own key - so a leaked key exposes nothing else.
/// </param>
/// <param name="IsAdmin">
/// An admin sees every submission; a teacher only those in courses of their
/// own department.
/// </param>
public sealed record SubmissionViewer(string UserId, bool IsStaff, int? ApiKeyId = null, bool IsAdmin = false)
{
    public static SubmissionViewer ForStudent(string userId) => new(userId, IsStaff: false);

    public static SubmissionViewer ForTeacher(string userId) => new(userId, IsStaff: true);

    public static SubmissionViewer ForAdmin(string userId) => new(userId, IsStaff: true, IsAdmin: true);

    public static SubmissionViewer ForApiClient(ApiClient client) =>
        new($"api-key:{client.ApiKeyId}", IsStaff: false, client.ApiKeyId);
}

/// <summary>
/// The one rule for who may see a submission and anything derived from it - its
/// details, its analysis, its report: an admin may see any submission, a teacher
/// those in their own department's courses, a student only their own, an API
/// client only those it submitted.
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
