using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Identity;
using SmartPlagiarism.Infrastructure.Reports;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;
using SmartPlagiarism.Web.Authorization;
using UglyToad.PdfPig;

namespace SmartPlagiarism.Tests.Reports;

/// <summary>
/// A student must never reach another student's submission or report. Checked at
/// every layer that could leak it: the shared access rule, the authorization
/// handler the controllers use, and the report service's own check - which
/// holds even if a controller forgets the handler.
/// </summary>
public class SubmissionAccessTests
{
    private const string Shared =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string TheirOwnWords =
        "This paragraph was written by the other student and appears in nobody else's submission at all.";

    // ---------------- the rule ----------------

    [Fact]
    public async Task Student_CanViewTheirOwnSubmission_ButNotAnotherStudents()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();
        var access = new SubmissionAccessService(context);
        var student = new SubmissionViewer(harness.StudentUserId, IsStaff: false);

        (await access.CanViewAsync(mine, student)).Should().BeTrue();
        (await access.CanViewAsync(theirs, student)).Should().BeFalse();
        (await access.CanViewAsync(987654, student)).Should().BeFalse("a missing id looks exactly like someone else's");
    }

    [Fact]
    public async Task Teacher_SeesTheirDepartmentsSubmissions_AndNoOtherDepartments()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();
        var access = new SubmissionAccessService(context);

        (await access.CanViewAsync(theirs, SubmissionViewer.ForTeacher(harness.TeacherUserId))).Should().BeTrue();
        (await access.CanViewAsync(theirs, SubmissionViewer.ForTeacher(harness.OutsideTeacherUserId))).Should().BeFalse();
    }

    [Fact]
    public async Task Admin_CanViewAnySubmission()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();

        (await new SubmissionAccessService(context).CanViewAsync(theirs, SubmissionViewer.ForAdmin("admin-id")))
            .Should().BeTrue();
    }

    // ---------------- the authorization handler ----------------

    [Fact]
    public async Task Handler_DeniesAStudentAnotherStudentsSubmission()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();
        var handler = new SubmissionAccessHandler(new SubmissionAccessService(context));
        var student = Principal(harness.StudentUserId, ApplicationRoles.Student);

        (await Authorize(handler, student, mine)).Should().BeTrue();
        (await Authorize(handler, student, theirs)).Should().BeFalse();
    }

    [Fact]
    public async Task Handler_AllowsTheDepartmentsTeachersAndAdmins_AndRefusesEveryoneElse()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();
        var handler = new SubmissionAccessHandler(new SubmissionAccessService(context));

        (await Authorize(handler, Principal(harness.TeacherUserId, ApplicationRoles.Teacher), theirs)).Should().BeTrue();
        (await Authorize(handler, Principal(harness.OutsideTeacherUserId, ApplicationRoles.Teacher), theirs)).Should().BeFalse();
        (await Authorize(handler, Principal("admin-id", ApplicationRoles.Admin), theirs)).Should().BeTrue();
        (await Authorize(handler, new ClaimsPrincipal(new ClaimsIdentity()), theirs)).Should().BeFalse();
    }

    // ---------------- the report service ----------------

    [Fact]
    public async Task ReportService_RefusesAStudentAnotherStudentsReport_EvenWithoutTheHandler()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(theirs);

        await using var context = harness.NewContext();

        var report = await NewReportService(context)
            .GetSubmissionReportAsync(theirs, new SubmissionViewer(harness.StudentUserId, IsStaff: false));

        report.Should().BeNull();
    }

    [Fact]
    public async Task StudentsOwnReport_NeverNamesOrQuotesTheOtherStudent()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([TheirOwnWords, Shared])));
        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(theirs);
        await harness.AnalyzeAsync(mine);

        await using var context = harness.NewContext();
        var service = NewReportService(context);

        var studentCopy = await service.GetSubmissionReportAsync(mine, new SubmissionViewer(harness.StudentUserId, IsStaff: false));
        var staffCopy = await service.GetSubmissionReportAsync(mine, SubmissionViewer.ForTeacher(harness.TeacherUserId));

        var studentText = PdfText(studentCopy!.Content);
        var staffText = PdfText(staffCopy!.Content);

        studentCopy.FileName.Should().Be($"originality-report-{mine}.pdf");
        studentText.Should().Contain("Another student's submission").And.Contain("Student copy");
        studentText.Should().NotContain(harness.OtherStudentUserId).And.NotContain("other@uni.edu").And.NotContain("2026-002");

        staffText.Should().Contain("other@uni.edu").And.Contain("2026-002").And.Contain("Staff copy");
        staffText.Should().Contain("Matched source");
    }

    [Fact]
    public async Task Report_ShowsTheScoreDecisionAndWarnings()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var original = await harness.SubmitAsOtherStudentAsync(("original.docx", DocxFixture.Create([TheirOwnWords, Shared])));
        var copy = await harness.SubmitAsync(("copy.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(original);
        await harness.AnalyzeAsync(copy);

        await using var context = harness.NewContext();
        await new ReviewService(context, new AuditService(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuditService>.Instance))
            .RejectAsync(copy, "Copied from another submission.", harness.TeacherUserId);

        var report = await NewReportService(context).GetSubmissionReportAsync(copy, SubmissionViewer.ForTeacher(harness.TeacherUserId));
        var text = PdfText(report!.Content);

        report.ContentType.Should().Be("application/pdf");
        text.Should().Contain("Originality Report").And.Contain("Overall similarity").And.Contain("high");
        text.Should().Contain("Rejected").And.Contain("Copied from another submission.");
        text.Should().Contain("copy.docx").And.Contain("Top matched passages");
    }

    private static ReportService NewReportService(Infrastructure.Data.AppDbContext context) =>
        new(context, new SubmissionAccessService(context), new QuestPdfReportGenerator(), Options.Create(new ReportOptions()));

    private static ClaimsPrincipal Principal(string userId, string role) =>
        new(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId), new Claim(ClaimTypes.Role, role)],
            authenticationType: "Test"));

    private static async Task<bool> Authorize(SubmissionAccessHandler handler, ClaimsPrincipal user, int submissionId)
    {
        var requirement = new SubmissionAccessRequirement();
        var context = new AuthorizationHandlerContext([requirement], user, new SubmissionResource(submissionId));

        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
