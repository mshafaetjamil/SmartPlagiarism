using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Archive;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Core.Files;
using SmartPlagiarism.Infrastructure.Analysis;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Archive;

/// <inheritdoc cref="IArchiveImporter"/>
/// <remarks>
/// <para>For each file in the ZIP, in order:</para>
/// <list type="number">
///   <item><description>
///     Metadata: the manifest row if there is one; otherwise the title comes from
///     the file name and the year from a year folder in its path
///     (<c>2015/thesis.pdf</c>), falling back to the import's default year. A file
///     that ends up with no year is skipped rather than filed under a guess.
///   </description></item>
///   <item><description>
///     The same checks as a student upload - extension, magic bytes, 25 MB - and a
///     skip for bytes already in the archive, so re-importing a ZIP is harmless.
///   </description></item>
///   <item><description>
///     Stored, extracted (with OCR and Bijoy conversion as needed) and
///     fingerprinted by the same <see cref="DocumentIndexer"/> submissions use.
///   </description></item>
/// </list>
/// <para>The ZIP is read entry by entry and never unpacked to disk, so entry
/// names are never used as paths: a hostile archive cannot write anywhere.</para>
/// </remarks>
public class ArchiveImporter : IArchiveImporter
{
    /// <summary>Progress is written this often, so the admin page can show it moving.</summary>
    private const int ProgressInterval = 10;

    private readonly AppDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IFileUploadValidator _validator;
    private readonly DocumentIndexer _indexer;
    private readonly ILogger<ArchiveImporter> _logger;

    public ArchiveImporter(
        AppDbContext context,
        IFileStorageService storage,
        IFileUploadValidator validator,
        DocumentIndexer indexer,
        ILogger<ArchiveImporter> logger)
    {
        _context = context;
        _storage = storage;
        _validator = validator;
        _indexer = indexer;
        _logger = logger;
    }

    public async Task<IReadOnlyList<int>> ResumePendingAsync(CancellationToken cancellationToken = default) =>
        await _context.ArchiveImports
            .AsNoTracking()
            .Where(import => import.Status == ArchiveImportStatus.Queued || import.Status == ArchiveImportStatus.Processing)
            .OrderBy(import => import.CreatedAtUtc)
            .Select(import => import.Id)
            .ToListAsync(cancellationToken);

    public async Task ImportAsync(int importId, CancellationToken cancellationToken = default)
    {
        var import = await _context.ArchiveImports.FirstOrDefaultAsync(candidate => candidate.Id == importId, cancellationToken);

        if (import is null || import.Status is not (ArchiveImportStatus.Queued or ArchiveImportStatus.Processing))
        {
            return;
        }

        var resumed = import.Status == ArchiveImportStatus.Processing;
        var progress = new Progress(import.Id);

        if (resumed)
        {
            // Everything is gone through again; files that made it in before the
            // restart are recognised by their hash and skipped.
            progress.Note("Resumed after a restart: files imported before it are listed as already in the archive.");
        }

        import.Status = ArchiveImportStatus.Processing;
        await _context.SaveChangesAsync(cancellationToken);

        var defaults = new Defaults(import.DefaultYear, import.DefaultDepartmentId, import.PackagePath);
        _context.ChangeTracker.Clear();

        try
        {
            await ImportPackageAsync(defaults, progress, cancellationToken);
            await FinishAsync(progress, ArchiveImportStatus.Completed, defaults.PackagePath, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: left Processing, resumed at the next start.
            throw;
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Archive import {ImportId} is not a readable ZIP.", importId);
            progress.Note("The file is not a readable ZIP archive, so nothing was imported.");
            await FinishAsync(progress, ArchiveImportStatus.Failed, defaults.PackagePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Archive import {ImportId} failed.", importId);
            progress.Note("The import stopped because of an unexpected error; it has been logged. Upload the ZIP again to finish it - files already imported are skipped.");
            await FinishAsync(progress, ArchiveImportStatus.Failed, defaults.PackagePath, cancellationToken);
        }
    }

    private async Task ImportPackageAsync(Defaults defaults, Progress progress, CancellationToken cancellationToken)
    {
        if (defaults.PackagePath is null)
        {
            throw new InvalidDataException("The import has no package.");
        }

        await using var package = await _storage.OpenReadAsync(defaults.PackagePath, cancellationToken);
        using var zip = new ZipArchive(package, ZipArchiveMode.Read);

        var (manifest, manifestFolder) = await ReadManifestAsync(zip, cancellationToken);

        foreach (var problem in manifest.Problems)
        {
            progress.Note(problem);
        }

        var entries = zip.Entries.Where(IsDocumentCandidate).OrderBy(entry => entry.FullName, StringComparer.Ordinal).ToList();

        if (entries.Count > ArchiveLimits.MaxFilesPerPackage)
        {
            progress.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"The ZIP holds {entries.Count} files; at most {ArchiveLimits.MaxFilesPerPackage} are imported at once. Split it, by year for example."));
            return;
        }

        progress.Total = entries.Count;
        await SaveProgressAsync(progress, cancellationToken);

        var departments = await _context.Departments
            .AsNoTracking()
            .ToDictionaryAsync(department => department.Code, department => department.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = RelativeTo(manifestFolder, entry.FullName);
            var row = manifest.Find(relativePath);

            try
            {
                await ImportEntryAsync(entry, row, defaults, departments, progress, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad file never sinks the rest of the archive.
                _logger.LogError(ex, "Archive import {ImportId}: {Entry} failed.", progress.ImportId, entry.FullName);
                _context.ChangeTracker.Clear();
                progress.Skip($"{entry.FullName}: could not be imported because of an unexpected error; it has been logged.");
            }

            if (progress.Processed % ProgressInterval == 0)
            {
                await SaveProgressAsync(progress, cancellationToken);
            }
        }
    }

    private async Task ImportEntryAsync(
        ZipArchiveEntry entry,
        ArchiveManifest.Row? row,
        Defaults defaults,
        IReadOnlyDictionary<string, int> departments,
        Progress progress,
        CancellationToken cancellationToken)
    {
        var year = row?.Year ?? YearFromFolders(entry.FullName) ?? defaults.Year;
        var latestYear = DateTime.UtcNow.Year;

        if (year is not { } documentYear || documentYear < ArchiveDocument.EarliestYear || documentYear > latestYear)
        {
            progress.Skip(year is null
                ? $"{entry.FullName}: no year - add it to manifest.csv, put the file in a year folder, or choose a default year."
                : string.Create(CultureInfo.InvariantCulture, $"{entry.FullName}: {year} is outside {ArchiveDocument.EarliestYear}-{latestYear}."));
            return;
        }

        if (entry.Length > UploadConstraints.MaxFileSizeBytes)
        {
            progress.Skip($"{entry.FullName}: larger than the 25 MB limit.");
            return;
        }

        using var content = await ReadEntryAsync(entry, cancellationToken);

        if (content is null)
        {
            progress.Skip($"{entry.FullName}: larger than the 25 MB limit once unpacked.");
            return;
        }

        var validation = _validator.Validate(entry.Name, content.Length, content);

        if (!validation.IsValid)
        {
            progress.Skip($"{entry.FullName}: {validation.ErrorMessage}");
            return;
        }

        content.Position = 0;
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken));

        if (await _context.UploadedFiles.AnyAsync(file => file.Sha256Hash == hash && file.ArchiveDocumentId != null, cancellationToken))
        {
            progress.Skip($"{entry.FullName}: already in the archive.");
            return;
        }

        content.Position = 0;
        var stored = await _storage.StoreAsync(new FileUpload(entry.Name, content.Length, content), cancellationToken);

        int? departmentId = defaults.DepartmentId;

        if (row?.DepartmentCode is { } code)
        {
            if (departments.TryGetValue(code, out var id))
            {
                departmentId = id;
            }
            else
            {
                progress.Note($"{entry.FullName}: department \"{code}\" is not set up, so none was recorded.");
            }
        }

        var document = new ArchiveDocument
        {
            Title = Truncate(row?.Title ?? TitleFromFileName(entry.Name), ArchiveDocument.MaxTitleLength)!,
            Author = Truncate(row?.Author, ArchiveDocument.MaxAuthorLength),
            Year = documentYear,
            DepartmentId = departmentId,
            CourseCode = Truncate(row?.CourseCode, Course.MaxCodeLength),
            ArchiveImportId = progress.ImportId,
            ImportedAtUtc = DateTime.UtcNow,
            File = new UploadedFile
            {
                OriginalFileName = validation.SafeFileName!,
                ContentType = stored.ContentType,
                SizeBytes = stored.SizeBytes,
                Sha256Hash = stored.Sha256Hash,
                StoragePath = stored.StoragePath,
            },
        };

        _context.ArchiveDocuments.Add(document);
        await _context.SaveChangesAsync(cancellationToken);

        var indexed = (await _indexer.IndexAsync([document.File.Id], cancellationToken)).Single();

        // A long import touches thousands of rows; tracking them all would slow
        // every later save down.
        _context.ChangeTracker.Clear();

        if (indexed.Document is null)
        {
            var reason = indexed.Warnings.FirstOrDefault();
            progress.Note(reason is null
                ? $"{entry.FullName}: imported, but no readable text was found, so it cannot be matched."
                : $"{entry.FullName}: imported, but no readable text was found, so it cannot be matched. {reason}");
        }

        progress.Imported++;
    }

    /// <summary>
    /// The shallowest manifest.csv, and the folder it sits in: a ZIP made by
    /// compressing a folder puts everything under that folder's name.
    /// </summary>
    private static async Task<(ArchiveManifest Manifest, string Folder)> ReadManifestAsync(ZipArchive zip, CancellationToken cancellationToken)
    {
        var entry = zip.Entries
            .Where(candidate => candidate.Name.Equals(ArchiveLimits.ManifestFileName, StringComparison.OrdinalIgnoreCase)
                && !IsSystemPath(candidate.FullName))
            .OrderBy(candidate => candidate.FullName.Count(character => character == '/'))
            .FirstOrDefault();

        if (entry is null)
        {
            return (ArchiveManifest.Empty, string.Empty);
        }

        await using var stream = await entry.OpenAsync(cancellationToken);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var folder = entry.FullName[..^entry.Name.Length];
        return (ArchiveManifest.Parse(reader), folder);
    }

    /// <summary>
    /// The entry's bytes, read with a hard cap: the size a ZIP declares can lie,
    /// and a compression bomb must not be allowed to fill memory.
    /// </summary>
    private static async Task<MemoryStream?> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken cancellationToken)
    {
        await using var source = await entry.OpenAsync(cancellationToken);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > UploadConstraints.MaxFileSizeBytes)
            {
                await buffer.DisposeAsync();
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        buffer.Position = 0;
        return buffer;
    }

    /// <summary>Files worth trying: not folders, the manifest, or the clutter operating systems add to ZIPs.</summary>
    private static bool IsDocumentCandidate(ZipArchiveEntry entry) =>
        entry.Name.Length > 0
        && !entry.Name.Equals(ArchiveLimits.ManifestFileName, StringComparison.OrdinalIgnoreCase)
        && !IsSystemPath(entry.FullName)
        && !entry.Name.StartsWith('.')
        && !entry.Name.StartsWith("~$", StringComparison.Ordinal);

    private static bool IsSystemPath(string path) =>
        path.StartsWith("__MACOSX/", StringComparison.Ordinal) || path.Contains("/.", StringComparison.Ordinal);

    /// <summary>The first folder in the path that is a year the archive covers, as in 2015/thesis.pdf.</summary>
    internal static int? YearFromFolders(string entryPath)
    {
        var folders = ArchiveManifest.NormalizePath(entryPath).Split('/')[..^1];

        foreach (var folder in folders)
        {
            if (folder.Length == 4
                && int.TryParse(folder, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year >= ArchiveDocument.EarliestYear
                && year <= DateTime.UtcNow.Year)
            {
                return year;
            }
        }

        return null;
    }

    private static string RelativeTo(string folder, string path) =>
        folder.Length > 0 && path.StartsWith(folder, StringComparison.Ordinal) ? path[folder.Length..] : path;

    private static string TitleFromFileName(string fileName)
    {
        var title = Path.GetFileNameWithoutExtension(fileName).Replace('_', ' ').Trim();
        return title.Length == 0 ? fileName : title;
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    private async Task SaveProgressAsync(Progress progress, CancellationToken cancellationToken)
    {
        var import = await _context.ArchiveImports.FirstAsync(candidate => candidate.Id == progress.ImportId, cancellationToken);

        progress.CopyTo(import);
        await _context.SaveChangesAsync(cancellationToken);
        _context.ChangeTracker.Clear();
    }

    private async Task FinishAsync(Progress progress, ArchiveImportStatus status, string? packagePath, CancellationToken cancellationToken)
    {
        _context.ChangeTracker.Clear();

        var import = await _context.ArchiveImports.FirstAsync(candidate => candidate.Id == progress.ImportId, cancellationToken);

        progress.CopyTo(import);
        import.Status = status;
        import.CompletedAtUtc = DateTime.UtcNow;
        import.PackagePath = null;
        await _context.SaveChangesAsync(cancellationToken);

        // The documents are stored individually now; the ZIP has done its job.
        if (packagePath is not null)
        {
            await _storage.DeleteAsync(packagePath, cancellationToken);
        }
    }

    private sealed record Defaults(int? Year, int? DepartmentId, string? PackagePath);

    /// <summary>Counters and notes kept in memory and written to the import row now and then.</summary>
    private sealed class Progress(int importId)
    {
        private readonly List<string> _messages = [];

        public int ImportId { get; } = importId;

        public int Total { get; set; }

        public int Imported { get; set; }

        public int Skipped { get; private set; }

        public int Processed => Imported + Skipped;

        public void Skip(string reason)
        {
            Skipped++;
            Note(reason);
        }

        public void Note(string message) => _messages.Add(message);

        public void CopyTo(ArchiveImport import)
        {
            import.TotalFiles = Total;
            import.ImportedFiles = Imported;
            import.SkippedFiles = Skipped;
            import.Messages = [];

            foreach (var message in _messages)
            {
                import.Note(message);
            }
        }
    }
}
