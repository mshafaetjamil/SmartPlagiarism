using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Analysis;

/// <summary>What the status endpoints return for each submission a page is waiting on.</summary>
public sealed record AnalysisStatusDto(int SubmissionId, AnalysisStatus AnalysisStatus);

public enum RequeueOutcome
{
    Requeued,
    NotFound,

    /// <summary>Already Queued or Processing; a second request would change nothing.</summary>
    AlreadyPending,
}

/// <summary>Limits shared by the status endpoints.</summary>
public static class AnalysisStatusQuery
{
    /// <summary>
    /// More ids than any page shows. Keeps a crafted request from turning the
    /// lightweight polling endpoint into an expensive one.
    /// </summary>
    public const int MaxSubmissionIds = 100;
}
