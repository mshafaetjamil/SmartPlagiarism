using SmartPlagiarism.Engine.Text;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Util;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Reads a PDF page's letters in the order they were drawn, puts back the word
/// and line breaks the file never stored, and reads two-column pages one column
/// at a time.
///
/// A PDF positions glyphs; it has no notion of words or lines. Most producers
/// draw a space glyph between words but nothing between lines, and LaTeX draws
/// no spaces at all - it just starts the next word further along. Joining the
/// letters as they are, which is what PdfPig's <c>Page.Text</c> does, glues the
/// last word of every line to the first word of the next, and turns a LaTeX line
/// into a single word.
///
/// The breaks are inferred the way PdfPig's own <c>ContentOrderTextExtractor</c>
/// infers them: a baseline that moves by most of a font size is a new line, and
/// a horizontal gap wider than about a fifth of the font size is a space. That
/// extractor returns a plain string, though, and the Bijoy conversion needs each
/// letter's font, so the letters are yielded here as font runs.
///
/// Columns: most producers draw a two-column page one column after the other,
/// which reads correctly as it is. Some draw it line by line across both
/// columns, which interleaves them - a sentence in the left column is broken by
/// a line from the right at every line end. Such lines share one wide gap, the
/// gutter, and where enough of them do, the left parts are read before the
/// right.
/// </summary>
internal static class PdfPageLayout
{
    /// <summary>
    /// A baseline shift larger than this share of the smaller font size is a line
    /// break. Superscripts and subscripts move much less than this.
    /// </summary>
    private const double LineBreakRatio = 0.9;

    /// <summary>
    /// A jump back along the same baseline by more than this share of the font
    /// size means the text resumes somewhere else - the next cell of a table, the
    /// other column - so the two sides are separate words. Smaller steps back are
    /// marks drawn over the letter before them, as Bangla vowel signs are.
    /// </summary>
    private const double ResumeElsewhereRatio = 1.0;

    /// <summary>
    /// A gap on one line wider than this many font sizes could be a column
    /// gutter. Justified text stretches word spaces to about one.
    /// </summary>
    private const double WideGapRatio = 2.0;

    /// <summary>Lines that must share a gutter before the page is read as two columns.</summary>
    private const int MinimumColumnLines = 3;

    /// <summary>The gutter of a two-column page lies in this middle band of its width.</summary>
    private const double GutterBandStart = 0.3;

    private const double GutterBandEnd = 0.7;

    public static IEnumerable<FontRun> Runs(Page page)
    {
        var lines = ReadLines(page);
        var first = true;

        foreach (var line in InReadingOrder(lines, page.Width))
        {
            if (!first)
            {
                yield return new FontRun(Environment.NewLine, null);
            }

            first = false;

            for (var index = 0; index < line.Count; index++)
            {
                if (index > 0)
                {
                    yield return new FontRun(" ", null);
                }

                foreach (var (spaceBefore, letter) in line[index].Letters)
                {
                    if (spaceBefore)
                    {
                        yield return new FontRun(" ", null);
                    }

                    yield return new FontRun(letter.Value, letter.FontName);
                }
            }
        }
    }

    /// <summary>
    /// The page's visible letters grouped into lines, and each line into
    /// segments wherever a wide gap or a jump back separates its text.
    /// </summary>
    private static List<Line> ReadLines(Page page)
    {
        var lines = new List<Line>();
        Segment? segment = null;
        Letter? previous = null;
        var lineEnd = 0d;
        var spaceBefore = false;

        foreach (var letter in page.Letters)
        {
            if (string.IsNullOrWhiteSpace(letter.Value))
            {
                // A space glyph separates words. A glyph with no text at all is
                // usually a Bangla conjunct the font gives no Unicode for; it
                // separates nothing, but it still takes up room on the line, and
                // skipping over that room would read as a gap between words.
                if (string.IsNullOrEmpty(letter.Value))
                {
                    lineEnd = previous is null ? lineEnd : Math.Max(lineEnd, letter.EndBaseLine.X);
                }
                else
                {
                    spaceBefore = true;
                }

                continue;
            }

            if (previous is null || StartsNewLine(previous, letter))
            {
                lines.Add(new Line());
                segment = lines[^1].StartSegment();
                spaceBefore = false;
            }
            else
            {
                // Measured from the furthest point the line has reached rather
                // than from the previous letter, whose end is not the line's end
                // when it is a mark drawn over the letter before it.
                var gap = letter.StartBaseLine.X - lineEnd;

                if (gap < -letter.PointSize * ResumeElsewhereRatio || gap > letter.PointSize * WideGapRatio)
                {
                    segment = lines[^1].StartSegment();
                    spaceBefore = false;
                }
                else if (WhitespaceSizeStatistics.IsProbablyWhitespace(gap, previous))
                {
                    spaceBefore = true;
                }
            }

            segment!.Add(spaceBefore, letter);
            lineEnd = segment.Letters.Count == 1 ? letter.EndBaseLine.X : Math.Max(lineEnd, letter.EndBaseLine.X);
            spaceBefore = false;
            previous = letter;
        }

        return lines;
    }

    private static bool StartsNewLine(Letter previous, Letter letter)
    {
        var fontSize = Math.Min(previous.PointSize, letter.PointSize);

        return Math.Abs(previous.StartBaseLine.Y - letter.StartBaseLine.Y) > fontSize * LineBreakRatio;
    }

    /// <summary>
    /// The lines to output, each a list of segments. Unchanged unless the page
    /// has a gutter; then each run of lines that keeps clear of it is read left
    /// column first.
    /// </summary>
    private static List<List<Segment>> InReadingOrder(List<Line> lines, double pageWidth)
    {
        var ordered = new List<List<Segment>>(lines.Count);

        if (FindGutter(lines, pageWidth) is not { } gutter)
        {
            ordered.AddRange(lines.Select(line => line.Segments));
            return ordered;
        }

        var block = new List<Line>();

        foreach (var line in lines)
        {
            if (line.Segments.All(segment => segment.Right <= gutter || segment.Left >= gutter))
            {
                block.Add(line);
                continue;
            }

            // A line across the gutter - a full-width heading - ends the columns.
            FlushColumns(block, gutter, ordered);
            ordered.Add(line.Segments);
        }

        FlushColumns(block, gutter, ordered);

        return ordered;
    }

    private static void FlushColumns(List<Line> block, double gutter, List<List<Segment>> ordered)
    {
        foreach (var column in new Func<Segment, bool>[] { segment => segment.Right <= gutter, segment => segment.Left >= gutter })
        {
            foreach (var line in block)
            {
                var part = line.Segments.Where(column).ToList();

                if (part.Count > 0)
                {
                    ordered.Add(part);
                }
            }
        }

        block.Clear();
    }

    /// <summary>
    /// The x-position of a gap in the middle of the page that enough lines
    /// share to be a column gutter, or null.
    /// </summary>
    private static double? FindGutter(List<Line> lines, double pageWidth)
    {
        var gaps = lines
            .Select(line => line.Gaps().ToList())
            .Where(lineGaps => lineGaps.Count > 0)
            .ToList();

        if (gaps.Count < MinimumColumnLines)
        {
            return null;
        }

        double? best = null;
        var bestCount = MinimumColumnLines - 1;

        foreach (var (left, right) in gaps.SelectMany(lineGaps => lineGaps))
        {
            var candidate = (left + right) / 2;

            if (candidate < pageWidth * GutterBandStart || candidate > pageWidth * GutterBandEnd)
            {
                continue;
            }

            var count = gaps.Count(lineGaps => lineGaps.Any(gap => gap.Left <= candidate && candidate <= gap.Right));

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    private sealed class Line
    {
        public List<Segment> Segments { get; } = [];

        public Segment StartSegment()
        {
            var segment = new Segment();
            Segments.Add(segment);
            return segment;
        }

        /// <summary>The empty stretches between segments, left to right.</summary>
        public IEnumerable<(double Left, double Right)> Gaps()
        {
            var ordered = Segments.OrderBy(segment => segment.Left).ToList();

            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index - 1].Right < ordered[index].Left)
                {
                    yield return (ordered[index - 1].Right, ordered[index].Left);
                }
            }
        }
    }

    private sealed class Segment
    {
        public List<(bool SpaceBefore, Letter Letter)> Letters { get; } = [];

        public double Left { get; private set; } = double.MaxValue;

        public double Right { get; private set; } = double.MinValue;

        public void Add(bool spaceBefore, Letter letter)
        {
            Letters.Add((spaceBefore, letter));
            Left = Math.Min(Left, Math.Min(letter.StartBaseLine.X, letter.EndBaseLine.X));
            Right = Math.Max(Right, Math.Max(letter.StartBaseLine.X, letter.EndBaseLine.X));
        }
    }
}
