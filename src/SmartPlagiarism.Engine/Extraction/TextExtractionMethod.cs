namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// How text was recovered from a document.
///
/// The Engine deliberately defines its own copy rather than sharing Core's
/// ExtractionMethod: CLAUDE.md forbids this project from referencing any other,
/// so the mapping happens at the Core boundary.
/// </summary>
public enum TextExtractionMethod
{
    /// <summary>Read from the document's own text layer.</summary>
    Native = 0,

    /// <summary>Recognised from page images because there was no usable text layer.</summary>
    Ocr = 1,

    /// <summary>Some parts native, others OCR.</summary>
    Mixed = 2,
}
