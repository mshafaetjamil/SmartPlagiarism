using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// One ZIP of old work uploaded by an admin, and how far its import has got.
/// The import runs in the background; this record is what the admin page shows
/// while it does, and what lets an interrupted import resume after a restart.
/// </summary>
public class ArchiveImport
{
    /// <summary>Messages kept per import; a 10,000-file ZIP full of problems stays readable.</summary>
    public const int MaxMessages = 500;

    public const int MaxPackageFileNameLength = 260;

    public int Id { get; set; }

    public string PackageFileName { get; set; } = string.Empty;

    /// <summary>Where the uploaded ZIP is kept until the import finishes.</summary>
    public string? PackagePath { get; set; }

    /// <summary>Year for files that neither the manifest nor a year folder dates.</summary>
    public int? DefaultYear { get; set; }

    public int? DefaultDepartmentId { get; set; }

    public string CreatedByUserId { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ArchiveImportStatus Status { get; set; } = ArchiveImportStatus.Queued;

    public int TotalFiles { get; set; }

    public int ImportedFiles { get; set; }

    public int SkippedFiles { get; set; }

    /// <summary>Per-file problems and notes, in the order they happened.</summary>
    public List<string> Messages { get; set; } = [];

    public DateTime? CompletedAtUtc { get; set; }

    public int ProcessedFiles => ImportedFiles + SkippedFiles;

    public void Note(string message)
    {
        if (Messages.Count < MaxMessages)
        {
            Messages.Add(message);
        }
        else if (Messages.Count == MaxMessages)
        {
            Messages.Add("Further messages were not kept.");
        }
    }
}
