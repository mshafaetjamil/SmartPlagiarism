using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs.Analysis;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Analysis;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Analysis;

/// <summary>
/// The web-facing side of the pipeline: queuing on creation, the teacher's
/// re-run, and the status endpoints the pages poll.
/// </summary>
public class AnalysisServiceTests
{
    private static byte[] Docx() => DocxFixture.Create(["A short report about fingerprinting and similarity."]);

    [Fact]
    public async Task CreatingASubmission_QueuesItForAnalysis()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        var course = await context.Courses.SingleAsync();
        var semester = await context.Semesters.SingleAsync();
        var service = new SubmissionService(context, harness.Storage, new FileUploadValidator(), harness.Queue, new SubmissionAccessService(context));

        var bytes = Docx();
        await using var stream = new MemoryStream(bytes);

        var result = await service.CreateAsync(new CreateSubmissionRequest(
            harness.StudentUserId,
            "Report",
            SubmissionType.Report,
            course.Id,
            semester.Id,
            [new FileUpload("report.docx", bytes.Length, stream)]));

        result.Succeeded.Should().BeTrue();
        harness.Queue.Enqueued.Should().Equal(result.SubmissionId);
    }

    [Fact]
    public async Task Rerun_OfAFinishedSubmission_RequeuesAndAudits()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("report.docx", Docx()));
        await harness.AnalyzeAsync(id);

        await using var context = harness.NewContext();
        var service = NewService(context, harness);

        var outcome = await service.RequeueAsync(id, "teacher-user-id");

        outcome.Should().Be(RequeueOutcome.Requeued);
        harness.Queue.Enqueued.Should().Equal(id);
        (await context.Submissions.SingleAsync()).AnalysisStatus.Should().Be(AnalysisStatus.Queued);
        (await context.AuditLogs.SingleAsync()).Should().BeEquivalentTo(new
        {
            Action = AuditActions.AnalysisRequeued,
            EntityType = AuditEntityTypes.Submission,
            EntityId = id.ToString(),
            UserId = "teacher-user-id",
        });
    }

    [Fact]
    public async Task Rerun_WhileStillQueued_IsRefused()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("report.docx", Docx()));

        await using var context = harness.NewContext();
        var outcome = await NewService(context, harness).RequeueAsync(id, "teacher-user-id");

        outcome.Should().Be(RequeueOutcome.AlreadyPending);
        harness.Queue.Enqueued.Should().BeEmpty();
    }

    [Fact]
    public async Task Rerun_OfAMissingSubmission_ReportsNotFound()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        (await NewService(context, harness).RequeueAsync(12345, "teacher-user-id")).Should().Be(RequeueOutcome.NotFound);
    }

    [Fact]
    public async Task StudentStatusQuery_NeverReturnsAnotherStudentsSubmission()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var mine = await harness.SubmitAsync(("mine.docx", Docx()));
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", Docx()));

        await using var context = harness.NewContext();
        var service = new SubmissionService(context, harness.Storage, new FileUploadValidator(), harness.Queue, new SubmissionAccessService(context));

        var statuses = await service.GetAnalysisStatusesForStudentAsync(harness.StudentUserId, [mine, theirs]);

        statuses.Should().Equal(new AnalysisStatusDto(mine, AnalysisStatus.Queued));
    }

    [Fact]
    public async Task TeacherStatusQuery_CapsTheNumberOfIds()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.SubmitAsync(("report.docx", Docx()));

        await using var context = harness.NewContext();
        var ids = Enumerable.Range(1, AnalysisStatusQuery.MaxSubmissionIds * 3).ToList();

        var statuses = await NewService(context, harness).GetStatusesAsync(ids);

        statuses.Should().ContainSingle();
    }

    private static AnalysisService NewService(Infrastructure.Data.AppDbContext context, AnalysisTestHarness harness) =>
        new(context, harness.Queue, new AuditService(context, NullLogger<AuditService>.Instance));
}
