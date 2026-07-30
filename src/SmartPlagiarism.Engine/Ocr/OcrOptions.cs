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

    /// <summary>Language code, matching a <c>{language}.traineddata</c> file.</summary>
    public string Language { get; set; } = "eng";
}
