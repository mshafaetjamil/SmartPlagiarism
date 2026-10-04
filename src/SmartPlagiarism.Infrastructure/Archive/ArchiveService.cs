using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Auditing;
using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.DTOs.Archive;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Archive;

/// <inheritdoc cref="IArchiveService"/>
public class ArchiveService : IArchiveService
{
    /// <summary>Imports listed on the overview; older ones are in the audit log.</summary>
    private const int RecentImportCount = 10;

    private static readonly byte[] s_zipMagic = "PK\u0003\u0004"u8.ToArray();

    private readonly AppDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IArchiveImportQueue _queue;
    private readonly IAuditService _audit;

    public ArchiveService(AppDbContext context, IFileStorageService storage, IArchiveImportQueue queue, IAuditService audit)
    {
        _context = context;
        _storage = storage;
        _queue = queue;
        _audit = audit;
    }

    public async Task<ArchiveOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _context.ArchiveDocuments
            .AsNoTracking()
            .GroupBy(document => document.Year)
            .Select(group => new { Year = group.Key, Documents = group.Count() })
            .ToDictionaryAsync(group => group.Year, group => group.Documents, cancellationToken);

        var byYear = Enumerable.Range(ArchiveDocument.EarliestYear, DateTime.UtcNow.Year - ArchiveDocument.EarliestYear + 1)
            .Select(year => new ArchiveYearCountDto(year, counts.GetValueOrDefault(year)))
            .ToList();

        var imports = await _context.ArchiveImports
            .AsNoTracking()
            .OrderByDescending(import => import.CreatedAtUtc)
            .ThenByDescending(import => import.Id)
            .Take(RecentImportCount)
            .Select(import => new ArchiveImportDto(
                import.Id,
                import.PackageFileName,
                import.CreatedAtUtc,
                import.Status,
                import.TotalFiles,
                import.ImportedFiles,
                import.SkippedFiles,
                import.Messages,
                import.CompletedAtUtc))
            .ToListAsync(cancellationToken);

        return new ArchiveOverviewDto(counts.Values.Sum(), byYear, imports);
    }

    public async Task<PagedResult<ArchiveDocumentDto>> GetDocumentsAsync(ArchiveQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var documents = _context.ArchiveDocuments.AsNoTracking();

        if (query.Year is { } year)
        {
            documents = documents.Where(document => document.Year == year);
        }

        if (query.DepartmentId is { } departmentId)
        {
            documents = documents.Where(document => document.DepartmentId == departmentId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            documents = documents.Where(document =>
                document.Title.Contains(search)
                || (document.Author != null && document.Author.Contains(search))
                || document.File.OriginalFileName.Contains(search));
        }

        return await documents
            .OrderByDescending(document => document.Year)
            .ThenBy(document => document.Title)
            .ThenBy(document => document.Id)
            .Select(document => new ArchiveDocumentDto(
                document.Id,
                document.Title,
                document.Author,
                document.Year,
                document.Department == null ? null : document.Department.Code,
                document.CourseCode,
                document.File.OriginalFileName,
                document.File.ExtractedText == null ? null : document.File.ExtractedText.ExtractionMethod,
                document.File.ExtractedText == null ? null : document.File.ExtractedText.WordCount,
                _context.MatchedDocuments.Count(match => match.MatchedUploadedFileId == document.File.Id)))
            .ToPagedResultAsync(query.Page, ArchiveQuery.PageSize, cancellationToken);
    }

    public async Task<AdminResult> QueueImportAsync(ArchivePackage package, string adminUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        var problem = await ValidateAsync(package, cancellationToken);

        if (problem is not null)
        {
            return AdminResult.Failure(problem);
        }

        var packagePath = await _storage.StorePackageAsync(package.Content, cancellationToken);
        var fileName = Path.GetFileName(package.FileName.Replace('\\', '/'));

        var import = new ArchiveImport
        {
            PackageFileName = fileName.Length <= ArchiveImport.MaxPackageFileNameLength ? fileName : fileName[..ArchiveImport.MaxPackageFileNameLength],
            PackagePath = packagePath,
            DefaultYear = package.DefaultYear,
            DefaultDepartmentId = package.DefaultDepartmentId,
            CreatedByUserId = adminUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };

        _context.ArchiveImports.Add(import);
        await _context.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntry(AuditActions.ArchiveImportQueued, AuditEntityTypes.ArchiveImport, Id(import.Id), adminUserId, import.PackageFileName),
            cancellationToken);

        // After the save: if the process stops before the worker gets to it, the
        // import is still Queued in the database and resumes at the next start.
        await _queue.EnqueueAsync(import.Id, cancellationToken);

        return AdminResult.Success;
    }

    public async Task<AdminResult> DeleteDocumentAsync(int documentId, string adminUserId, CancellationToken cancellationToken = default)
    {
        var document = await _context.ArchiveDocuments
            .Include(candidate => candidate.File)
            .FirstOrDefaultAsync(candidate => candidate.Id == documentId, cancellationToken);

        if (document is null)
        {
            return AdminResult.Failure("That document is no longer in the archive.");
        }

        if (await _context.MatchedDocuments.AnyAsync(match => match.MatchedUploadedFileId == document.File.Id, cancellationToken))
        {
            return AdminResult.Failure(
                $"\"{document.Title}\" has been matched by submissions, so it stays: deleting it would remove evidence from their results.");
        }

        var storagePath = document.File.StoragePath;
        var description = string.Create(CultureInfo.InvariantCulture, $"{document.Year}: {document.Title}");

        // The file row, its text and its fingerprints go with it (cascade).
        _context.ArchiveDocuments.Remove(document);
        await _context.SaveChangesAsync(cancellationToken);

        // Files are stored by content hash, so a submission may share the bytes.
        if (!await _context.UploadedFiles.AnyAsync(file => file.StoragePath == storagePath, cancellationToken))
        {
            await _storage.DeleteAsync(storagePath, cancellationToken);
        }

        await _audit.RecordAsync(
            new AuditEntry(AuditActions.RecordDeleted, AuditEntityTypes.ArchiveDocument, Id(documentId), adminUserId, description),
            cancellationToken);

        return AdminResult.Success;
    }

    private async Task<string?> ValidateAsync(ArchivePackage package, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(package.FileName), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            return "Upload a .zip file.";
        }

        if (package.SizeBytes <= 0)
        {
            return "The file is empty.";
        }

        if (package.SizeBytes > ArchiveLimits.MaxPackageBytes)
        {
            return "The ZIP is larger than 1 GB. Split it, by year for example, and upload the parts one at a time.";
        }

        if (!await StartsWithZipMagicAsync(package.Content, cancellationToken))
        {
            return "The file is not a ZIP archive.";
        }

        var latestYear = DateTime.UtcNow.Year;

        if (package.DefaultYear is { } year && (year < ArchiveDocument.EarliestYear || year > latestYear))
        {
            return string.Create(CultureInfo.InvariantCulture, $"The default year must be between {ArchiveDocument.EarliestYear} and {latestYear}.");
        }

        if (package.DefaultDepartmentId is { } departmentId
            && !await _context.Departments.AnyAsync(department => department.Id == departmentId, cancellationToken))
        {
            return "That department no longer exists.";
        }

        return null;
    }

    /// <summary>Checks the first bytes and rewinds, so the whole stream can then be stored.</summary>
    private static async Task<bool> StartsWithZipMagicAsync(Stream content, CancellationToken cancellationToken)
    {
        var header = new byte[s_zipMagic.Length];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

        content.Position = 0;

        return read == header.Length && header.AsSpan().SequenceEqual(s_zipMagic);
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);
}
