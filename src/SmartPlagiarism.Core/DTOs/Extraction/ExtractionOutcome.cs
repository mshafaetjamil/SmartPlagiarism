using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.DTOs.Extraction;

/// <summary>
/// Result of extracting and storing the text of one uploaded file.
/// </summary>
/// <param name="Method">Null when extraction did not get far enough to decide.</param>
/// <param name="Warnings">
/// Non-fatal problems - pages with no text, OCR unavailable. Extraction can
/// succeed with warnings; Phase 6 surfaces them without failing the analysis.
/// </param>
public sealed record ExtractionOutcome(
    bool Succeeded,
    ExtractionMethod? Method,
    int WordCount,
    IReadOnlyList<string> Warnings,
    string? Error)
{
    public static ExtractionOutcome Success(
        ExtractionMethod method,
        int wordCount,
        IReadOnlyList<string> warnings) => new(true, method, wordCount, warnings, null);

    public static ExtractionOutcome Failure(string error) => new(false, null, 0, [], error);
}
