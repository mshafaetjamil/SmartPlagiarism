using System.Text;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Assembles a <see cref="TextExtractionResult"/> as an extractor walks a
/// document, so all three extractors agree on how segments are joined, how
/// offsets are counted and how the overall method is decided.
/// </summary>
public sealed class TextExtractionResultBuilder
{
    /// <summary>Blank line between segments, so page boundaries survive in the text.</summary>
    private const string SegmentSeparator = "\n\n";

    private readonly StringBuilder _fullText = new();
    private readonly List<TextSegment> _segments = [];
    private readonly List<string> _warnings = [];

    public void AddSegment(int number, SegmentKind kind, string? text, TextExtractionMethod method)
    {
        var segmentText = (text ?? string.Empty).Trim();

        if (_fullText.Length > 0)
        {
            _fullText.Append(SegmentSeparator);
        }

        _segments.Add(new TextSegment(number, kind, segmentText, method, _fullText.Length));
        _fullText.Append(segmentText);
    }

    public void AddWarning(string warning) => _warnings.Add(warning);

    public TextExtractionResult Build()
    {
        var fullText = _fullText.ToString();
        var normalized = TextNormalizer.Normalize(fullText);

        return new TextExtractionResult(
            fullText,
            normalized,
            _segments,
            TextNormalizer.CountWords(normalized),
            DetermineOverallMethod(),
            _warnings);
    }

    /// <summary>
    /// Native unless OCR contributed: all-OCR reads as Ocr, any blend as Mixed.
    /// A document with no segments at all counts as Native - nothing was recognised,
    /// so claiming OCR was involved would be misleading.
    /// </summary>
    private TextExtractionMethod DetermineOverallMethod()
    {
        var usedOcr = _segments.Any(segment => segment.Method == TextExtractionMethod.Ocr);

        if (!usedOcr)
        {
            return TextExtractionMethod.Native;
        }

        var usedNative = _segments.Any(segment => segment.Method == TextExtractionMethod.Native);

        return usedNative ? TextExtractionMethod.Mixed : TextExtractionMethod.Ocr;
    }
}
