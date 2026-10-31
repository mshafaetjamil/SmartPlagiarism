using System.Globalization;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Web.Models;

/// <summary>Maps statuses and similarity scores onto Bootstrap badge classes.</summary>
public static class StatusBadge
{
    public static string For(AnalysisStatus status) => status switch
    {
        AnalysisStatus.Queued => "text-bg-secondary",
        AnalysisStatus.Processing => "text-bg-info",
        AnalysisStatus.Completed => "text-bg-success",
        AnalysisStatus.Failed => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    public static string For(SubmissionStatus status) => status switch
    {
        SubmissionStatus.PendingReview => "text-bg-warning",
        SubmissionStatus.Approved => "text-bg-success",
        SubmissionStatus.Rejected => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    public static string For(ArchiveImportStatus status) => status switch
    {
        ArchiveImportStatus.Queued => "text-bg-secondary",
        ArchiveImportStatus.Processing => "text-bg-info",
        ArchiveImportStatus.Completed => "text-bg-success",
        ArchiveImportStatus.Failed => "text-bg-danger",
        _ => "text-bg-secondary",
    };

    /// <summary>How a file's text was read, in words; null when it has not been read.</summary>
    public static string Label(ExtractionMethod? method) => method switch
    {
        ExtractionMethod.Native => "Embedded text",
        ExtractionMethod.Ocr => "OCR",
        ExtractionMethod.Mixed => "Embedded text and OCR",
        _ => "Not yet read",
    };

    /// <summary>"PendingReview" reads badly in a badge.</summary>
    public static string Label(SubmissionStatus status) => status switch
    {
        SubmissionStatus.PendingReview => "Pending review",
        _ => status.ToString(),
    };

    /// <summary>
    /// A similarity score as a badge coloured by <see cref="SimilarityBands"/>:
    /// green below 20%, yellow to 50%, red above. A dash when there is no score.
    /// </summary>
    public static IHtmlContent Similarity(decimal? percent)
    {
        if (percent is not { } value)
        {
            return NoScore();
        }

        return Pill(SimilarityBadgeClass(value), FormatPercent(value));
    }

    /// <summary>
    /// A similarity score as a bar coloured by <see cref="SimilarityBands"/>, with
    /// the number beside it. The number carries the value; the bar is decoration,
    /// hidden from screen readers. A dash when there is no score.
    /// </summary>
    public static IHtmlContent SimilarityBar(decimal? percent)
    {
        if (percent is not { } value)
        {
            return NoScore();
        }

        var fill = new TagBuilder("div");
        fill.AddCssClass($"stat-bar-fill {SimilarityBarClass(value)}");
        fill.Attributes["data-bar-width"] = string.Create(CultureInfo.InvariantCulture, $"{Math.Clamp(value, 0m, 100m):0.##}");

        var track = new TagBuilder("div");
        track.AddCssClass("stat-bar");
        track.Attributes["aria-hidden"] = "true";
        track.InnerHtml.AppendHtml(fill);

        var number = new TagBuilder("span");
        number.AddCssClass($"similarity-bar-value {SimilarityTextClass(value)}");
        number.InnerHtml.Append(FormatPercent(value));

        var bar = new TagBuilder("div");
        bar.AddCssClass("similarity-bar");
        bar.InnerHtml.AppendHtml(track);
        bar.InnerHtml.AppendHtml(number);
        return bar;
    }

    private static TagBuilder NoScore()
    {
        var none = new TagBuilder("span");
        none.AddCssClass("text-body-secondary");
        none.InnerHtml.Append("-");
        return none;
    }

    public static string FormatPercent(decimal percent) =>
        string.Create(CultureInfo.InvariantCulture, $"{percent:0.00}%");

    /// <summary>
    /// A submission's state in a list. Once an analysis has completed only the
    /// teacher's decision is left to report, so its pill goes.
    /// </summary>
    public static IHtmlContent Status(AnalysisStatus analysis, SubmissionStatus review)
    {
        var pills = new TagBuilder("div");
        pills.AddCssClass("status-pills");

        if (analysis != AnalysisStatus.Completed)
        {
            pills.InnerHtml.AppendHtml(Pill(For(analysis), analysis.ToString()));
        }

        pills.InnerHtml.AppendHtml(Pill(For(review), Label(review)));
        return pills;
    }

    private static TagBuilder Pill(string cssClass, string text)
    {
        var pill = new TagBuilder("span");
        pill.AddCssClass($"badge {cssClass}");
        pill.InnerHtml.Append(text);
        return pill;
    }

    /// <summary>A score's band, in words.</summary>
    public static string SimilarityBandLabel(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => "Low similarity",
        SimilarityBand.Moderate => "Moderate similarity",
        _ => "High similarity",
    };

    /// <summary>A bar's width as a percentage of the largest value in its chart; site.js applies it.</summary>
    public static string BarWidth(int value, int max) =>
        (max == 0 ? 0 : value * 100.0 / max).ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>A date for a line of detail in a list.</summary>
    public static string ShortDate(DateTime utc) => utc.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>The full time, for a tooltip on <see cref="ShortDate"/>.</summary>
    public static string FullTime(DateTime utc) => utc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    public static string SimilarityBadgeClass(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => "text-bg-success",
        SimilarityBand.Moderate => "text-bg-warning",
        _ => "text-bg-danger",
    };

    /// <summary>The fill of a similarity bar; theme.css gives these the bar shades.</summary>
    public static string SimilarityBarClass(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => "bg-success",
        SimilarityBand.Moderate => "bg-warning",
        _ => "bg-danger",
    };

    /// <summary>For a large score figure, where a filled badge would be too heavy.</summary>
    public static string SimilarityTextClass(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => "text-success",
        SimilarityBand.Moderate => "text-warning-emphasis",
        _ => "text-danger",
    };

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unit = 0;

        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.##} {units[unit]}";
    }
}
