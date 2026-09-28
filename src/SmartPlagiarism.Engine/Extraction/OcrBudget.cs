using System.Diagnostics;
using System.Globalization;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// The OCR time one document is allowed, and what had to be left unread once it
/// ran out. Time is measured only while the document waits for or runs OCR, so
/// a long document of ordinary text is not held against its few scanned pages.
/// </summary>
internal sealed class OcrBudget(TimeSpan allowance)
{
    /// <summary>Unread images named individually before the rest are only counted.</summary>
    private const int ImagesListed = 5;

    private readonly Stopwatch _spent = new();
    private readonly SortedSet<int> _skippedPages = [];
    private readonly List<string> _skippedImages = [];

    public bool HasTimeLeft => _spent.Elapsed < allowance;

    /// <summary>Runs one OCR call on the clock.</summary>
    public async Task<T> MeasureAsync<T>(Func<Task<T>> recognize)
    {
        _spent.Start();

        try
        {
            return await recognize();
        }
        finally
        {
            _spent.Stop();
        }
    }

    public void SkipPage(int pageNumber) => _skippedPages.Add(pageNumber);

    /// <summary>Records an image left unread, described by where it is ("slide 4").</summary>
    public void SkipImage(string location) => _skippedImages.Add(location);

    /// <summary>One warning for everything skipped, or null when nothing was.</summary>
    public string? Warning()
    {
        var unread = new List<string>();

        if (_skippedPages.Count > 0)
        {
            unread.Add((_skippedPages.Count == 1 ? "page " : "pages ") + DescribeRanges(_skippedPages));
        }

        if (_skippedImages.Count > 0)
        {
            var listed = string.Join(", ", _skippedImages.Take(ImagesListed));
            var more = _skippedImages.Count - ImagesListed;

            unread.Add(more > 0
                ? string.Create(CultureInfo.InvariantCulture, $"images on {listed} and {more} more")
                : $"images on {listed}");
        }

        return unread.Count == 0
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"OCR stopped at the {allowance.TotalMinutes:0}-minute limit for one document, so similarity is not "
                + $"measured for {string.Join(" and ", unread)}.");
    }

    /// <summary>"3, 7, 9-12" for 3, 7, 9, 10, 11, 12.</summary>
    internal static string DescribeRanges(IEnumerable<int> sortedNumbers)
    {
        var ranges = new List<string>();
        int? start = null;
        var end = 0;

        foreach (var number in sortedNumbers)
        {
            if (start is not null && number == end + 1)
            {
                end = number;
                continue;
            }

            if (start is not null)
            {
                ranges.Add(Range(start.Value, end));
            }

            start = end = number;
        }

        if (start is not null)
        {
            ranges.Add(Range(start.Value, end));
        }

        return string.Join(", ", ranges);

        static string Range(int first, int last) => first == last
            ? first.ToString(CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"{first}-{last}");
    }
}
