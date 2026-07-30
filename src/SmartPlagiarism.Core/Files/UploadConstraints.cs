namespace SmartPlagiarism.Core.Files;

/// <summary>Hard limits applied to every upload.</summary>
public static class UploadConstraints
{
    /// <summary>25 MB, as required by CLAUDE.md.</summary>
    public const long MaxFileSizeBytes = 25L * 1024 * 1024;

    /// <summary>
    /// Cap on files per submission. "1..N" with no ceiling is a denial-of-service
    /// vector, and a report plus its slides is realistically two or three files.
    /// </summary>
    public const int MaxFilesPerSubmission = 5;

    /// <summary>Matches the nvarchar(260) OriginalFileName column.</summary>
    public const int MaxFileNameLength = 260;

    /// <summary>
    /// Ceiling for the whole multipart request. Kestrel's default is 30 MB, which
    /// a legitimate multi-file submission would exceed.
    /// </summary>
    public const long MaxRequestBytes = (MaxFileSizeBytes * MaxFilesPerSubmission) + (1024 * 1024);
}
