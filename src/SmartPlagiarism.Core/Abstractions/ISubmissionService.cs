using SmartPlagiarism.Core.DTOs.Analysis;
using SmartPlagiarism.Core.DTOs.Submissions;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Student-facing submission operations. Every read is scoped to the requesting
/// student, so one student can never reach another's work.
/// </summary>
public interface ISubmissionService
{
    /// <summary>
    /// Validates the request and its files, stores the files, creates the
    /// submission with AnalysisStatus = Queued and queues it for analysis. Nothing
    /// is written - to disk or to the database - unless every file passes
    /// validation.
    /// </summary>
    Task<CreateSubmissionResult> CreateAsync(
        CreateSubmissionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>A student's own submissions, newest first.</summary>
    Task<IReadOnlyList<SubmissionListItemDto>> GetForStudentAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One submission, or null when it does not exist or does not belong to this
    /// student. The two cases are deliberately indistinguishable to the caller.
    /// </summary>
    Task<SubmissionDetailDto?> GetDetailForStudentAsync(
        int submissionId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One submission, or null when it does not exist or the viewer may not see
    /// it - checked with <see cref="ISubmissionAccessService"/>. Serves the web
    /// page and the REST API alike.
    /// </summary>
    Task<SubmissionDetailDto?> GetDetailAsync(
        int submissionId,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default);

    Task<SubmissionFormOptionsDto> GetFormOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The account of the student with this number, or null - how the REST API
    /// turns the portal's student number into the student a submission is for.
    /// </summary>
    Task<string?> FindStudentUserIdAsync(string studentNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analysis status of the requested submissions that belong to this student.
    /// Ids of anyone else's work are silently left out.
    /// </summary>
    Task<IReadOnlyList<AnalysisStatusDto>> GetAnalysisStatusesForStudentAsync(
        string userId,
        IReadOnlyCollection<int> submissionIds,
        CancellationToken cancellationToken = default);
}
