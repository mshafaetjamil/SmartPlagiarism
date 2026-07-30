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
}
