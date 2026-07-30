namespace SmartPlagiarism.Core.Enums;

/// <summary>
/// The teacher's verdict on a submission. Independent of <see cref="AnalysisStatus"/>:
/// a teacher may reject work whose analysis failed, or approve work with a high
/// similarity score after reviewing the matches.
/// </summary>
public enum SubmissionStatus
{
    PendingReview = 0,
    Approved = 1,
    Rejected = 2,
}
