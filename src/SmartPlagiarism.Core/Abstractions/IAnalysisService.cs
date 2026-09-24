using SmartPlagiarism.Core.DTOs.Analysis;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>Teacher-facing analysis operations.</summary>
public interface IAnalysisService
{
    /// <summary>
    /// Queues a finished submission to be analysed again - after OCR is installed,
    /// say, or after a failure. Refused while an analysis is already pending.
    /// </summary>
    Task<RequeueOutcome> RequeueAsync(
        int submissionId,
        string requestedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Current analysis status of each requested submission that exists.</summary>
    Task<IReadOnlyList<AnalysisStatusDto>> GetStatusesAsync(
        IReadOnlyCollection<int> submissionIds,
        CancellationToken cancellationToken = default);
}
