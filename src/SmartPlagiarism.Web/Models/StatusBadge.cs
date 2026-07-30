using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Web.Models;

/// <summary>Maps the two status enums onto Bootstrap badge classes.</summary>
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

    /// <summary>"PendingReview" reads badly in a badge.</summary>
    public static string Label(SubmissionStatus status) => status switch
    {
        SubmissionStatus.PendingReview => "Pending review",
        _ => status.ToString(),
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
