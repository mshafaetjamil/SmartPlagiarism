using SmartPlagiarism.Core.DTOs.Review;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// The teacher's side of the system: browsing all submissions, reading their
/// analysis, comparing matched passages, and recording a verdict.
/// </summary>
public interface IReviewService
{
    Task<PagedResult<ReviewSubmissionListItemDto>> GetSubmissionsAsync(
        ReviewSubmissionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Null when the submission does not exist.</summary>
    Task<ReviewSubmissionDetailDto?> GetSubmissionAsync(int submissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The side-by-side comparison for one matched document. Null when the match
    /// does not exist or has no stored source to compare with.
    /// </summary>
    Task<ComparisonDto?> GetComparisonAsync(int matchId, CancellationToken cancellationToken = default);

    Task<ReviewDecisionOutcome> ApproveAsync(
        int submissionId,
        string? comment,
        string teacherUserId,
        CancellationToken cancellationToken = default);

    Task<ReviewDecisionOutcome> RejectAsync(
        int submissionId,
        string? comment,
        string teacherUserId,
        CancellationToken cancellationToken = default);
}
