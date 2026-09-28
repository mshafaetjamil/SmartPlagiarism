using SmartPlagiarism.Core.Enums;

namespace SmartPlagiarism.Core.Entities;

/// <summary>
/// Text pulled out of an uploaded file, stored once at upload time so that
/// comparing a new submission against hundreds of old ones never re-parses
/// hundreds of documents.
/// </summary>
public class ExtractedText
{
    public int Id { get; set; }

    public int UploadedFileId { get; set; }

    public UploadedFile UploadedFile { get; set; } = null!;

    public string FullText { get; set; } = string.Empty;

    public int WordCount { get; set; }

    public ExtractionMethod ExtractionMethod { get; set; }

    /// <summary>
    /// When the stored text was last read out of the file with a different result.
    /// Re-reading that finds the same text leaves it alone, so a later time than
    /// an analysis means the offsets that analysis stored may no longer line up.
    /// </summary>
    public DateTime ExtractedAtUtc { get; set; }

    /// <summary>
    /// The engine that read the text. After an upgrade that reads documents
    /// better, texts from older engines are read again - archive documents at the
    /// next start, submissions when they are re-analysed. Null for texts stored
    /// before this was recorded.
    /// </summary>
    public string? EngineVersion { get; set; }

    /// <summary>
    /// What extraction could not do for this file - a damaged Bangla page read
    /// by OCR instead, a scanned page with OCR unavailable, a file that could not
    /// be read at all. Kept so a teacher is told why a score may be low rather
    /// than shown a number with no explanation.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}
