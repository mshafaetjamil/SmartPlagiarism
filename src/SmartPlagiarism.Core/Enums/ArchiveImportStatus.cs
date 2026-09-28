namespace SmartPlagiarism.Core.Enums;

public enum ArchiveImportStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,

    /// <summary>The package as a whole could not be read - not a ZIP, say. Single bad files only get skipped.</summary>
    Failed = 3,
}
