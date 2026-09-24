using PDFtoImage;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>A rendered page, or why it could not be rendered.</summary>
public sealed record PageRenderResult(byte[]? Png, string? Error)
{
    public bool Succeeded => Png is { Length: > 0 };

    public static PageRenderResult Success(byte[] png) => new(png, null);

    public static PageRenderResult Failure(string error) => new(null, error);
}

/// <summary>
/// Draws a PDF page as an image, so OCR can read what the page actually shows.
///
/// PdfPig reads a PDF's text layer but cannot draw pages. That layer is useless
/// in two cases this project must handle: a scanned page, where there is no text
/// layer, and Bengali, where the layer is often too damaged to trust. In both,
/// OCR of the drawn page is the only way to recover the words.
/// </summary>
public interface IPdfPageRenderer
{
    /// <param name="pageNumber">One-based, matching PdfPig's page numbering.</param>
    PageRenderResult RenderPng(byte[] pdf, int pageNumber);
}

/// <summary>
/// Renders with PDFium, the engine inside Chrome, through the PDFtoImage package,
/// which ships native builds for Windows, Linux and macOS (x64 and arm64).
/// </summary>
public sealed class PdfiumPageRenderer : IPdfPageRenderer
{
    /// <summary>
    /// 300 DPI is what Tesseract is trained on. Lower loses the small vowel signs
    /// and joining strokes Bengali depends on; higher only costs time.
    /// </summary>
    public const int DefaultDpi = 300;

    // PDFium keeps global state and is not safe to call concurrently.
    private static readonly Lock s_pdfiumLock = new();

    private readonly int _dpi;

    public PdfiumPageRenderer(int dpi = DefaultDpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 72);
        _dpi = dpi;
    }

    public PageRenderResult RenderPng(byte[] pdf, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);

        // PDFium ships for these three; the server never runs anywhere else.
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return PageRenderResult.Failure("page rendering is not supported on this operating system.");
        }

        try
        {
            using var png = new MemoryStream();

            lock (s_pdfiumLock)
            {
                Conversion.SavePng(png, pdf, pageNumber - 1, password: null, new RenderOptions(Dpi: _dpi));
            }

            return PageRenderResult.Success(png.ToArray());
        }
        catch (Exception ex)
        {
            // Broad on purpose, as with Tesseract: a missing native library, a
            // corrupt page and an encrypted file all mean "no image", and none of
            // them may stop the rest of the document being extracted.
            return PageRenderResult.Failure($"the page could not be rendered for OCR: {ex.Message}");
        }
    }
}
