using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Ocr;

/// <summary>
/// An always-available OCR service that returns fixed text, for testing the
/// decisions made around OCR without depending on Tesseract being installed.
/// </summary>
internal sealed class ScriptedOcrService(string text) : IOcrService
{
    public int CallCount { get; private set; }

    public bool IsAvailable => true;

    public string? UnavailableReason => null;

    public Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        CallCount++;
        return Task.FromResult(OcrResult.Success(text));
    }
}
