using System.Text;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Removes a PDF's running headers and footers: lines repeated at the top or
/// bottom of most pages. A DOCX keeps these in parts of their own that are never
/// read, so without this the same report scores differently as a PDF - and every
/// report made from the university's template matches every other on them.
///
/// Lines are compared with each run of digits masked, so "Page 3 of 40" and
/// "Page 4 of 40" are the same line, and a bare page number is one line
/// throughout the document.
/// </summary>
internal static class PdfRunningLines
{
    /// <summary>Non-empty lines at each end of a page that may be a header or footer.</summary>
    private const int EdgeLines = 2;

    /// <summary>A line must repeat on at least this many pages...</summary>
    private const int MinimumPages = 3;

    /// <summary>...and on at least this share of them. Below half, so alternating
    /// left and right page headers still count.</summary>
    private const double MinimumShare = 0.4;

    public static IReadOnlyList<string> Remove(IReadOnlyList<string> pages)
    {
        if (pages.Count < MinimumPages)
        {
            return pages;
        }

        var split = pages.Select(page => page.Split('\n').Select(line => line.TrimEnd('\r')).ToList()).ToList();
        var edges = split.Select(EdgeIndexes).ToList();
        var pagesWithLine = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var page = 0; page < split.Count; page++)
        {
            foreach (var key in edges[page].Select(index => Key(split[page][index])).Distinct())
            {
                pagesWithLine[key] = pagesWithLine.GetValueOrDefault(key) + 1;
            }
        }

        var threshold = Math.Max(MinimumPages, (int)Math.Ceiling(pages.Count * MinimumShare));
        var running = pagesWithLine.Where(entry => entry.Value >= threshold).Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);

        if (running.Count == 0)
        {
            return pages;
        }

        var result = new List<string>(pages.Count);

        for (var page = 0; page < split.Count; page++)
        {
            var remove = edges[page].Where(index => running.Contains(Key(split[page][index]))).ToHashSet();

            result.Add(remove.Count == 0
                ? pages[page]
                : string.Join(Environment.NewLine, split[page].Where((_, index) => !remove.Contains(index))));
        }

        return result;
    }

    /// <summary>Indexes of the first and last few non-empty lines.</summary>
    private static List<int> EdgeIndexes(List<string> lines)
    {
        var nonEmpty = Enumerable.Range(0, lines.Count).Where(index => !string.IsNullOrWhiteSpace(lines[index])).ToList();

        return [.. nonEmpty.Take(EdgeLines).Concat(nonEmpty.TakeLast(EdgeLines)).Distinct()];
    }

    /// <summary>The line lower-cased, with whitespace collapsed and digit runs masked.</summary>
    private static string Key(string line)
    {
        var key = new StringBuilder(line.Length);
        var previous = ' ';

        foreach (var character in line.Trim())
        {
            var next = char.IsDigit(character) ? '#' : char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character);

            if ((next == '#' || next == ' ') && next == previous)
            {
                continue;
            }

            key.Append(next);
            previous = next;
        }

        return key.ToString();
    }
}
