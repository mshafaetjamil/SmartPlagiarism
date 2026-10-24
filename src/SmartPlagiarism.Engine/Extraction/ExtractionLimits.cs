namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Bounds on what reading one document may cost.
///
/// Uploads are capped at 25 MB, but size on disk says little about the cost of
/// reading a file: a DOCX or PPTX is a ZIP and can unpack to gigabytes, a PDF can
/// hold thousands of pages or a page many metres wide, and OCR takes seconds to
/// minutes a page. Without bounds, one hostile or unlucky file could exhaust the
/// server's memory - which no catch block survives - or keep an analysis worker
/// busy for hours. The defaults sit far above any genuine report or deck.
/// </summary>
public sealed class ExtractionLimits
{
    /// <summary>Pages read from one PDF; the rest are skipped with a warning.</summary>
    public int MaxPdfPages { get; set; } = 2000;

    /// <summary>
    /// Total unpacked size of a DOCX or PPTX, as its ZIP directory declares it.
    /// Images barely compress, so a genuine 25 MB file unpacks to little more.
    /// </summary>
    public long MaxUnpackedOfficeBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>Files inside a DOCX or PPTX package.</summary>
    public int MaxOfficePackageEntries { get; set; } = 10_000;

    /// <summary>
    /// Characters in any one XML part, enforced by the Open XML SDK as it reads -
    /// the backstop for a package whose ZIP directory lies about its sizes.
    /// </summary>
    public long MaxCharactersInOfficePart { get; set; } = 64L * 1024 * 1024;

    /// <summary>
    /// Longer side, in pixels, of a page drawn for OCR. A4 at 300 DPI is 3508;
    /// anything larger is drawn at a lower resolution rather than allocating an
    /// image of arbitrary size.
    /// </summary>
    public int MaxRenderedPagePixels { get; set; } = 6000;

    /// <summary>
    /// Pixels in one picture handed to OCR - a picture inside a DOCX or PPTX, or an
    /// image embedded in a PDF. Decoding costs a few bytes a pixel however small
    /// the file, and a file of a few megabytes can declare billions of pixels, so
    /// a larger picture is skipped with a warning instead. An A4 page scanned at
    /// 600 DPI is 35 megapixels.
    /// </summary>
    public long MaxPicturePixels { get; set; } = 40_000_000;

    /// <summary>
    /// Time one document may spend in OCR. Pages or images still unread when it
    /// runs out are listed in a warning rather than read.
    /// </summary>
    public TimeSpan OcrTimeBudget { get; set; } = TimeSpan.FromMinutes(30);
}
