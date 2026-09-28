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
            var none = new TagBuilder("span");
            none.AddCssClass("text-body-secondary");
            none.InnerHtml.Append("-");
            return none;
        }

        var badge = new TagBuilder("span");
        badge.AddCssClass($"badge {SimilarityBadgeClass(value)}");
        badge.InnerHtml.Append(string.Create(CultureInfo.InvariantCulture, $"{value:0.00}%"));
        return badge;
    }

    public static string SimilarityBadgeClass(decimal percent) => SimilarityBands.Classify(percent) switch
    {
        SimilarityBand.Low => "text-bg-success",
        SimilarityBand.Moderate => "text-bg-warning",
        _ => "text-bg-danger",
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
