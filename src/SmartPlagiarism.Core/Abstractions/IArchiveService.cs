using SmartPlagiarism.Core.DTOs;
using SmartPlagiarism.Core.DTOs.Admin;
using SmartPlagiarism.Core.DTOs.Archive;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// The archive of work from before the system existed: importing it, browsing
/// it, and removing mistakes. Every archived document takes part in matching
/// exactly as a submission does.
/// </summary>
public interface IArchiveService
{
    Task<ArchiveOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<ArchiveDocumentDto>> GetDocumentsAsync(ArchiveQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores the ZIP and queues its import; the work happens in the background.
    /// Refuses anything that is not a ZIP within the size limit.
    /// </summary>
    Task<AdminResult> QueueImportAsync(ArchivePackage package, string adminUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a document imported by mistake. Refused once a submission has been
    /// matched against it: deleting it would pull evidence out of that result.
    /// </summary>
    Task<AdminResult> DeleteDocumentAsync(int documentId, string adminUserId, CancellationToken cancellationToken = default);
}

/// <summary>Runs archive imports. Used only by the background worker.</summary>
public interface IArchiveImporter
{
    /// <summary>
    /// Imports every usable file in the queued package, skipping - and saying why -
    /// the rest. Does nothing unless the import is waiting or was interrupted.
    /// </summary>
    Task ImportAsync(int importId, CancellationToken cancellationToken = default);

    /// <summary>Imports left waiting or interrupted by a restart, oldest first.</summary>
    Task<IReadOnlyList<int>> ResumePendingAsync(CancellationToken cancellationToken = default);
}

/// <summary>Hands queued archive imports to their background worker.</summary>
public interface IArchiveImportQueue
{
    ValueTask EnqueueAsync(int importId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<int> DequeueAllAsync(CancellationToken cancellationToken);
}
