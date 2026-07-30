namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// A file attached to a submission. The bytes live outside wwwroot, named by their
/// SHA-256 hash; the original filename is kept here for display and download.
/// </summary>
public class UploadedFile
{
    public int Id { get; set; }

    public int SubmissionId { get; set; }

    public Submission Submission { get; set; } = null!;

    /// <summary>Filename as the student uploaded it. Never used to build a storage path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>
    /// Lowercase hex SHA-256 of the file contents. Indexed but deliberately not
    /// unique: two students legitimately submitting the same file is exactly the
    /// duplicate we want to detect, not a constraint violation.
    /// </summary>
    public string Sha256Hash { get; set; } = string.Empty;

    /// <summary>Path relative to the configured storage root.</summary>
    public string StoragePath { get; set; } = string.Empty;

    public ExtractedText? ExtractedText { get; set; }

    public DocumentFingerprint? Fingerprint { get; set; }
}
