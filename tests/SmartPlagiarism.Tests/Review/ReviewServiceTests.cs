using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs.Review;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Infrastructure.Data;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Review;

/// <summary>
/// The teacher module's service over real analysed submissions: the list's
/// filters and sort, the detail page, the comparison the viewer renders, and the
/// review verdict.
/// </summary>
public class ReviewServiceTests
{
    private const string Introduction =
        "This report describes a plagiarism detection system for academic submissions at our university, "
        + "covering the motivation for the project and the constraints set by the department.";

    private const string CopiedParagraph =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string OwnWork =
        "Our evaluation was carried out over two semesters with volunteers from the second year cohort, "
        + "who each submitted three short essays written under supervised conditions.";

    private static ReviewService NewService(AppDbContext context) =>
        new(context, new AuditService(context, NullLogger<AuditService>.Instance));

    /// <summary>An original, and a second submission copying one of its paragraphs, both analysed.</summary>
    private static async Task<(int OriginalId, int CopyId)> OriginalAndCopyAsync(AnalysisTestHarness harness)
    {
        var originalId = await harness.SubmitAsync(("original.docx", DocxFixture.Create([Introduction, CopiedParagraph])));
        var copyId = await harness.SubmitAsOtherStudentAsync(("copy.docx", DocxFixture.Create([OwnWork, CopiedParagraph])));

        await harness.AnalyzeAsync(originalId);
        await harness.AnalyzeAsync(copyId);

        return (originalId, copyId);
    }

    // ---------------- list ----------------

    [Fact]
    public async Task List_FiltersBySimilarityRange()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (originalId, copyId) = await OriginalAndCopyAsync(harness);

        await using var context = harness.NewContext();
        var service = NewService(context);

        var suspicious = await service.GetSubmissionsAsync(new ReviewSubmissionQuery(MinSimilarityPercent: 20));
        var clean = await service.GetSubmissionsAsync(new ReviewSubmissionQuery(MaxSimilarityPercent: 5));

        suspicious.Items.Select(item => item.Id).Should().Equal(copyId);
        clean.Items.Select(item => item.Id).Should().Equal(originalId);
    }

    [Fact]
    public async Task List_ARangeEnteredBackwards_IsSwapped()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (_, copyId) = await OriginalAndCopyAsync(harness);

        await using var context = harness.NewContext();

        var result = await NewService(context).GetSubmissionsAsync(
            new ReviewSubmissionQuery(MinSimilarityPercent: 100, MaxSimilarityPercent: 20));

        result.Items.Select(item => item.Id).Should().Equal(copyId);
    }

    [Fact]
    public async Task List_SortsBySimilarity_HighestFirst()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (originalId, copyId) = await OriginalAndCopyAsync(harness);

        await using var context = harness.NewContext();

        var result = await NewService(context).GetSubmissionsAsync(
            new ReviewSubmissionQuery(Sort: ReviewSortField.Similarity, Descending: true));

        result.Items.Select(item => item.Id).Should().Equal(copyId, originalId);
        result.Items[0].MatchCount.Should().Be(1);
    }

    [Fact]
    public async Task List_FiltersByReviewAndAnalysisStatus()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (originalId, copyId) = await OriginalAndCopyAsync(harness);
        var queuedId = await harness.SubmitAsync(("pending.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();
        var service = NewService(context);
        await service.RejectAsync(copyId, "Copied from another student.", "teacher");

        (await service.GetSubmissionsAsync(new ReviewSubmissionQuery(ReviewStatus: SubmissionStatus.Rejected)))
            .Items.Select(item => item.Id).Should().Equal(copyId);

        (await service.GetSubmissionsAsync(new ReviewSubmissionQuery(AnalysisStatus: AnalysisStatus.Queued)))
            .Items.Select(item => item.Id).Should().Equal(queuedId);

        (await service.GetSubmissionsAsync(new ReviewSubmissionQuery(CourseId: 999)))
            .Items.Should().BeEmpty();

        (await service.GetSubmissionsAsync(new ReviewSubmissionQuery(ReviewStatus: SubmissionStatus.PendingReview)))
            .Items.Select(item => item.Id).Should().BeEquivalentTo([originalId, queuedId]);
    }

    [Fact]
    public async Task List_PagesAreClampedToWhatExists()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await harness.SubmitAsync(("only.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();

        var result = await NewService(context).GetSubmissionsAsync(new ReviewSubmissionQuery(Page: 50));

        result.Page.Should().Be(1);
        result.Items.Should().ContainSingle();
        result.TotalPages.Should().Be(1);
    }

    // ---------------- detail ----------------

    [Fact]
    public async Task Detail_ListsTheMatchAndEachFilesExtraction()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (originalId, copyId) = await OriginalAndCopyAsync(harness);

        await using var context = harness.NewContext();

        var detail = await NewService(context).GetSubmissionAsync(copyId);

        detail!.Result!.OverallSimilarityPercent.Should().BePositive();

        var match = detail.Result.Matches.Should().ContainSingle().Subject;
        match.MatchedSubmissionId.Should().Be(originalId);
        match.OwnFileName.Should().Be("copy.docx");
        match.MatchedFileName.Should().Be("original.docx");
        match.PassageCount.Should().Be(1);

        var file = detail.Files.Should().ContainSingle().Subject;
        file.ExtractionMethod.Should().Be(ExtractionMethod.Native);
        file.WordCount.Should().BePositive();
    }

    [Fact]
    public async Task Detail_ShowsExtractionWarnings_SoALowScoreIsExplained()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("empty.docx", DocxFixture.Create([])));
        await harness.AnalyzeAsync(id);

        await using (var context = harness.NewContext())
        {
            // As a damaged Bangla PDF would record.
            var text = await context.ExtractedTexts.SingleAsync();
            text.Warnings = ["Page 1: the PDF's embedded Bangla text is damaged, so the page was read by OCR instead."];
            await context.SaveChangesAsync();
        }

        await using var check = harness.NewContext();
        var detail = await NewService(check).GetSubmissionAsync(id);

        detail!.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        detail.AnalysisError.Should().Contain("No readable text");
        detail.Files.Single().Warnings.Should().ContainSingle().Which.Should().Contain("read by OCR");
    }

    [Fact]
    public async Task Detail_OfAMissingSubmission_IsNull()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        (await NewService(context).GetSubmissionAsync(4242)).Should().BeNull();
    }

    // ---------------- comparison ----------------

    [Fact]
    public async Task Comparison_HighlightsTheCopiedParagraphOnBothSides()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (_, copyId) = await OriginalAndCopyAsync(harness);

        await using var context = harness.NewContext();
        var service = NewService(context);
        var matchId = (await service.GetSubmissionAsync(copyId))!.Result!.Matches.Single().MatchId;

        var comparison = await service.GetComparisonAsync(matchId);

        comparison!.PassageCount.Should().Be(1);
        comparison.IdenticalFiles.Should().BeFalse();
        comparison.SourceChangedSinceAnalysis.Should().BeFalse();
        comparison.Submitted.FileName.Should().Be("copy.docx");
        comparison.Source.FileName.Should().Be("original.docx");

        foreach (var side in new[] { comparison.Submitted, comparison.Source })
        {
            var highlighted = side.Segments.Where(segment => segment.IsHighlighted).Should().ContainSingle().Subject;
            highlighted.FragmentIndex.Should().Be(0);
            highlighted.Text.Should().Contain("Winnowing selects the minimum hash value").And.Contain("a document produces");
        }

        var submittedText = await context.ExtractedTexts
            .Where(text => text.UploadedFile.SubmissionId == copyId)
            .Select(text => text.FullText)
            .SingleAsync();

        string.Concat(comparison.Submitted.Segments.Select(segment => segment.Text)).Should().Be(submittedText);
    }

    [Fact]
    public async Task Comparison_WarnsWhenTheSourceWasReadAgainAfterTheAnalysis()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var (originalId, copyId) = await OriginalAndCopyAsync(harness);

        await using (var context = harness.NewContext())
        {
            var sourceText = await context.ExtractedTexts.SingleAsync(text => text.UploadedFile.SubmissionId == originalId);
            sourceText.ExtractedAtUtc = DateTime.UtcNow.AddHours(1);
            await context.SaveChangesAsync();
        }

        await using var check = harness.NewContext();
        var service = NewService(check);
        var matchId = (await service.GetSubmissionAsync(copyId))!.Result!.Matches.Single().MatchId;

        (await service.GetComparisonAsync(matchId))!.SourceChangedSinceAnalysis.Should().BeTrue();
    }

    [Fact]
    public async Task Comparison_OfAMissingMatch_IsNull()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        (await NewService(context).GetComparisonAsync(4242)).Should().BeNull();
    }

    // ---------------- verdict ----------------

    [Fact]
    public async Task Approve_RecordsTheVerdictAndAudits()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("report.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();

        var outcome = await NewService(context).ApproveAsync(id, "  Well done.  ", "teacher-id");

        outcome.Should().Be(ReviewDecisionOutcome.Recorded);

        var submission = await context.Submissions.SingleAsync();
        submission.Status.Should().Be(SubmissionStatus.Approved);
        submission.TeacherComment.Should().Be("Well done.");

        (await context.AuditLogs.SingleAsync()).Should().BeEquivalentTo(new
        {
            Action = AuditActions.SubmissionApproved,
            EntityType = AuditEntityTypes.Submission,
            EntityId = id.ToString(),
            UserId = "teacher-id",
            Details = "Well done.",
        });
    }

    [Fact]
    public async Task Reject_WithoutAComment_IsRefused()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("report.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();

        (await NewService(context).RejectAsync(id, "   ", "teacher-id")).Should().Be(ReviewDecisionOutcome.CommentRequired);

        (await context.Submissions.SingleAsync()).Status.Should().Be(SubmissionStatus.PendingReview);
        (await context.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reject_RecordsTheReasonAndAudits()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("report.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();

        (await NewService(context).RejectAsync(id, "Copied from another student.", "teacher-id"))
            .Should().Be(ReviewDecisionOutcome.Recorded);

        (await context.Submissions.SingleAsync()).TeacherComment.Should().Be("Copied from another student.");
        (await context.AuditLogs.SingleAsync()).Action.Should().Be(AuditActions.SubmissionRejected);
    }

    [Fact]
    public async Task ASecondVerdict_IsRefused()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("report.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();
        var service = NewService(context);

        await service.ApproveAsync(id, null, "teacher-id");

        (await service.RejectAsync(id, "Changed my mind.", "teacher-id")).Should().Be(ReviewDecisionOutcome.AlreadyReviewed);
        (await context.Submissions.SingleAsync()).Status.Should().Be(SubmissionStatus.Approved);
    }

    [Fact]
    public async Task AnOverlongComment_IsRefusedBeforeTouchingTheDatabase()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("report.docx", DocxFixture.Create([OwnWork])));

        await using var context = harness.NewContext();

        (await NewService(context).ApproveAsync(id, new string('x', 1001), "teacher-id"))
            .Should().Be(ReviewDecisionOutcome.CommentTooLong);
    }

    [Fact]
    public async Task VerdictOnAMissingSubmission_ReportsNotFound()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        await using var context = harness.NewContext();

        (await NewService(context).ApproveAsync(4242, null, "teacher-id")).Should().Be(ReviewDecisionOutcome.NotFound);
    }
}
