using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Content-addressed storage for uploaded documents, outside the web root.
/// </summary>
public interface IFileStorageService
{
    /// <summary>
    /// Validates, hashes and stores a file. Identical bytes already in the store are
    /// reused rather than written twice.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The file fails validation. Callers are expected to have validated already via
    /// <see cref="IFileUploadValidator"/>; this re-check is defence in depth, so
    /// reaching it means a caller skipped its own validation.
    /// </exception>
    Task<StoredFile> StoreAsync(FileUpload file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a previously stored file for reading. The caller disposes the stream.
    /// </summary>
    /// <param name="storagePath">
    /// A <see cref="StoredFile.StoragePath"/> value, relative to the storage root.
    /// </param>
    /// <exception cref="FileNotFoundException">Nothing is stored at that path.</exception>
    /// <exception cref="UnauthorizedAccessException">
    /// The path resolves outside the storage root.
    /// </exception>
    Task<Stream> OpenReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
