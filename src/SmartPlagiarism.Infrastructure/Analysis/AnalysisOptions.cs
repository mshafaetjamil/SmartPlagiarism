namespace SmartPlagiarism.Infrastructure.Analysis;

/// <summary>Settings for the background analysis worker, bound from "Analysis".</summary>
public class AnalysisOptions
{
    public const int MaxWorkers = 8;

    /// <summary>
    /// Submissions analysed at once. Two means an ordinary submission never
    /// waits behind a long scan going through OCR; OCR itself still reads one
    /// page at a time, as Tesseract already uses every core for a page.
    /// </summary>
    public int Workers { get; set; } = 2;
}
