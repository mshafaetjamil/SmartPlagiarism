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

    public DateTime ExtractedAtUtc { get; set; }

    /// <summary>
    /// What extraction could not do for this file - a damaged Bangla page read
    /// by OCR instead, a scanned page with OCR unavailable, a file that could not
    /// be read at all. Kept so a teacher is told why a score may be low rather
    /// than shown a number with no explanation.
    /// </summary>
    public List<string> Warnings { get; set; } = [];
}
