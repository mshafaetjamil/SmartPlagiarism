using SmartPlagiarism.Core.DTOs.Submissions;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Student-facing submission operations. Every read is scoped to the requesting
/// student, so one student can never reach another's work.
/// </summary>
public interface ISubmissionService
{
    /// <summary>
    /// Validates the request and its files, stores the files, and creates the
    /// submission with AnalysisStatus = Queued. Nothing is written - to disk or to
    /// the database - unless every file passes validation.
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

    Task<SubmissionFormOptionsDto> GetFormOptionsAsync(CancellationToken cancellationToken = default);
}
