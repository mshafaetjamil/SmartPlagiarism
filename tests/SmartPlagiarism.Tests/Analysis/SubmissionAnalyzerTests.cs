using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Extraction;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Engine.Matching;
using SmartPlagiarism.Tests.Extraction;

namespace SmartPlagiarism.Tests.Analysis;

/// <summary>
/// The worker's unit of work, end to end over a real database and real files:
/// the scenario Phase 6 is judged by - a second document copying a paragraph
/// from the first - plus every way a submission can fail to produce a result.
/// </summary>
public class SubmissionAnalyzerTests
{
    private const string Introduction =
        "This report describes a plagiarism detection system for academic submissions at our university, "
        + "covering the motivation for the project and the constraints set by the department.";

    private const string CopiedParagraph =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string Conclusion =
        "In conclusion the prototype met its goals and future work will add internet sources.";

    private const string OwnWork =
        "Our evaluation was carried out over two semesters with volunteers from the second year cohort, "
        + "who each submitted three short essays written under supervised conditions.";

    private static byte[] Docx(params string[] paragraphs) => DocxFixture.Create(paragraphs);

    [Fact]
    public async Task SecondSubmissionCopyingAParagraph_MatchesTheFirst()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var originalId = await harness.SubmitAsync(("original.docx", Docx(Introduction, CopiedParagraph, Conclusion)));
        var copyId = await harness.SubmitAsOtherStudentAsync(("copy.docx", Docx(OwnWork, CopiedParagraph)));

        await harness.AnalyzeAsync(originalId);
        await harness.AnalyzeAsync(copyId);

        await using var context = harness.NewContext();

        var originalFileId = await context.UploadedFiles.Where(file => file.SubmissionId == originalId).Select(file => file.Id).SingleAsync();
        var copyFileId = await context.UploadedFiles.Where(file => file.SubmissionId == copyId).Select(file => file.Id).SingleAsync();

        var copy = await context.Submissions
            .Include(submission => submission.PlagiarismResult!)
            .ThenInclude(result => result.Matches)
            .SingleAsync(submission => submission.Id == copyId);

        copy.AnalysisStatus.Should().Be(AnalysisStatus.Completed);
        copy.PlagiarismResult!.OverallSimilarityPercent.Should().BeInRange(1m, 99m, "one of two paragraphs was copied");

        var match = copy.PlagiarismResult.Matches.Should().ContainSingle().Subject;
        match.MatchedUploadedFileId.Should().Be(originalFileId);
        match.UploadedFileId.Should().Be(copyFileId);
        match.SimilarityPercent.Should().BePositive();

        var copyText = await context.ExtractedTexts.Where(text => text.UploadedFileId == copyFileId).Select(text => text.FullText).SingleAsync();
        var fragment = MatchedFragmentSerializer.Deserialize(match.MatchedFragmentsJson).Should().ContainSingle().Subject;
        copyText[fragment.TargetStart..fragment.TargetEnd].Should().Contain("Winnowing selects the minimum hash value");
    }

    [Fact]
    public async Task FirstSubmission_CompletesWithNoMatches_AndLeavesFingerprintsForLaterOnes()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("original.docx", Docx(Introduction, CopiedParagraph)));

        await harness.AnalyzeAsync(id);

        await using var context = harness.NewContext();
        var submission = await context.Submissions.Include(s => s.PlagiarismResult!).ThenInclude(r => r.Matches).SingleAsync();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Completed);
        submission.PlagiarismResult!.OverallSimilarityPercent.Should().Be(0m);
        submission.PlagiarismResult.Matches.Should().BeEmpty();
        (await context.DocumentFingerprints.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task SubmissionWithNoReadableText_FailsWithAReason_NotAZeroScore()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("empty.docx", Docx()));

        await harness.AnalyzeAsync(id);

        await using var context = harness.NewContext();
        var submission = await context.Submissions.Include(s => s.PlagiarismResult).SingleAsync();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        submission.AnalysisError.Should().Contain("No readable text").And.Contain("empty.docx");
        submission.PlagiarismResult.Should().BeNull("a 0% score would claim the work was checked and found original");
    }

    [Fact]
    public async Task UnexpectedError_FailsTheSubmissionWithoutLeakingTheException()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("original.docx", Docx(Introduction)));

        await using (var context = harness.NewContext())
        {
            await harness.NewAnalyzer(context, new ThrowingExtractionService()).AnalyzeAsync(id);
        }

        await using var check = harness.NewContext();
        var submission = await check.Submissions.SingleAsync();

        submission.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        submission.AnalysisError.Should().Contain("unexpected error").And.NotContain(ThrowingExtractionService.Secret);
    }

    [Fact]
    public async Task SubmissionNoLongerQueued_IsLeftAlone()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var id = await harness.SubmitAsync(("original.docx", Docx(Introduction)));
        await harness.AnalyzeAsync(id);

        var extraction = new ThrowingExtractionService();

        await using (var context = harness.NewContext())
        {
            // A duplicate queue entry for a submission that is already Completed.
            await harness.NewAnalyzer(context, extraction).AnalyzeAsync(id);
        }

        extraction.CallCount.Should().Be(0);

        await using var check = harness.NewContext();
        (await check.Submissions.SingleAsync()).AnalysisStatus.Should().Be(AnalysisStatus.Completed);
    }

    [Fact]
    public async Task ReRun_ReplacesTheResult_RatherThanAddingAnother()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var originalId = await harness.SubmitAsync(("original.docx", Docx(Introduction, CopiedParagraph)));
        var copyId = await harness.SubmitAsync(("copy.docx", Docx(OwnWork, CopiedParagraph)));

        await harness.AnalyzeAsync(originalId);
        await harness.AnalyzeAsync(copyId);

        await using (var context = harness.NewContext())
        {
            var copy = await context.Submissions.SingleAsync(submission => submission.Id == copyId);
            copy.RequeueForAnalysis();
            await context.SaveChangesAsync();
        }

        await harness.AnalyzeAsync(copyId);

        await using var check = harness.NewContext();
        (await check.PlagiarismResults.CountAsync(result => result.SubmissionId == copyId)).Should().Be(1);
        (await check.MatchedDocuments.CountAsync()).Should().Be(1);
        (await check.DocumentFingerprints.CountAsync()).Should().Be(2, "re-running replaces the file's fingerprints");
    }

    [Fact]
    public async Task ResumePending_ReturnsInterruptedAndQueuedSubmissions_OldestFirst()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var interruptedId = await harness.SubmitAsync(("a.docx", Docx(Introduction)));
        var queuedId = await harness.SubmitAsync(("b.docx", Docx(OwnWork)));

        await using (var context = harness.NewContext())
        {
            // The app stopped while analysing this one.
            var interrupted = await context.Submissions.SingleAsync(submission => submission.Id == interruptedId);
            interrupted.MarkProcessing();
            await context.SaveChangesAsync();
        }

        IReadOnlyList<int> pending;

        await using (var context = harness.NewContext())
        {
            pending = await harness.NewAnalyzer(context).ResumePendingAsync();
        }

        pending.Should().Equal(interruptedId, queuedId);

        await using var check = harness.NewContext();
        (await check.Submissions.SingleAsync(submission => submission.Id == interruptedId))
            .AnalysisStatus.Should().Be(AnalysisStatus.Queued);
    }

    [Fact]
    public async Task ReRunningTheOriginal_AfterSomeoneCopiedIt_DoesNotScoreItAsTheCopy()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var originalId = await harness.SubmitAsync(("original.docx", Docx(Introduction, CopiedParagraph, Conclusion)));
        var copyId = await harness.SubmitAsOtherStudentAsync(("copy.docx", Docx(OwnWork, CopiedParagraph)));

        await harness.AnalyzeAsync(originalId);
        await harness.AnalyzeAsync(copyId);

        await using (var context = harness.NewContext())
        {
            (await context.Submissions.SingleAsync(submission => submission.Id == originalId)).RequeueForAnalysis();
            await context.SaveChangesAsync();
        }

        await harness.AnalyzeAsync(originalId);

        await using var check = harness.NewContext();
        var original = await check.PlagiarismResults.Include(result => result.Matches).SingleAsync(result => result.SubmissionId == originalId);

        original.OverallSimilarityPercent.Should().Be(0m, "the copy came later; it is the copy's result that shows the match");
        original.Matches.Should().BeEmpty();
    }

    [Fact]
    public async Task ResubmissionBySameStudent_ListsTheEarlierOneWithoutCountingIt()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var firstId = await harness.SubmitAsync(("draft.docx", Docx(Introduction, CopiedParagraph)));
        var secondId = await harness.SubmitAsync(("final.docx", Docx(Introduction, CopiedParagraph, Conclusion)));

        await harness.AnalyzeAsync(firstId);
        await harness.AnalyzeAsync(secondId);

        await using var context = harness.NewContext();
        var second = await context.Submissions
            .Include(submission => submission.PlagiarismResult!)
            .ThenInclude(result => result.Matches)
            .SingleAsync(submission => submission.Id == secondId);

        second.PlagiarismResult!.OverallSimilarityPercent.Should().Be(0m);
        second.PlagiarismResult.Matches.Should().ContainSingle("the earlier draft is still listed for the teacher");
    }

    [Fact]
    public async Task CoverPageTheWholeClassShares_IsSetAsideRatherThanMatchingEveryClassmate()
    {
        const string coverPage =
            "Department of Computer Science and Engineering. A project report submitted in partial fulfilment "
            + "of the requirements for the degree of Bachelor of Science in Computer Science and Engineering.";

        await using var harness = await AnalysisTestHarness.CreateAsync();

        string[] classmatesOwnWork =
        [
            "Rainfall in the northern districts was recorded daily for two monsoon seasons by volunteers.",
            "The library catalogue was rebuilt around a search index that tolerates spelling mistakes.",
            "Solar panels on the hostel roof were monitored to compare cleaning schedules against output.",
            "A survey of rickshaw pullers examined how mobile payments changed their daily earnings.",
            "Traffic at the main junction was counted by camera to model the effect of a new signal.",
            "The canteen menu was redesigned using nutrition data collected from student volunteers.",
        ];

        foreach (var ownWork in classmatesOwnWork)
        {
            await harness.AnalyzeAsync(await harness.SubmitAsNewStudentAsync(("report.docx", Docx(coverPage, ownWork))));
        }

        var id = await harness.SubmitAsync(("mine.docx", Docx(coverPage, OwnWork)));
        await harness.AnalyzeAsync(id);

        await using var context = harness.NewContext();
        var result = await context.PlagiarismResults.Include(candidate => candidate.Matches).SingleAsync(candidate => candidate.SubmissionId == id);

        result.Matches.Should().BeEmpty("sharing the cover page is not copying from six classmates");
        result.OverallSimilarityPercent.Should().Be(0m);
        result.CommonTextPercent.Should().BeGreaterThan(20m);
    }

    [Fact]
    public async Task ResumePending_AfterAnEngineUpgrade_QueuesResultsTheOldEngineProduced()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("a.docx", Docx(Introduction)));
        await harness.AnalyzeAsync(id);

        await using (var context = harness.NewContext())
        {
            // As if an earlier engine had read and scored it.
            var result = await context.PlagiarismResults.SingleAsync(candidate => candidate.SubmissionId == id);
            result.EngineVersion = "0.0.1";
            await context.SaveChangesAsync();
        }

        await using (var context = harness.NewContext())
        {
            (await harness.NewAnalyzer(context).ResumePendingAsync()).Should().Equal(id);
        }

        await harness.AnalyzeAsync(id);

        await using var check = harness.NewContext();
        (await check.PlagiarismResults.SingleAsync(candidate => candidate.SubmissionId == id))
            .EngineVersion.Should().Be(SmartPlagiarism.Engine.EngineInfo.Version);
    }

    [Fact]
    public async Task ResumePending_InterruptedAtEveryAttempt_FailsInsteadOfQueuingAgain()
    {
        // A file that takes the server down would otherwise do so at every start.
        await using var harness = await AnalysisTestHarness.CreateAsync();

        var submissionId = await harness.SubmitAsync(("a.docx", Docx(Introduction)));

        for (var attempt = 1; attempt <= Submission.MaxAnalysisAttempts; attempt++)
        {
            await using (var context = harness.NewContext())
            {
                var submission = await context.Submissions.SingleAsync(candidate => candidate.Id == submissionId);
                submission.MarkProcessing();
                await context.SaveChangesAsync();
            }

            await using (var context = harness.NewContext())
            {
                var pending = await harness.NewAnalyzer(context).ResumePendingAsync();

                if (attempt < Submission.MaxAnalysisAttempts)
                {
                    pending.Should().Equal(submissionId);
                }
                else
                {
                    pending.Should().BeEmpty();
                }
            }
        }

        await using var check = harness.NewContext();
        var failed = await check.Submissions.SingleAsync(submission => submission.Id == submissionId);
        failed.AnalysisStatus.Should().Be(AnalysisStatus.Failed);
        failed.AnalysisError.Should().Contain("every time");
    }

    private sealed class ThrowingExtractionService : IExtractionService
    {
        public const string Secret = "C:\\secret\\connection-string";

        public int CallCount { get; private set; }

        public Task<ExtractionOutcome> ExtractAndPersistAsync(int uploadedFileId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            throw new InvalidOperationException($"Database unreachable at {Secret}");
        }
    }
}
