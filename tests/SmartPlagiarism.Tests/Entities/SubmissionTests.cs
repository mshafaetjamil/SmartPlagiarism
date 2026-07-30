using FluentAssertions;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Tests.Entities;

/// <summary>
/// Covers the two state machines on <see cref="Submission"/>. These are the rules
/// the background worker and the teacher review screen both depend on, so an
/// illegal transition has to throw rather than quietly corrupt the record.
/// </summary>
public class SubmissionTests
{
    private static Submission NewSubmission() => new()
    {
        Title = "Winnowing-based similarity detection",
        Type = SubmissionType.Report,
        SubmittedAtUtc = new DateTime(2026, 7, 30, 9, 0, 0, DateTimeKind.Utc),
    };

    private static Submission SubmissionInAnalysisState(AnalysisStatus target)
    {
        var submission = NewSubmission();

        switch (target)
        {
            case AnalysisStatus.Queued:
                break;
            case AnalysisStatus.Processing:
                submission.MarkProcessing();
                break;
            case AnalysisStatus.Completed:
                submission.MarkProcessing();
                submission.MarkCompleted();
                break;
            case AnalysisStatus.Failed:
                submission.MarkProcessing();
                submission.MarkFailed();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(target), target, "Unhandled analysis status.");
        }

        return submission;
    }

    // ---------------- initial state ----------------

    [Fact]
    public void NewSubmission_StartsQueuedAndPendingReview()
    {
        var submission = NewSubmission();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Queued);
        submission.Status.Should().Be(SubmissionStatus.PendingReview);
        submission.TeacherComment.Should().BeNull();
    }

    // ---------------- analysis lifecycle ----------------

    [Fact]
    public void MarkProcessing_FromQueued_MovesToProcessing()
    {
        var submission = SubmissionInAnalysisState(AnalysisStatus.Queued);

        submission.MarkProcessing();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Processing);
    }

    [Theory]
    [InlineData(AnalysisStatus.Processing)]
    [InlineData(AnalysisStatus.Completed)]
    [InlineData(AnalysisStatus.Failed)]
    public void MarkProcessing_FromAnythingButQueued_Throws(AnalysisStatus startingState)
    {
        var submission = SubmissionInAnalysisState(startingState);

        var act = submission.MarkProcessing;

        act.Should().Throw<InvalidOperationException>();
        submission.AnalysisStatus.Should().Be(startingState, "a rejected transition must not change state");
    }

    [Fact]
    public void MarkCompleted_FromProcessing_MovesToCompleted()
    {
        var submission = SubmissionInAnalysisState(AnalysisStatus.Processing);

        submission.MarkCompleted();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Completed);
    }

    [Theory]
    [InlineData(AnalysisStatus.Queued)]
    [InlineData(AnalysisStatus.Completed)]
    [InlineData(AnalysisStatus.Failed)]
    public void MarkCompleted_FromAnythingButProcessing_Throws(AnalysisStatus startingState)
    {
        var submission = SubmissionInAnalysisState(startingState);

        var act = submission.MarkCompleted;

        act.Should().Throw<InvalidOperationException>();
        submission.AnalysisStatus.Should().Be(startingState);
    }

    [Fact]
    public void MarkFailed_FromProcessing_MovesToFailed()
    {
        var submission = SubmissionInAnalysisState(AnalysisStatus.Processing);

        submission.MarkFailed();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
    }

    [Theory]
    [InlineData(AnalysisStatus.Queued)]
    [InlineData(AnalysisStatus.Completed)]
    [InlineData(AnalysisStatus.Failed)]
    public void MarkFailed_FromAnythingButProcessing_Throws(AnalysisStatus startingState)
    {
        var submission = SubmissionInAnalysisState(startingState);

        var act = submission.MarkFailed;

        act.Should().Throw<InvalidOperationException>();
        submission.AnalysisStatus.Should().Be(startingState);
    }

    [Theory]
    [InlineData(AnalysisStatus.Completed)]
    [InlineData(AnalysisStatus.Failed)]
    public void RequeueForAnalysis_FromFinishedState_ReturnsToQueued(AnalysisStatus startingState)
    {
        var submission = SubmissionInAnalysisState(startingState);

        submission.RequeueForAnalysis();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Queued);
    }

    [Theory]
    [InlineData(AnalysisStatus.Queued)]
    [InlineData(AnalysisStatus.Processing)]
    public void RequeueForAnalysis_WhileStillInFlight_Throws(AnalysisStatus startingState)
    {
        var submission = SubmissionInAnalysisState(startingState);

        var act = submission.RequeueForAnalysis;

        act.Should().Throw<InvalidOperationException>();
        submission.AnalysisStatus.Should().Be(startingState);
    }

    [Fact]
    public void FullPipeline_QueuedThroughRequeue_EndsBackAtQueued()
    {
        var submission = NewSubmission();

        submission.MarkProcessing();
        submission.MarkCompleted();
        submission.RequeueForAnalysis();
        submission.MarkProcessing();
        submission.MarkFailed();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Failed);

        submission.RequeueForAnalysis();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Queued);
    }

    // ---------------- teacher review ----------------

    [Fact]
    public void Approve_FromPendingReview_ApprovesAndStoresTrimmedComment()
    {
        var submission = NewSubmission();

        submission.Approve("  Good work.  ");

        submission.Status.Should().Be(SubmissionStatus.Approved);
        submission.TeacherComment.Should().Be("Good work.");
    }

    [Fact]
    public void Approve_WithoutComment_LeavesCommentNull()
    {
        var submission = NewSubmission();

        submission.Approve();

        submission.Status.Should().Be(SubmissionStatus.Approved);
        submission.TeacherComment.Should().BeNull();
    }

    [Fact]
    public void Approve_WithWhitespaceComment_LeavesCommentNull()
    {
        var submission = NewSubmission();

        submission.Approve("   ");

        submission.TeacherComment.Should().BeNull();
    }

    [Fact]
    public void Reject_FromPendingReview_RejectsAndStoresTrimmedComment()
    {
        var submission = NewSubmission();

        submission.Reject("  Sources are not cited.  ");

        submission.Status.Should().Be(SubmissionStatus.Rejected);
        submission.TeacherComment.Should().Be("Sources are not cited.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_WithoutAReason_Throws(string comment)
    {
        var submission = NewSubmission();

        var act = () => submission.Reject(comment);

        act.Should().Throw<ArgumentException>().WithParameterName("comment");
        submission.Status.Should().Be(SubmissionStatus.PendingReview);
    }

    [Fact]
    public void Approve_AfterAlreadyReviewed_Throws()
    {
        var submission = NewSubmission();
        submission.Approve();

        var act = () => submission.Approve();

        act.Should().Throw<InvalidOperationException>();
        submission.Status.Should().Be(SubmissionStatus.Approved);
    }

    [Fact]
    public void Reject_AfterApproval_Throws()
    {
        var submission = NewSubmission();
        submission.Approve();

        var act = () => submission.Reject("Changed my mind.");

        act.Should().Throw<InvalidOperationException>();
        submission.Status.Should().Be(SubmissionStatus.Approved);
    }

    [Fact]
    public void ReviewAndAnalysis_AreIndependent()
    {
        var submission = NewSubmission();

        submission.MarkProcessing();
        submission.MarkFailed();

        // A teacher can still act on work whose analysis failed.
        submission.Reject("Analysis failed and the file is corrupt.");

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        submission.Status.Should().Be(SubmissionStatus.Rejected);
    }
}
