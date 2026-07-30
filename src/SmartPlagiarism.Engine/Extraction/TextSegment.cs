namespace SmartPlagiarism.Engine.Extraction;

/// <summary>What a segment corresponds to in the source document.</summary>
public enum SegmentKind
{
    Page = 0,
    Slide = 1,
}

/// <summary>
/// One page or slide of extracted text.
/// </summary>
/// <param name="Number">1-based page or slide number.</param>
/// <param name="Kind">Whether this is a page or a slide.</param>
/// <param name="Text">The segment's text, exactly as extracted.</param>
/// <param name="Method">How this particular segment's text was obtained.</param>
/// <param name="StartOffset">
/// Index of this segment's first character within
/// <see cref="TextExtractionResult.FullText"/>, so a match found in the whole
/// document can be traced back to the page it came from.
/// </param>
public sealed record TextSegment(
    int Number,
    SegmentKind Kind,
    string Text,
    TextExtractionMethod Method,
    int StartOffset)
{
    public int Length => Text.Length;
}
