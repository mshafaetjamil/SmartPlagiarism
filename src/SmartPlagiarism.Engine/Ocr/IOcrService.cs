namespace SmartPlagiarism.Engine.Ocr;

/// <summary>Text recognised from one image.</summary>
/// <param name="Succeeded">False when recognition could not run or failed.</param>
/// <param name="Text">Recognised text, empty when it did not succeed.</param>
/// <param name="Error">Why it failed, for the extraction warnings.</param>
public sealed record OcrResult(bool Succeeded, string Text, string? Error)
{
    public static OcrResult Success(string text) => new(true, text, null);

    public static OcrResult Failure(string error) => new(false, string.Empty, error);
}

/// <summary>
/// Optical character recognition, kept behind an interface so the Engine works
/// without it. Implementations must report unavailability through
/// <see cref="IsAvailable"/> rather than throwing.
/// </summary>
public interface IOcrService
{
    /// <summary>False when OCR cannot run - no native library, no tessdata, disabled.</summary>
    bool IsAvailable { get; }

    /// <summary>Why OCR is unavailable, for the extraction warnings. Null when available.</summary>
    string? UnavailableReason { get; }

    /// <param name="imageBytes">A PNG image.</param>
    Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default);
}
