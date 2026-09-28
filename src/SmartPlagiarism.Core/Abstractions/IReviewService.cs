using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Review;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// The teacher's side of the system: browsing submissions, reading their
/// analysis, comparing matched passages, and recording a verdict. A teacher
/// reaches only submissions to courses of their own department; anything else
/// is reported as not found.
/// </summary>
public interface IReviewService
{
    Task<PagedResult<ReviewSubmissionListItemDto>> GetSubmissionsAsync(
        ReviewSubmissionQuery query,
        string teacherUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Null when the submission does not exist or is outside the teacher's department.</summary>
    Task<ReviewSubmissionDetailDto?> GetSubmissionAsync(
        int submissionId,
        string teacherUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The side-by-side comparison for one matched document. Null when the match
    /// does not exist, has no stored source to compare with, or involves no
    /// submission in the teacher's department.
    /// </summary>
    Task<ComparisonDto?> GetComparisonAsync(int matchId, string teacherUserId, CancellationToken cancellationToken = default);

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
