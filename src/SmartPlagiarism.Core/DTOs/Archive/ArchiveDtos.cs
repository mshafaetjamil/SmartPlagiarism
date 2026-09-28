using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Archive;

/// <summary>Limits on what an archive import accepts.</summary>
public static class ArchiveLimits
{
    /// <summary>
    /// One upload. Larger archives are best split by year, which also keeps any
    /// one import's problems easy to read.
    /// </summary>
    public const long MaxPackageBytes = 1024L * 1024 * 1024;

    /// <summary>Files per package, so a runaway ZIP cannot occupy the importer for days.</summary>
    public const int MaxFilesPerPackage = 20_000;

    /// <summary>The optional metadata file inside the ZIP.</summary>
    public const string ManifestFileName = "manifest.csv";
}

/// <summary>A ZIP an admin uploaded, with the defaults for files it does not date itself.</summary>
/// <param name="Content">The ZIP; read, not kept open, by the call it is passed to.</param>
public sealed record ArchivePackage(
    string FileName,
    long SizeBytes,
    Stream Content,
    int? DefaultYear,
    int? DefaultDepartmentId);

public sealed record ArchiveQuery(int? Year = null, int? DepartmentId = null, string? Search = null, int Page = 1)
{
    public const int PageSize = 25;
}

/// <param name="TimesMatched">Submissions that matched this document; a matched document cannot be deleted.</param>
public sealed record ArchiveDocumentDto(
    int Id,
    string Title,
    string? Author,
    int Year,
    string? DepartmentCode,
    string? CourseCode,
    string FileName,
    ExtractionMethod? ReadBy,
    int? WordCount,
    int TimesMatched);

public sealed record ArchiveImportDto(
    int Id,
    string PackageFileName,
    DateTime CreatedAtUtc,
    ArchiveImportStatus Status,
    int TotalFiles,
    int ImportedFiles,
    int SkippedFiles,
    IReadOnlyList<string> Messages,
    DateTime? CompletedAtUtc)
{
    public bool IsRunning => Status is ArchiveImportStatus.Queued or ArchiveImportStatus.Processing;
}

/// <param name="DocumentsByYear">Every year from the archive's first to this one, zeros included.</param>
public sealed record ArchiveOverviewDto(
    int TotalDocuments,
    IReadOnlyList<ArchiveYearCountDto> DocumentsByYear,
    IReadOnlyList<ArchiveImportDto> RecentImports);

public sealed record ArchiveYearCountDto(int Year, int Documents);
