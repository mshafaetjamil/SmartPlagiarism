using SmartPlagiarism.Core.DTOs.Extraction;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Reads an uploaded document through the Engine and stores the text against it,
/// so a later comparison never has to re-parse the file.
///
/// Nothing calls this yet - Phase 6's background worker is what triggers it.
///
/// It takes an id rather than a loaded entity on purpose: the worker dequeues ids,
/// and the implementation needs its own DbContext scope. Handing it an entity
/// tracked by some other scope would be a bug waiting to happen.
/// </summary>
public interface IExtractionService
{
    /// <summary>
    /// Extracts text for one uploaded file and stores it, replacing any previous
    /// extraction for that file. Reports failure through the returned outcome
    /// rather than by throwing.
    /// </summary>
    Task<ExtractionOutcome> ExtractAndPersistAsync(int uploadedFileId, CancellationToken cancellationToken = default);
}
