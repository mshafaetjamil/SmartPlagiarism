using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SmartPlagiarism.Tests.Extraction;

/// <summary>
/// Builds real PDFs in memory. Each format has its own fixture file because
/// PdfPig and the two Open XML dialects collide on type names like PageSize and
/// Text, and aliasing all three in one file reads far worse than three files.
/// </summary>
internal static class PdfFixture
{
    /// <summary>
    /// One page per entry. An empty entry produces a page with no text layer,
    /// which is what a scanned page looks like to PdfPig.
    /// </summary>
    internal static byte[] Create(params string[] pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var text in pages)
        {
            var page = builder.AddPage(PageSize.A4);

            if (!string.IsNullOrEmpty(text))
            {
                page.AddText(text, 12, new PdfPoint(50, 700), font);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// One page with each entry on a line of its own. Words are separated by
    /// space glyphs, but nothing separates the lines - which is how most PDF
    /// producers write a paragraph.
    /// </summary>
    internal static byte[] CreateLines(params string[] lines) => CreatePages([lines]);

    /// <summary>Like <see cref="CreateLines"/>, one entry per page.</summary>
    internal static byte[] CreatePages(IReadOnlyList<IReadOnlyList<string>> pages)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        foreach (var lines in pages)
        {
            var page = builder.AddPage(PageSize.A4);

            for (var line = 0; line < lines.Count; line++)
            {
                page.AddText(lines[line], 12, new PdfPoint(50, 780 - (line * 15)), font);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// A two-column page drawn a row at a time across both columns - left line,
    /// right line, next left line - as some producers write one.
    /// </summary>
    internal static byte[] CreateTwoColumnsDrawnAcross(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        for (var row = 0; row < Math.Max(left.Count, right.Count); row++)
        {
            var y = 780 - (row * 13);

            if (row < left.Count)
            {
                page.AddText(left[row], 10, new PdfPoint(50, y), font);
            }

            if (row < right.Count)
            {
                page.AddText(right[row], 10, new PdfPoint(320, y), font);
            }
        }

        return builder.Build();
    }

    /// <summary>A page with a short heading above a picture covering most of it.</summary>
    internal static byte[] CreatePicturePage(string heading, byte[] png)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);

        page.AddText(heading, 14, new PdfPoint(50, 790), font);
        page.AddPng(png, new PdfRectangle(40, 60, 555, 770));

        return builder.Build();
    }

    /// <summary>
    /// One line whose words are each placed at their own position with no space
    /// glyph between them, which is how LaTeX writes a PDF.
    /// </summary>
    internal static byte[] CreateWithoutSpaceGlyphs(string sentence)
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(PageSize.A4);
        var x = 50d;

        foreach (var word in sentence.Split(' '))
        {
            var letters = page.AddText(word, 12, new PdfPoint(x, 700), font);
            x = letters[^1].EndBaseLine.X + 4;
        }

        return builder.Build();
    }
}
