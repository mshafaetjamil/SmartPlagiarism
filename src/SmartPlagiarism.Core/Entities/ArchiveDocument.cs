namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Work from before the system existed - a 2010 thesis, a 2015 report - kept
/// so that new submissions are compared against it too.
///
/// Its file is an ordinary <see cref="UploadedFile"/>, so extraction,
/// fingerprints and matching treat it exactly like a submission's file; what
/// differs is only who it belongs to. The student who wrote it has no account,
/// so the author is kept as text.
/// </summary>
public class ArchiveDocument
{
    public const int MaxTitleLength = 250;
    public const int MaxAuthorLength = 200;

    /// <summary>The first year accepted: the university's archive starts in 2010.</summary>
    public const int EarliestYear = 2010;

    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Author { get; set; }

    public int Year { get; set; }

    public int? DepartmentId { get; set; }

    public Department? Department { get; set; }

    /// <summary>The course it was written for, as the old records name it; free text.</summary>
    public string? CourseCode { get; set; }

    public int? ArchiveImportId { get; set; }

    public ArchiveImport? ArchiveImport { get; set; }

    public DateTime ImportedAtUtc { get; set; }

    public UploadedFile File { get; set; } = null!;
}
