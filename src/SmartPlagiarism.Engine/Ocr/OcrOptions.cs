namespace SmartPlagiarism.Engine.Ocr;

public class OcrOptions
{
    /// <summary>Set false to skip OCR entirely, even if tessdata is present.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Directory holding the .traineddata files. Absolute by the time it reaches
    /// the Engine - resolving relative paths is the host's job.
    /// </summary>
    public string TessDataPath { get; set; } = string.Empty;

    /// <summary>
    /// Tesseract language codes joined by "+", each matching a
    /// <c>{language}.traineddata</c> file. "ben+eng" reads Bangla and English on
    /// the same page, which is what a Bangla report with English terms needs.
    /// </summary>
    public string Language { get; set; } = "ben+eng";

    /// <summary>
    /// Path to the <c>tesseract</c> command-line program, used where the in-process
    /// library cannot load (macOS, and Linux without matching library names).
    /// Empty means search PATH and the usual install locations.
    /// </summary>
    public string TesseractExecutable { get; set; } = string.Empty;

    /// <summary>
    /// Time Tesseract may spend on one image - a page or a picture. A 300 DPI
    /// Bangla page takes a few seconds; anything this slow is stuck, and one stuck
    /// image must not hold up every other document waiting for OCR.
    /// </summary>
    public TimeSpan PageTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>The individual language codes in <see cref="Language"/>.</summary>
    public IReadOnlyList<string> Languages =>
        Language.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
