using System.Globalization;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Reports;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Infrastructure.Reports;

/// <summary>
/// The originality report as a PDF, laid out with QuestPDF.
///
/// Text is set in Lato, falling back to the embedded Noto Sans Bengali for any
/// character Lato lacks, so a Bangla title, name or quotation renders the same on
/// every server. The similarity colour comes from <see cref="SimilarityBands"/>,
/// exactly as on the web pages.
/// </summary>
public sealed class QuestPdfReportGenerator : IReportGenerator
{
    private const string LatinFont = "Lato";
    private const string BanglaFont = "Noto Sans Bengali";

    private static readonly Lazy<bool> s_configured = new(Configure, LazyThreadSafetyMode.ExecutionAndPublication);

    public byte[] GenerateSubmissionReport(SubmissionReportData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _ = s_configured.Value;

        return Document
            .Create(document => document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(style => style.FontFamily(LatinFont, BanglaFont).FontSize(10).FontColor(Colors.Grey.Darken4));

                page.Header().Element(container => ComposeHeader(container, data));
                page.Content().PaddingVertical(12).Column(column =>
                {
                    column.Spacing(16);
                    column.Item().Element(container => ComposeParticulars(container, data));
                    column.Item().Element(container => ComposeScore(container, data));
                    column.Item().Element(container => ComposeFiles(container, data));
                    column.Item().Element(container => ComposeMatches(container, data));
                    column.Item().Element(container => ComposeExcerpts(container, data));
                    column.Item().Element(container => ComposeDecision(container, data));
                });
                page.Footer().Element(container => ComposeFooter(container, data));
            }))
            .WithMetadata(new DocumentMetadata
            {
                Title = $"Originality report - {data.Title}",
                Author = data.InstitutionName,
                CreationDate = data.GeneratedAtUtc,
            })
            .GeneratePdf();
    }

    /// <summary>
    /// QuestPDF's settings are process-wide, so they are applied once, before the
    /// first document.
    /// </summary>
    private static bool Configure()
    {
        // Free for education and for organisations under USD 1M revenue.
        QuestPDF.Settings.License = LicenseType.Community;

        // The embedded fonts only, never whatever a server happens to have
        // installed: the same report must look the same everywhere.
        QuestPDF.Settings.UseSystemFonts = false;

        // A character no font covers - an emoji in a title - renders as a gap
        // rather than failing the whole report.
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        var assembly = typeof(QuestPdfReportGenerator).Assembly;

        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".ttf", StringComparison.Ordinal)))
        {
            using var font = assembly.GetManifestResourceStream(resource)!;
            FontManager.RegisterFontFromStream(font);
        }

        return true;
    }

    private static void ComposeHeader(IContainer container, SubmissionReportData data)
    {
        container.BorderBottom(1.5f).BorderColor(Colors.Blue.Darken3).PaddingBottom(8).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(data.InstitutionName).FontSize(15).Bold().FontColor(Colors.Blue.Darken3);
                column.Item().Text(data.DepartmentName).FontColor(Colors.Grey.Darken1);
                column.Item().PaddingTop(4).Text("Originality Report").FontSize(12).SemiBold();
            });

            row.ConstantItem(170).AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text($"Submission #{data.SubmissionId}").SemiBold();
                column.Item().AlignRight().Text($"Generated {Format(data.GeneratedAtUtc)}").FontSize(8).FontColor(Colors.Grey.Darken1);
                column.Item().AlignRight().Text(data.ForStaff ? "Staff copy" : "Student copy").FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void ComposeParticulars(IContainer container, SubmissionReportData data)
    {
        container.Row(row =>
        {
            row.Spacing(12);

            row.RelativeItem().Element(Box).Column(column =>
            {
                column.Item().Element(Caption).Text("Student");
                column.Item().Text(data.StudentName).SemiBold();
                column.Item().Text(data.StudentNumber).FontColor(Colors.Grey.Darken1);
            });

            row.RelativeItem(2).Element(Box).Column(column =>
            {
                column.Item().Element(Caption).Text("Submission");
                column.Item().Text(data.Title).SemiBold();
                column.Item().Text($"{data.CourseCode} - {data.CourseTitle}");
                column.Item().Text($"{data.SemesterName} · {data.Type} · submitted {Format(data.SubmittedAtUtc)}")
                    .FontColor(Colors.Grey.Darken1);
            });
        });
    }

    private static void ComposeScore(IContainer container, SubmissionReportData data)
    {
        if (data.OverallSimilarityPercent is not { } score)
        {
            container.Element(Box).Column(column =>
            {
                column.Item().Element(Caption).Text("Overall similarity");
                column.Item().Text(data.AnalysisStatus == AnalysisStatus.Failed
                    ? $"Analysis failed: {data.AnalysisError}"
                    : "Analysis has not finished yet.");
            });
            return;
        }

        var (ink, wash, label) = BandStyle(score);

        container.Background(wash).Border(1).BorderColor(ink).Padding(12).Row(row =>
        {
            row.ConstantItem(130).Text($"{score:0.00}%").FontSize(28).Bold().FontColor(ink);

            row.RelativeItem().AlignMiddle().Column(column =>
            {
                column.Item().Text($"Overall similarity - {label}").SemiBold().FontColor(ink);
                column.Item().Text("How much of the submission's text was also found in other submissions.")
                    .FontSize(9);

                if (data.CommonTextPercent > 0)
                {
                    column.Item().Text(
                            $"A further {data.CommonTextPercent:0.00}% is text most of the course shares - a cover page, "
                            + "a declaration - and is not counted.")
                        .FontSize(8);
                }

                if (data.AnalyzedAtUtc is { } analysedAt)
                {
                    column.Item().Text($"Analysed {Format(analysedAt)} by engine {data.EngineVersion}.")
                        .FontSize(8).FontColor(Colors.Grey.Darken1);
                }
            });
        });
    }

    private static void ComposeFiles(IContainer container, SubmissionReportData data)
    {
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("Files");
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(4);
                    columns.RelativeColumn(2);
                    columns.ConstantColumn(60);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("File");
                    header.Cell().Element(HeaderCell).Text("Read by");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Words");
                });

                foreach (var file in data.Files)
                {
                    table.Cell().Element(BodyCell).Text(file.FileName);
                    table.Cell().Element(BodyCell).Text(file.ReadBy switch
                    {
                        ExtractionMethod.Native => "Embedded text",
                        ExtractionMethod.Ocr => "OCR",
                        ExtractionMethod.Mixed => "Embedded text and OCR",
                        _ => "Not read",
                    });
                    table.Cell().Element(BodyCell).AlignRight().Text(file.WordCount?.ToString("N0", CultureInfo.InvariantCulture) ?? "-");

                    foreach (var warning in file.Warnings)
                    {
                        table.Cell().ColumnSpan(3).Background(Colors.Amber.Lighten5).PaddingHorizontal(6).PaddingVertical(3)
                            .Text(warning).FontSize(8).Italic().FontColor(Colors.Amber.Darken4);
                    }
                }
            });
        });
    }

    private static void ComposeMatches(IContainer container, SubmissionReportData data)
    {
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("Matched documents");

            if (data.Matches.Count == 0)
            {
                column.Item().Text(data.OverallSimilarityPercent is null
                        ? "No analysis result yet."
                        : "No other submission shares text with this one.")
                    .FontColor(Colors.Grey.Darken1);
                return;
            }

            column.Item().PaddingBottom(4).Text(
                    "Share: how much of this submission lies in passages found in that source. "
                    + "The overall figure counts each word once, however many sources it was found in.")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(5);
                    columns.RelativeColumn(3);
                    columns.ConstantColumn(55);
                    columns.ConstantColumn(60);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("Matched source");
                    header.Cell().Element(HeaderCell).Text("In file");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Passages");
                    header.Cell().Element(HeaderCell).AlignRight().Text("Share");
                });

                foreach (var match in data.Matches)
                {
                    table.Cell().Element(BodyCell).Text(match.Source);
                    table.Cell().Element(BodyCell).Text(match.OwnFileName);
                    table.Cell().Element(BodyCell).AlignRight().Text(match.PassageCount.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).AlignRight().Text($"{match.SimilarityPercent:0.00}%")
                        .SemiBold().FontColor(BandStyle(match.SimilarityPercent).Ink);
                }
            });
        });
    }

    private static void ComposeExcerpts(IContainer container, SubmissionReportData data)
    {
        if (data.TopExcerpts.Count == 0)
        {
            return;
        }

        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(SectionTitle).Text("Top matched passages");

            if (!data.ForStaff)
            {
                column.Item().Text("Only your own text is quoted; the matched submissions belong to other students.")
                    .FontSize(8).FontColor(Colors.Grey.Darken1);
            }

            for (var index = 0; index < data.TopExcerpts.Count; index++)
            {
                var excerpt = data.TopExcerpts[index];
                var number = index + 1;

                column.Item().Column(passage =>
                {
                    passage.Item().Text($"Passage {number} - {excerpt.Source} ({excerpt.SimilarityPercent:0.00}%)")
                        .SemiBold().FontSize(9);

                    passage.Item().PaddingTop(3).Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem().Element(container => Quotation(container, "Submitted", excerpt.SubmittedExcerpt));

                        if (excerpt.SourceExcerpt is { } source)
                        {
                            row.RelativeItem().Element(container => Quotation(container, "Matched source", source));
                        }
                    });
                });
            }
        });
    }

    private static void ComposeDecision(IContainer container, SubmissionReportData data)
    {
        container.Element(Box).Column(column =>
        {
            column.Item().Element(Caption).Text("Teacher decision");

            var (text, color) = data.ReviewStatus switch
            {
                SubmissionStatus.Approved => ("Approved", Colors.Green.Darken2),
                SubmissionStatus.Rejected => ("Rejected", Colors.Red.Darken2),
                _ => ("Pending review", Colors.Grey.Darken1),
            };

            column.Item().Text(text).SemiBold().FontColor(color);

            if (!string.IsNullOrWhiteSpace(data.TeacherComment))
            {
                column.Item().PaddingTop(2).Text(data.TeacherComment);
            }
        });
    }

    private static void ComposeFooter(IContainer container, SubmissionReportData data)
    {
        container.BorderTop(0.5f).BorderColor(Colors.Grey.Lighten1).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Text($"{data.InstitutionName} · Originality report for submission #{data.SubmissionId}")
                .FontSize(8).FontColor(Colors.Grey.Darken1);

            row.ConstantItem(80).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Darken1));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static void Quotation(IContainer container, string caption, string quote)
    {
        container.Background(Colors.Grey.Lighten4).BorderLeft(2).BorderColor(Colors.Amber.Darken1).Padding(6).Column(column =>
        {
            column.Item().Text(caption).FontSize(7).FontColor(Colors.Grey.Darken1);
            column.Item().Text(string.IsNullOrEmpty(quote) ? "(text unavailable)" : quote).FontSize(9);
        });
    }

    /// <summary>Colours for a score's band: text, background, and a word for it.</summary>
    private static (Color Ink, Color Wash, string Label) BandStyle(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => (Colors.Green.Darken2, Colors.Green.Lighten5, "low"),
        SimilarityBand.Moderate => (Colors.Amber.Darken3, Colors.Amber.Lighten5, "moderate"),
        _ => (Colors.Red.Darken2, Colors.Red.Lighten5, "high"),
    };

    private static IContainer Box(IContainer container) =>
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten2).Padding(8);

    private static IContainer Caption(IContainer container) => container.PaddingBottom(2).DefaultTextStyle(style =>
        style.FontSize(7).FontColor(Colors.Grey.Darken1).LetterSpacing(0.05f));

    private static IContainer SectionTitle(IContainer container) => container.PaddingBottom(4).DefaultTextStyle(style =>
        style.FontSize(11).SemiBold().FontColor(Colors.Blue.Darken3));

    private static IContainer HeaderCell(IContainer container) => container
        .BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingVertical(4).PaddingHorizontal(6)
        .DefaultTextStyle(style => style.SemiBold().FontSize(9));

    private static IContainer BodyCell(IContainer container) => container
        .BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).PaddingHorizontal(6);

    private static string Format(DateTime utc) =>
        utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
