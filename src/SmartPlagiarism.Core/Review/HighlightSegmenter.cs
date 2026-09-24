namespace SmartPlagiarism.Core.Review;

/// <summary>A passage to highlight: characters [Start, End) of the text.</summary>
/// <param name="FragmentIndex">Which matched passage this is, shared by both sides of the viewer.</param>
public sealed record HighlightSpan(int Start, int End, int FragmentIndex);

/// <summary>
/// One run of text to render. <see cref="FragmentIndex"/> is null for plain text
/// and set for highlighted text.
///
/// Segments carry the text itself, never markup: the view writes each one through
/// Razor's HTML encoding, so nothing a student wrote can ever be rendered as HTML.
/// </summary>
public sealed record HighlightSegment(string Text, int? FragmentIndex)
{
    public bool IsHighlighted => FragmentIndex.HasValue;
}

/// <summary>
/// Cuts a document's text into plain and highlighted segments from the matched
/// passages' character offsets.
///
/// The offsets come from stored analysis results, so they are not trusted:
/// spans reaching past the text (because the text was re-extracted since) are
/// clipped, and empty or inverted ones are dropped. Where passages overlap, the
/// lowest-numbered passage owns the overlap, so every character belongs to at
/// most one highlight and the segments always concatenate back to the text.
/// </summary>
public static class HighlightSegmenter
{
    public static IReadOnlyList<HighlightSegment> Segment(string? text, IEnumerable<HighlightSpan> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var valid = spans
            .Select(span => span with
            {
                Start = Math.Clamp(span.Start, 0, text.Length),
                End = Math.Clamp(span.End, 0, text.Length),
            })
            .Where(span => span.End > span.Start)
            .OrderBy(span => span.FragmentIndex)
            .ToList();

        // Every place a highlight starts or ends is a potential segment boundary.
        var boundaries = valid
            .SelectMany(span => new[] { span.Start, span.End })
            .Append(0)
            .Append(text.Length)
            .Distinct()
            .Order()
            .ToList();

        var segments = new List<HighlightSegment>();

        for (var index = 0; index + 1 < boundaries.Count; index++)
        {
            var start = boundaries[index];
            var end = boundaries[index + 1];
            var owner = valid.FirstOrDefault(span => span.Start <= start && span.End >= end);
            int? fragment = owner?.FragmentIndex;

            // Neighbouring pieces with the same owner render as one segment.
            if (segments.Count > 0 && segments[^1].FragmentIndex == fragment)
            {
                segments[^1] = segments[^1] with { Text = segments[^1].Text + text[start..end] };
            }
            else
            {
                segments.Add(new HighlightSegment(text[start..end], fragment));
            }
        }

        return segments;
    }
}
