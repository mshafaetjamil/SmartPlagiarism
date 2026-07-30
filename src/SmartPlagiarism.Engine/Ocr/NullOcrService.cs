namespace SmartPlagiarism.Engine.Ocr;

/// <summary>
/// An OCR service that never recognises anything. Used when OCR is switched off,
/// and by tests that need extraction to behave as it does on a machine without
/// Tesseract installed.
/// </summary>
public sealed class NullOcrService : IOcrService
{
    public NullOcrService(string? reason = null)
    {
        UnavailableReason = reason ?? "OCR is not configured.";
    }

    public bool IsAvailable => false;

    public string? UnavailableReason { get; }

    public Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
        Task.FromResult(OcrResult.Failure(UnavailableReason!));
}
