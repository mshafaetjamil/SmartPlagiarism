using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Extraction;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
using SmartPlagiarism.Engine;
using SmartPlagiarism.Engine.Extraction;
using SmartPlagiarism.Infrastructure.Data;

namespace SmartPlagiarism.Infrastructure.Services;

/// <inheritdoc cref="IExtractionService"/>
public class ExtractionService : IExtractionService
{
    private readonly AppDbContext _context;
    private readonly IFileStorageService _fileStorage;
    private readonly IExtractorFactory _extractorFactory;
    private readonly ILogger<ExtractionService> _logger;

    public ExtractionService(
        AppDbContext context,
        IFileStorageService fileStorage,
        IExtractorFactory extractorFactory,
        ILogger<ExtractionService> logger)
    {
        _context = context;
        _fileStorage = fileStorage;
        _extractorFactory = extractorFactory;
        _logger = logger;
    }

    public async Task<ExtractionOutcome> ExtractAndPersistAsync(
        int uploadedFileId,
        CancellationToken cancellationToken = default)
    {
        var file = await _context.UploadedFiles
            .Include(uploaded => uploaded.ExtractedText)
            .FirstOrDefaultAsync(uploaded => uploaded.Id == uploadedFileId, cancellationToken);

        if (file is null)
        {
            return ExtractionOutcome.Failure($"Uploaded file {uploadedFileId} was not found.");
        }

        // The extension comes from StoragePath, not from OriginalFileName: the
        // storage path was built from the format the validator actually detected,
        // whereas the original name is whatever the client claimed.
        var extractor = _extractorFactory.Find(file.StoragePath);

        if (extractor is null)
        {
            return await FailAsync(file, $"No extractor is registered for '{Path.GetExtension(file.StoragePath)}'.", cancellationToken);
        }

        TextExtractionResult result;

        // Only reading the document is guarded here. A failure saving the text
        // below is not the file's fault and must not be reported to the student
        // as a corrupt file; it propagates to the analyzer, which records an
        // unexpected error that a teacher can re-run.
        try
        {
            await using var document = await _fileStorage.OpenReadAsync(file.StoragePath, cancellationToken);
            result = await extractor.ExtractAsync(document, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a document problem - let the worker see it.
            throw;
        }
        catch (DocumentTooLargeException ex)
        {
            // Refused before any real work was done; the reason is written for
            // the student, who can split or re-save the file.
            _logger.LogWarning(
                "Uploaded file {UploadedFileId} ({FileName}) is over an extraction limit: {Reason}",
                file.Id,
                file.OriginalFileName,
                ex.Message);

            return await FailAsync(file, ex.Message, cancellationToken);
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable document must fail this one file, never the
            // worker loop that called us.
            _logger.LogError(
                ex,
                "Extraction failed for uploaded file {UploadedFileId} ({FileName}).",
                file.Id,
                file.OriginalFileName);

            return await FailAsync(file, "The file could not be read; it may be corrupt or password-protected.", cancellationToken);
        }

        var method = MapMethod(result.Method);
        await PersistAsync(file, result.FullText, result.WordCount, method, result.Warnings, cancellationToken);

        foreach (var warning in result.Warnings)
        {
            _logger.LogWarning(
                "Extraction warning for uploaded file {UploadedFileId} ({FileName}): {Warning}",
                file.Id,
                file.OriginalFileName,
                warning);
        }

        _logger.LogInformation(
            "Extracted {WordCount} words from uploaded file {UploadedFileId} using {Method}.",
            result.WordCount,
            file.Id,
            method);

        return ExtractionOutcome.Success(method, result.WordCount, result.Warnings);
    }

    /// <summary>
    /// Records a file that yielded no text at all as an empty extraction carrying
    /// the reason, so the file's own row explains why it matched nothing - rather
    /// than a missing row that says nothing, or a stale one from an earlier run.
    /// </summary>
    private async Task<ExtractionOutcome> FailAsync(
        UploadedFile file,
        string reason,
        CancellationToken cancellationToken)
    {
        await PersistAsync(file, string.Empty, 0, ExtractionMethod.Native, [reason], cancellationToken);
        return ExtractionOutcome.Failure(reason);
    }

    private async Task PersistAsync(
        UploadedFile file,
        string fullText,
        int wordCount,
        ExtractionMethod method,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        // Replace rather than accumulate: re-extraction of the same file should
        // leave exactly one row, matching the 1:1 relationship in the schema.
        var extracted = file.ExtractedText;

        if (extracted is null)
        {
            extracted = new ExtractedText { UploadedFileId = file.Id };
            _context.ExtractedTexts.Add(extracted);
        }

        // The time moves only when the text does: matches elsewhere store offsets
        // into it, and are flagged as stale by comparing against this time.
        if (extracted.ExtractedAtUtc == default || !string.Equals(extracted.FullText, fullText, StringComparison.Ordinal))
        {
            extracted.ExtractedAtUtc = DateTime.UtcNow;
        }

        extracted.FullText = fullText;
        extracted.WordCount = wordCount;
        extracted.ExtractionMethod = method;
        extracted.EngineVersion = EngineInfo.Version;
        extracted.Warnings = [.. warnings];

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The Engine cannot reference Core, so it has its own copy of this enum. This
    /// is the single place the two meet.
    /// </summary>
    private static ExtractionMethod MapMethod(TextExtractionMethod method) => method switch
    {
        TextExtractionMethod.Native => ExtractionMethod.Native,
        TextExtractionMethod.Ocr => ExtractionMethod.Ocr,
        TextExtractionMethod.Mixed => ExtractionMethod.Mixed,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unmapped extraction method."),
    };
}
