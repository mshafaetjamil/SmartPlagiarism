using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Reads the text in pictures embedded in a DOCX or PPTX by OCR. A slide made of
/// screenshots, or scanned pages pasted into Word, carries its words only in
/// pictures; without this they would silently count as original.
///
/// Each picture is read once however often it appears, and only pictures that
/// could hold text are tried: raster formats OCR understands, big enough not to
/// be an icon, and small enough to decode safely. What OCR finds is kept only if
/// it amounts to a few words - a logo or a chart's axis labels are noise, not prose.
/// </summary>
/// <param name="maxPixels">Pictures declaring more pixels than this are not decoded.</param>
internal sealed class PictureReader(IOcrService ocr, OcrBudget budget, long maxPixels)
{
    /// <summary>Smaller files are icons and bullets, not pictures of text.</summary>
    private const long MinimumBytes = 10 * 1024;

    /// <summary>Fewer words than this from one picture are taken to be noise.</summary>
    private const int MinimumWords = 5;

    private static readonly HashSet<string> s_readableTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/bmp", "image/tiff", "image/webp",
    };

    private readonly Dictionary<Uri, string?> _read = [];
    private int _unreadWithoutOcr;
    private int _tooLarge;
    private int _unmeasured;
    private int _failed;

    /// <param name="location">Where the picture is, for warnings - "slide 4".</param>
    /// <returns>The picture's text, or null when it has none worth keeping.</returns>
    public async Task<string?> ReadAsync(OpenXmlPart? part, string location, CancellationToken cancellationToken)
    {
        if (part is not ImagePart image || !s_readableTypes.Contains(image.ContentType))
        {
            return null;
        }

        if (_read.TryGetValue(image.Uri, out var known))
        {
            return known;
        }

        _read[image.Uri] = null;

        // Read first: a part stream from a read-only package is compressed and
        // cannot say how long it is. The package's size was checked on opening.
        byte[] bytes;

        using (var stream = image.GetStream(FileMode.Open, FileAccess.Read))
        using (var copy = new MemoryStream())
        {
            await stream.CopyToAsync(copy, cancellationToken);
            bytes = copy.ToArray();
        }

        if (bytes.Length < MinimumBytes)
        {
            return null;
        }

        if (!ocr.IsAvailable)
        {
            _unreadWithoutOcr++;
            return null;
        }

        // Read from the header before OCR decodes it: decoding is what costs the
        // memory, and a small file can declare an enormous picture.
        var pixels = PictureSize.DecodedPixels(bytes);

        if (pixels is null)
        {
            _unmeasured++;
            return null;
        }

        if (pixels > maxPixels)
        {
            _tooLarge++;
            return null;
        }

        if (!budget.HasTimeLeft)
        {
            budget.SkipImage(location);
            return null;
        }

        var result = await budget.MeasureAsync(() => ocr.RecognizeAsync(bytes, cancellationToken));

        if (!result.Succeeded)
        {
            _failed++;
            return null;
        }

        var text = result.Text.Trim();
        var kept = WordTokenizer.Tokenize(text).Count >= MinimumWords ? text : null;
        _read[image.Uri] = kept;

        return kept;
    }

    /// <summary>Warnings about pictures that could not be read at all.</summary>
    public IEnumerable<string> Warnings()
    {
        if (_unreadWithoutOcr > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"The file has {_unreadWithoutOcr} picture(s) that may contain text, and OCR is unavailable "
                + $"({ocr.UnavailableReason}), so any text in them is not read.");
        }

        if (_tooLarge > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{_tooLarge} picture(s) are larger than the {maxPixels / 1_000_000.0:0.##}-megapixel limit for OCR, "
                + $"so any text in them is not measured.");
        }

        if (_unmeasured > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{_unmeasured} picture(s) are damaged or in an unrecognised format, so any text in them is not measured.");
        }

        if (_failed > 0)
        {
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"{_failed} picture(s) could not be read by OCR, so any text in them is not measured.");
        }
    }
}
