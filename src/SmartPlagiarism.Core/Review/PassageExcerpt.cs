using System.Text;

namespace SmartPlagiarism.Core.Review;

/// <summary>
/// A document cut down to its matched passages and some text either side of
/// each - for a reader entitled to the evidence of a match but not to the rest
/// of the work, such as a teacher looking at a student from another department.
///
/// Offsets come from stored analysis results and are not trusted, as in
/// <see cref="HighlightSegmenter"/>: they are clipped to the text, and empty or
/// inverted ones dropped. Cuts fall between words where a word boundary is near.
/// </summary>
public static class PassageExcerpt
{
    /// <summary>Characters kept either side of a passage, so it reads in context.</summary>
    public const int ContextCharacters = 300;

    /// <summary>Stands for each stretch of the document that is left out.</summary>
    public const string Gap = "\n\n[…]\n\n";

    /// <returns>The excerpt, and each span moved to where its passage now sits in it.</returns>
    public static (string Text, IReadOnlyList<HighlightSpan> Spans) Cut(
        string? text,
        IReadOnlyList<HighlightSpan> spans,
        int contextCharacters = ContextCharacters)
    {
        ArgumentNullException.ThrowIfNull(spans);

        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, []);
        }

        var valid = spans
            .Select(span => span with
            {
                Start = Math.Clamp(span.Start, 0, text.Length),
                End = Math.Clamp(span.End, 0, text.Length),
            })
            .Where(span => span.End > span.Start)
            .ToList();

        // Nothing matched, so nothing of the document is shown.
        if (valid.Count == 0)
        {
            return (Gap.Trim(), []);
        }

        // A window around each passage, merged with the one before where they meet.
        var windows = new List<(int Start, int End)>();

        foreach (var span in valid.OrderBy(span => span.Start))
        {
            var start = WordStart(text, Math.Max(0, span.Start - contextCharacters), span.Start);
            var end = WordEnd(text, Math.Min(text.Length, span.End + contextCharacters), span.End);

            if (windows.Count > 0 && start <= windows[^1].End)
            {
                windows[^1] = (windows[^1].Start, Math.Max(windows[^1].End, end));
            }
            else
            {
                windows.Add((start, end));
            }
        }

        var excerpt = new StringBuilder();
        var placed = new List<(int Start, int End, int At)>(windows.Count);

        foreach (var (start, end) in windows)
        {
            if (start > 0 || excerpt.Length > 0)
            {
                excerpt.Append(Gap);
            }

            placed.Add((start, end, excerpt.Length));
            excerpt.Append(text, start, end - start);
        }

        if (windows[^1].End < text.Length)
        {
            excerpt.Append(Gap);
        }

        var moved = valid
            .Select(span =>
            {
                var window = placed.First(window => window.Start <= span.Start && span.End <= window.End);
                var shift = window.At - window.Start;

                return span with { Start = span.Start + shift, End = span.End + shift };
            })
            .ToList();

        return (excerpt.ToString(), moved);
    }

    /// <summary>Moves a cut forward to the start of a word, never past <paramref name="limit"/>.</summary>
    private static int WordStart(string text, int cut, int limit)
    {
        for (var position = cut; position < limit; position++)
        {
            if (position == 0 || char.IsWhiteSpace(text[position - 1]))
            {
                return position;
            }
        }

        return cut;
    }

    /// <summary>Moves a cut back to the end of a word, never before <paramref name="limit"/>.</summary>
    private static int WordEnd(string text, int cut, int limit)
    {
        for (var position = cut; position > limit; position--)
        {
            if (position == text.Length || char.IsWhiteSpace(text[position]))
            {
                return position;
            }
        }

        return cut;
    }
}
