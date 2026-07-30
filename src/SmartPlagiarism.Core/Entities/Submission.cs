using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// One piece of work handed in by a student, carrying one or more uploaded files.
///
/// A submission tracks two independent lifecycles:
/// <list type="bullet">
///   <item><description><see cref="AnalysisStatus"/> - progress through the background analysis pipeline.</description></item>
///   <item><description><see cref="Status"/> - the teacher's review verdict.</description></item>
/// </list>
/// Both are read-only from the outside and change only through the methods below,
/// so an illegal transition fails loudly instead of silently corrupting state.
/// </summary>
public class Submission
{
    public int Id { get; set; }

    public int StudentProfileId { get; set; }

    public StudentProfile StudentProfile { get; set; } = null!;

    public int CourseId { get; set; }

    public Course Course { get; set; } = null!;

    public int SemesterId { get; set; }

    public Semester Semester { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public SubmissionType Type { get; set; }

    public DateTime SubmittedAtUtc { get; set; }

    public AnalysisStatus AnalysisStatus { get; private set; } = AnalysisStatus.Queued;

    public SubmissionStatus Status { get; private set; } = SubmissionStatus.PendingReview;

    /// <summary>Set when a teacher approves or rejects. Required for a rejection.</summary>
    public string? TeacherComment { get; private set; }

    public ICollection<UploadedFile> Files { get; } = [];

    /// <summary>The current analysis result, or null until an analysis completes.</summary>
    public PlagiarismResult? PlagiarismResult { get; set; }

    // ---------------- analysis lifecycle ----------------
    // Queued -> Processing -> Completed | Failed, and Completed/Failed -> Queued
    // when an admin or teacher asks for a re-run.

    /// <summary>Claims a queued submission for analysis.</summary>
    public void MarkProcessing()
    {
        if (AnalysisStatus != AnalysisStatus.Queued)
        {
            throw InvalidTransition(nameof(MarkProcessing));
        }

        AnalysisStatus = AnalysisStatus.Processing;
    }

    /// <summary>Records that analysis finished successfully.</summary>
    public void MarkCompleted()
    {
        if (AnalysisStatus != AnalysisStatus.Processing)
        {
            throw InvalidTransition(nameof(MarkCompleted));
        }

        AnalysisStatus = AnalysisStatus.Completed;
    }

    /// <summary>Records that analysis failed and will not produce a result.</summary>
    public void MarkFailed()
    {
        if (AnalysisStatus != AnalysisStatus.Processing)
        {
            throw InvalidTransition(nameof(MarkFailed));
        }

        AnalysisStatus = AnalysisStatus.Failed;
    }

    /// <summary>Puts a finished submission back on the queue for re-analysis.</summary>
    public void RequeueForAnalysis()
    {
        if (AnalysisStatus is not (AnalysisStatus.Completed or AnalysisStatus.Failed))
        {
            throw InvalidTransition(nameof(RequeueForAnalysis));
        }

        AnalysisStatus = AnalysisStatus.Queued;
    }

    // ---------------- teacher review ----------------

    /// <summary>Approves the submission, optionally with a comment.</summary>
    public void Approve(string? comment = null)
    {
        if (Status != SubmissionStatus.PendingReview)
        {
            throw InvalidTransition(nameof(Approve));
        }

        Status = SubmissionStatus.Approved;
        TeacherComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    }

    /// <summary>
    /// Rejects the submission. A reason is mandatory - a rejection the student
    /// cannot act on is not useful feedback.
    /// </summary>
    public void Reject(string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            throw new ArgumentException("A rejection must explain why.", nameof(comment));
        }

        if (Status != SubmissionStatus.PendingReview)
        {
            throw InvalidTransition(nameof(Reject));
        }

        Status = SubmissionStatus.Rejected;
        TeacherComment = comment.Trim();
    }

    private InvalidOperationException InvalidTransition(string operation) =>
        new($"Cannot {operation} on submission {Id}: analysis status is {AnalysisStatus} "
            + $"and review status is {Status}.");
}
