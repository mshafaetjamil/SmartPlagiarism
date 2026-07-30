using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartPlagiarism.Core.Abstractions;
using SmartPlagiarism.Core.DTOs.Extraction;
using SmartPlagiarism.Core.Entities;
using SmartPlagiarism.Core.Enums;
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
            return ExtractionOutcome.Failure(
                $"No extractor is registered for '{Path.GetExtension(file.StoragePath)}'.");
        }

        try
        {
            await using var document = await _fileStorage.OpenReadAsync(file.StoragePath, cancellationToken);

            var result = await extractor.ExtractAsync(document, cancellationToken);

            var method = MapMethod(result.Method);
            await PersistAsync(file, result.FullText, result.WordCount, method, cancellationToken);

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
        catch (OperationCanceledException)
        {
            // Shutdown, not a document problem - let the worker see it.
            throw;
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

            return ExtractionOutcome.Failure($"Could not extract text: {ex.Message}");
        }
    }

    private async Task PersistAsync(
        UploadedFile file,
        string fullText,
        int wordCount,
        ExtractionMethod method,
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

        extracted.FullText = fullText;
        extracted.WordCount = wordCount;
        extracted.ExtractionMethod = method;
        extracted.ExtractedAtUtc = DateTime.UtcNow;

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
