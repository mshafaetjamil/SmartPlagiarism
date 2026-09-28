using SmartPlagiarism.Core.DTOs.Analysis;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>Teacher-facing analysis operations.</summary>
public interface IAnalysisService
{
    /// <summary>
    /// Queues a finished submission to be analysed again - after OCR is installed,
    /// say, or after a failure. Refused while an analysis is already pending, and
    /// reported as not found outside a teacher's department.
    /// </summary>
    /// <param name="requestedBy">A teacher or an admin.</param>
    Task<RequeueOutcome> RequeueAsync(
        int submissionId,
        SubmissionViewer requestedBy,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Current analysis status of each requested submission that exists and the
    /// viewer may see.
    /// </summary>
    Task<IReadOnlyList<AnalysisStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<int> submissionIds,
        SubmissionViewer viewer,
        CancellationToken cancellationToken = default);
}
