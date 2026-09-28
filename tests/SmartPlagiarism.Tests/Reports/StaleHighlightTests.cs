using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Infrastructure.Reports;
using SmartPlagiarism.Infrastructure.Services;
using SmartPlagiarism.Tests.Analysis;
using SmartPlagiarism.Tests.Extraction;
using UglyToad.PdfPig;

namespace SmartPlagiarism.Tests.Reports;

/// <summary>
/// Matches store character offsets into both documents' text. When a text is
/// read again and comes out different, those offsets no longer point at the
/// matched words - and a report must not quote the wrong ones.
/// </summary>
public class StaleHighlightTests
{
    private const string Shared =
        "Winnowing selects the minimum hash value within each sliding window of consecutive fingerprints, "
        + "which guarantees that any sufficiently long shared passage between two documents will be found "
        + "while storing only a small fraction of all the hashes that a document produces.";

    private const string TheirOwnWords =
        "This paragraph was written by the other student and appears in nobody else's submission at all.";

    [Fact]
    public async Task ReadingAFileAgainWithTheSameResult_LeavesItsTextDated()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var id = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(id);

        var firstRead = await ExtractedAtAsync(harness, id);

        await using (var context = harness.NewContext())
        {
            (await context.Submissions.SingleAsync(submission => submission.Id == id)).RequeueForAnalysis();
            await context.SaveChangesAsync();
        }

        await harness.AnalyzeAsync(id);

        (await ExtractedAtAsync(harness, id)).Should().Be(firstRead, "the text did not change, so no offsets moved");
    }

    [Fact]
    public async Task StaffReport_DoesNotQuoteASourceWhoseTextChangedSinceTheAnalysis()
    {
        await using var harness = await AnalysisTestHarness.CreateAsync();
        var theirs = await harness.SubmitAsOtherStudentAsync(("theirs.docx", DocxFixture.Create([TheirOwnWords, Shared])));
        var mine = await harness.SubmitAsync(("mine.docx", DocxFixture.Create([Shared])));
        await harness.AnalyzeAsync(theirs);
        await harness.AnalyzeAsync(mine);

        await using var context = harness.NewContext();

        // The other student's file is read again later, and reads differently.
        var theirText = await context.ExtractedTexts.SingleAsync(text => text.UploadedFile.SubmissionId == theirs);
        theirText.FullText = "A new first line. " + theirText.FullText;
        theirText.ExtractedAtUtc = DateTime.UtcNow.AddHours(1);
        await context.SaveChangesAsync();

        var report = await new ReportService(
                context,
                new SubmissionAccessService(context),
                new QuestPdfReportGenerator(),
                Options.Create(new ReportOptions()))
            .GetSubmissionReportAsync(mine, SubmissionViewer.ForTeacher(harness.TeacherUserId));

        PdfText(report!.Content).Should().Contain("cannot be quoted reliably");
    }

    private static async Task<DateTime> ExtractedAtAsync(AnalysisTestHarness harness, int submissionId)
    {
        await using var context = harness.NewContext();
        return await context.ExtractedTexts
            .Where(text => text.UploadedFile.SubmissionId == submissionId)
            .Select(text => text.ExtractedAtUtc)
            .SingleAsync();
    }

    private static string PdfText(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        return string.Join(" ", document.GetPages().Select(page => string.Join(" ", page.GetWords().Select(word => word.Text))));
    }
}
