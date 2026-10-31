using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Submissions;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;
using SmartPlagiarism.Web.Models.Api.V1;

namespace SmartPlagiarism.Tests.Submissions;

/// <summary>
/// The matched sources on a student's own result page: what they may be told, and
/// that nobody else's name, number or title can reach them that way.
/// </summary>
public class StudentSourcesTests
{
    private const string Shared =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string TheirTitle = "The other student's private project title";

    private static SubmissionService NewService(AnalysisTestHarness harness, AppDbContext context) =>
        new(context, harness.Storage, new FileUploadValidator(), harness.Queue, new SubmissionAccessService(context), Options.Create(new SubmissionOptions()));

    /// <summary>The other student's work, then the student's copy of it, both analysed.</summary>
    private static async Task<int> CopyOfAnotherStudentsWorkAsync(AnalysisTestHarness harness)
    {
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([Shared])));

        await using (var context = harness.NewContext())
        {
            (await context.Submissions.SingleAsync(submission => submission.Id == theirs)).Title = TheirTitle;
            await context.SaveChangesAsync();
        }

        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(theirs);
        await harness.AnalyzeAsync(mine);
        return mine;
    }

    [Fact]
    public async Task AStudent_SeesWhatMatched_ButNeverWhoseItWas()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await CopyOfAnotherStudentsWorkAsync(harness);

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailForStudentAsync(mine, harness.StudentUserId);

        var source = detail!.Sources.Should().ContainSingle().Subject;
        source.Label.Should().Be("Another student's submission (CSE-400)");
        source.OwnFileName.Should().Be("mine.docx");
        source.PassageCount.Should().BeGreaterThan(0);
        source.SimilarityPercent.Should().BeGreaterThan(0);
        source.IsOwnEarlierWork.Should().BeFalse();

        var everythingOnThePage = JsonSerializer.Serialize(detail);
        everythingOnThePage.Should().NotContain("other@uni.edu").And.NotContain("2026-002")
            .And.NotContain(TheirTitle).And.NotContain("theirs.docx");
    }

    [Fact]
    public async Task AStudentsOwnEarlierWork_IsNamed_AndMarkedAsNotCounted()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var first = await harness.SubmitAsync(("first.docx", DocxFixture.Create([Shared])));
        var second = await harness.SubmitAsync(("second.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(first);
        await harness.AnalyzeAsync(second);

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailForStudentAsync(second, harness.StudentUserId);

        var source = detail!.Sources.Should().ContainSingle().Subject;
        source.IsOwnEarlierWork.Should().BeTrue();
        source.Label.Should().Contain("Your own submission").And.Contain("not counted");
    }

    [Fact]
    public async Task AStudent_CannotReachSourcesOfSomeoneElsesSubmission()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await CopyOfAnotherStudentsWorkAsync(harness);

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailForStudentAsync(mine, harness.OtherStudentUserId);

        detail.Should().BeNull();
    }

    [Fact]
    public async Task BeforeAnalysis_ThereAreNoSources()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailForStudentAsync(mine, harness.StudentUserId);

        detail!.Sources.Should().BeEmpty();
        detail.Files.Should().ContainSingle().Which.ReadBy.Should().BeNull();
    }

    [Fact]
    public async Task EachFile_SaysHowItWasRead_OnceAnalysed()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await CopyOfAnotherStudentsWorkAsync(harness);

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailForStudentAsync(mine, harness.StudentUserId);

        detail!.Files.Should().ContainSingle().Which.ReadBy.Should().Be(ExtractionMethod.Native);
    }

    [Fact]
    public async Task StaffViewers_DoNotLoadTheStudentWording_AndTheApiResponseIsUnchanged()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var mine = await CopyOfAnotherStudentsWorkAsync(harness);

        await using var context = harness.NewContext();
        var detail = await NewService(harness, context).GetDetailAsync(mine, SubmissionViewer.ForTeacher(harness.TeacherUserId));

        detail!.Sources.Should().BeEmpty();
        typeof(SubmissionApiResponse).GetProperties().Select(property => property.Name)
            .Should().NotContain(nameof(SubmissionDetailDto.Sources));
    }
}
