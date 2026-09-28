using System.Globalization;

namespace SmartPlagiarism.Infrastructure.Services;

/// <summary>
/// Who a matched file belongs to: another student's submission, or a document
/// from the archive. Projected straight from the query, and the one place the
/// labels are worded, so the review pages, the match viewer and the reports can
/// never describe a source differently.
/// </summary>
internal sealed record MatchSource(
    int? SubmissionId,
    string? SubmissionTitle,
    int? StudentProfileId,
    string? StudentName,
    string? StudentNumber,
    string? CourseCode,
    int? ArchiveYear,
    string? ArchiveTitle,
    string? ArchiveAuthor)
{
    public bool IsArchive => ArchiveYear is not null;

    public string Title => (IsArchive ? ArchiveTitle : SubmissionTitle) ?? string.Empty;

    /// <summary>The student, or the archive author and year.</summary>
    public string Owner => IsArchive
        ? string.Create(CultureInfo.InvariantCulture, $"{ArchiveAuthor ?? "Unknown author"} ({ArchiveYear} archive)")
        : StudentName ?? string.Empty;

    /// <summary>Everything a teacher needs to find the source.</summary>
    public string StaffLabel => IsArchive
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"Archive {ArchiveYear}: {ArchiveTitle}{(ArchiveAuthor is null ? string.Empty : $" - {ArchiveAuthor}")}")
        : $"{SubmissionTitle} - {StudentName} ({StudentNumber})";

    /// <summary>
    /// What a student may be told: never another person's name or title. Their
    /// own earlier submission is named, since it is their own work.
    /// </summary>
    public string StudentLabel(int ownStudentProfileId) => IsArchive
        ? string.Create(CultureInfo.InvariantCulture, $"University archive ({ArchiveYear})")
        : StudentProfileId == ownStudentProfileId
            ? $"Your own submission \"{SubmissionTitle}\""
            : $"Another student's submission ({CourseCode})";
}
