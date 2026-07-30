namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Everything one extraction produced.
///
/// <see cref="FullText"/> is kept verbatim for display and highlighting;
/// <see cref="NormalizedText"/> is the form the similarity engine compares.
/// Keeping both means offsets into the original survive.
/// </summary>
public sealed class TextExtractionResult
{
    internal TextExtractionResult(
        string fullText,
        string normalizedText,
        IReadOnlyList<TextSegment> segments,
        int wordCount,
        TextExtractionMethod method,
        IReadOnlyList<string> warnings)
    {
        FullText = fullText;
        NormalizedText = normalizedText;
        Segments = segments;
        WordCount = wordCount;
        Method = method;
        Warnings = warnings;
    }

    /// <summary>Extracted text exactly as it came out of the document.</summary>
    public string FullText { get; }

    /// <summary>Lowercased, unicode-normalised, punctuation-stripped form used for analysis.</summary>
    public string NormalizedText { get; }

    public IReadOnlyList<TextSegment> Segments { get; }

    /// <summary>Words in <see cref="NormalizedText"/>.</summary>
    public int WordCount { get; }

    /// <summary>Method across the document as a whole.</summary>
    public TextExtractionMethod Method { get; }

    /// <summary>
    /// Non-fatal problems: pages that yielded nothing, OCR being unavailable, and
    /// so on. Extraction never throws for these - it reports them.
    /// </summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>True when the document produced no text at all.</summary>
    public bool IsEmpty => WordCount == 0;
}
