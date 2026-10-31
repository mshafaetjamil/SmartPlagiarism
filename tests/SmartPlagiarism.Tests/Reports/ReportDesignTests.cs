using FluentAssertions;
using SmartPlagiarism.Core.DTOs.Reports;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Infrastructure.Reports;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartPlagiarism.Tests.Reports;

/// <summary>
/// The report's look, read back out of the PDF itself. A font the generator cannot
/// find falls back without any error, so only the embedded fonts can show that the
/// headings really are in the serif.
/// </summary>
public class ReportDesignTests
{
    private const string BanglaTitle = "\u09AC\u09BE\u0982\u09B2\u09BE \u09AD\u09BE\u09B7\u09BE \u0986\u09A8\u09CD\u09A6\u09CB\u09B2\u09A8\u09C7\u09B0 \u0987\u09A4\u09BF\u09B9\u09BE\u09B8";

    private static byte[] Report(decimal similarity = 12m, string title = "Clean power") =>
        new QuestPdfReportGenerator().GenerateSubmissionReport(Data(similarity, title));

    private static SubmissionReportData WithMatch(int passages) =>
        Data(100m, "Clean power") with { Matches = [new ReportMatchLine("Another student's submission (CSE401)", "report.pdf", 100m, passages)] };

    private static SubmissionReportData Data(decimal similarity, string title) =>
        new SubmissionReportData(
            "Test University",
            "Computer Science",
            ForStaff: false,
            7,
            title,
            SubmissionType.Report,
            "CSE401",
            "Software Engineering",
            "Summer 2026",
            new DateTime(2026, 10, 24, 8, 0, 0, DateTimeKind.Utc),
            "Demo Student",
            "2026-001",
            AnalysisStatus.Completed,
            null,
            similarity,
            0m,
            new DateTime(2026, 10, 24, 8, 5, 0, DateTimeKind.Utc),
            "0.2.0",
            [new ReportFileLine("report.pdf", ExtractionMethod.Native, 1200, [])],
            [],
            [],
            SubmissionStatus.PendingReview,
            null,
            new DateTime(2026, 10, 25, 8, 0, 0, DateTimeKind.Utc));

    private static Page FirstPage(byte[] pdf, out PdfDocument document)
    {
        document = PdfDocument.Open(pdf);
        return document.GetPage(1);
    }

    /// <summary>The letters of the first appearance of a text, ignoring any spacing between them.</summary>
    private static IReadOnlyList<Letter> LettersOf(Page page, string text)
    {
        var letters = page.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).ToList();
        var flat = string.Concat(letters.Select(letter => letter.Value));
        var start = flat.IndexOf(text.Replace(" ", string.Empty), StringComparison.Ordinal);

        start.Should().BeGreaterThanOrEqualTo(0, $"\"{text}\" should be on the page");
        return letters.Skip(start).Take(text.Replace(" ", string.Empty).Length).ToList();
    }

    private static bool InFont(Letter letter, string font) => letter.FontName?.Contains(font) == true;

    private static string Hex(Letter letter)
    {
        var (red, green, blue) = letter.Color.ToRGBValues();
        return $"#{(int)Math.Round(red * 255):X2}{(int)Math.Round(green * 255):X2}{(int)Math.Round(blue * 255):X2}";
    }

    [Fact]
    public void Headings_AreInTheSerif_AndBodyTextStaysInLato()
    {
        var page = FirstPage(Report(), out var document);
        using (document)
        {
            LettersOf(page, "Test University").Should().OnlyContain(letter => InFont(letter, "SourceSerif4"));
            LettersOf(page, "Matched documents").Should().OnlyContain(letter => InFont(letter, "SourceSerif4"));
            LettersOf(page, "Demo Student").Should().OnlyContain(letter => InFont(letter, "Lato"));
        }
    }

    [Fact]
    public void TheHeader_CarriesTheInstitutionInIndigo_AndTheGoldLabelAboveTheTitle()
    {
        var page = FirstPage(Report(), out var document);
        using (document)
        {
            LettersOf(page, "Test University").Should().OnlyContain(letter => Hex(letter) == "#2E2A6B");
            LettersOf(page, "ORIGINALITY REPORT").Should().OnlyContain(letter => Hex(letter) == "#8A6A10");

            var label = LettersOf(page, "ORIGINALITY REPORT")[0];
            var title = LettersOf(page, "Clean power")[0];
            title.Location.Y.Should().BeLessThan(label.Location.Y, "PDF pages count upward, and the label sits above the title");
        }
    }

    [Theory]
    [InlineData("19.99", "#136C3A")]
    [InlineData("20", "#8A5A0B")]
    [InlineData("41", "#8A5A0B")]
    [InlineData("50", "#8A5A0B")]
    [InlineData("50.01", "#B42318")]
    public void TheScore_IsColouredByTheSameBandsAsTheWebPages(string percent, string colour)
    {
        var value = decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture);

        var page = FirstPage(Report(value), out var document);
        using (document)
        {
            LettersOf(page, value.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + "%")
                .Should().OnlyContain(letter => Hex(letter) == colour);
        }
    }

    [Fact]
    public void AByteForByteCopy_ShowsADash_NotZeroPassages()
    {
        var pdf = new QuestPdfReportGenerator().GenerateSubmissionReport(WithMatch(passages: 0));

        var page = FirstPage(pdf, out var document);
        using (document)
        {
            var row = string.Concat(page.Letters.Where(letter => !string.IsNullOrWhiteSpace(letter.Value)).Select(letter => letter.Value));
            row.Should().Contain("report.pdf-100.00%");
        }
    }

    [Fact]
    public void ABanglaTitle_FallsBackToTheBanglaFont_InsteadOfLeavingAGap()
    {
        var latin = FirstPage(Report(), out var latinDocument);
        var bangla = FirstPage(Report(title: BanglaTitle), out var banglaDocument);

        using (latinDocument)
        using (banglaDocument)
        {
            latin.Letters.Should().NotContain(letter => InFont(letter, "NotoSansBengali"));
            bangla.Letters.Should().Contain(letter => InFont(letter, "NotoSansBengali"));
        }
    }
}
