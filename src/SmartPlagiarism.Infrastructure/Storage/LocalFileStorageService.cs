using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Infrastructure.Storage;

/// <summary>
/// Content-addressed storage on the local filesystem.
///
/// Files are written to <c>{root}/{first 2 chars of hash}/{hash}{ext}</c>. The
/// two-character fan-out keeps any one directory from accumulating tens of
/// thousands of entries, which several filesystems handle poorly.
///
/// Because the name is derived from the contents, identical bytes always map to
/// the same path - so deduplication is a file-existence check, not a lookup.
/// </summary>
public class LocalFileStorageService : IFileStorageService
{
    private readonly string _rootPath;
    private readonly IFileUploadValidator _validator;
    private readonly ILogger<LocalFileStorageService> _logger;

    public LocalFileStorageService(
        IOptions<FileStorageOptions> options,
        IFileUploadValidator validator,
        ILogger<LocalFileStorageService> logger)
    {
        _validator = validator;
        _logger = logger;
        _rootPath = options.Value.RootPath;

        GuardAgainstWebRoot(_rootPath);
        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFile> StoreAsync(FileUpload file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);

        var validation = _validator.Validate(file.FileName, file.SizeBytes, file.Content);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"Refusing to store an invalid file: {validation.ErrorMessage}");
        }

        var format = validation.Format!;

        file.Content.Seek(0, SeekOrigin.Begin);
        var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(file.Content, cancellationToken));

        // Forward slashes in the stored value so the path is portable across
        // operating systems; the filesystem call re-splits it locally.
        var relativePath = $"{hash[..2]}/{hash}{format.Extension}";
        var absolutePath = Path.Combine(_rootPath, hash[..2], hash + format.Extension);

        var wasDeduplicated = File.Exists(absolutePath);

        if (!wasDeduplicated)
        {
            wasDeduplicated = !await WriteAtomicallyAsync(file.Content, absolutePath, cancellationToken);
        }

        if (wasDeduplicated)
        {
            _logger.LogInformation(
                "Upload {FileName} matched stored content {Hash}; reusing the existing file.",
                validation.SafeFileName,
                hash);
        }

        return new StoredFile(hash, relativePath, format.ContentType, file.SizeBytes, wasDeduplicated);
    }

    /// <summary>
    /// Writes to a temporary name and then moves it into place, so a crash midway
    /// through can never leave a truncated file sitting at a hash-named path where
    /// it would be trusted as complete.
    /// </summary>
    /// <returns>True if this call wrote the file; false if another writer won.</returns>
    private static async Task<bool> WriteAtomicallyAsync(
        Stream content,
        string absolutePath,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        var temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            content.Seek(0, SeekOrigin.Begin);

            await using (var destination = File.Create(temporaryPath))
            {
                await content.CopyToAsync(destination, cancellationToken);
            }

            File.Move(temporaryPath, absolutePath, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(absolutePath))
        {
            // A concurrent upload of the same content got there first. Identical
            // bytes, so the existing file is just as good.
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    /// <summary>
    /// CLAUDE.md requires uploads to live outside wwwroot. Misconfiguring this would
    /// quietly publish every submitted document, so it fails at startup instead.
    /// </summary>
    private static void GuardAgainstWebRoot(string rootPath)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath))
            .Replace(Path.DirectorySeparatorChar, '/');

        if (normalized.EndsWith("/wwwroot", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/wwwroot/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"FileStorage:RootPath resolves to '{normalized}', which is inside wwwroot. "
                + "Uploaded documents must not be served as static files.");
        }
    }
}
