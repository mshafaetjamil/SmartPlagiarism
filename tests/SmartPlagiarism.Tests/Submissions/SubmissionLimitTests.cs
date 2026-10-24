using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Submissions;

/// <summary>
/// How much one student may submit: a few submissions waiting for analysis at
/// once, and a few a day - so one account cannot fill the disk or the analysis
/// queue while classmates' work waits.
/// </summary>
public class SubmissionLimitTests
{
    private static readonly byte[] s_docx = DocxFixture.Create(["A short report about fingerprinting and similarity."]);

    [Fact]
    public async Task WhileTheMostAllowedAreWaitingForAnalysis_AnotherIsRefused_UntilOneFinishes()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var waiting = new[] { await SubmitEarlierAsync(harness), await SubmitEarlierAsync(harness), await SubmitEarlierAsync(harness) };

        var refused = await CreateAsync(harness, new SubmissionOptions { MaxPendingPerStudent = 3 });

        refused.Succeeded.Should().BeFalse();
        refused.Errors.Should().ContainSingle().Which.Should().Contain("already have 3 submissions waiting to be analysed");
        harness.Queue.Enqueued.Should().BeEmpty();

        await CompleteAsync(harness, waiting[0]);

        (await CreateAsync(harness, new SubmissionOptions { MaxPendingPerStudent = 3 })).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task AfterTheMostAllowedInADay_AnotherIsRefused_AndTheStudentIsToldWhenToTryAgain()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var oldest = await SubmitEarlierAsync(harness, DateTime.UtcNow.AddHours(-22));
        await SubmitEarlierAsync(harness, DateTime.UtcNow.AddHours(-1));
        var limits = new SubmissionOptions { MaxPerStudentPerDay = 2, MaxPendingPerStudent = 100 };

        var refused = await CreateAsync(harness, limits);

        refused.Succeeded.Should().BeFalse();
        refused.Errors.Should().ContainSingle().Which.Should()
            .Contain("at most 2 submissions in 24 hours").And.Contain("submit again in 2 hour(s)");

        // A day on, the oldest no longer counts.
        await SetSubmittedAtAsync(harness, oldest, DateTime.UtcNow.AddHours(-25));

        (await CreateAsync(harness, limits)).Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task OtherStudentsSubmissions_DoNotCountAgainstTheLimit()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.SubmitAsOtherStudentAsync(("report.docx", s_docx));
        await harness.SubmitAsOtherStudentAsync(("report.docx", s_docx));

        var result = await CreateAsync(harness, new SubmissionOptions { MaxPendingPerStudent = 1, MaxPerStudentPerDay = 1 });

        result.Succeeded.Should().BeTrue();
    }

    private static async Task<CreateSubmissionResult> CreateAsync(AnalysisTestHarness harness, SubmissionOptions limits)
    {
        await using var context = harness.NewContext();
        var course = await context.Courses.SingleAsync();
        var semester = await context.Semesters.SingleAsync();
        var service = new SubmissionService(
            context,
            harness.Storage,
            new FileUploadValidator(),
            harness.Queue,
            new SubmissionAccessService(context),
            Options.Create(limits));

        await using var stream = new MemoryStream(s_docx);

        return await service.CreateAsync(new CreateSubmissionRequest(
            harness.StudentUserId,
            "Report",
            SubmissionType.Report,
            course.Id,
            semester.Id,
            [new FileUpload("report.docx", s_docx.Length, stream)]));
    }

    /// <summary>A queued submission by the harness's student, made before the one under test.</summary>
    private static async Task<int> SubmitEarlierAsync(AnalysisTestHarness harness, DateTime? submittedAtUtc = null)
    {
        var id = await harness.SubmitAsync(("report.docx", s_docx));

        if (submittedAtUtc is { } at)
        {
            await SetSubmittedAtAsync(harness, id, at);
        }

        return id;
    }

    private static async Task SetSubmittedAtAsync(AnalysisTestHarness harness, int submissionId, DateTime submittedAtUtc)
    {
        await using var context = harness.NewContext();
        var submission = await context.Submissions.SingleAsync(candidate => candidate.Id == submissionId);

        submission.SubmittedAtUtc = submittedAtUtc;
        await context.SaveChangesAsync();
    }

    private static async Task CompleteAsync(AnalysisTestHarness harness, int submissionId)
    {
        await using var context = harness.NewContext();
        var submission = await context.Submissions.SingleAsync(candidate => candidate.Id == submissionId);

        submission.MarkProcessing();
        submission.MarkCompleted();
        await context.SaveChangesAsync();
    }
}
