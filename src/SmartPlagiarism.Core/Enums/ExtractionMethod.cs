namespace SmartPlagiarism.Core.Enums;

/// <summary>
/// How text was recovered from a document. Recorded because OCR output is far
/// noisier than native text, which matters when interpreting a similarity score.
/// </summary>
public enum ExtractionMethod
{
    /// <summary>Text layer read directly from the document.</summary>
    Native = 0,

    /// <summary>Whole document was scanned images; everything came from OCR.</summary>
    Ocr = 1,

    /// <summary>Some pages had a text layer, others needed OCR.</summary>
    Mixed = 2,
}
