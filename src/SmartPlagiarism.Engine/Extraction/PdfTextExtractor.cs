using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Extracts text from PDFs with PdfPig, turning to OCR of the drawn page where
/// the embedded text cannot be trusted.
///
/// Each page takes one of three routes:
/// <list type="bullet">
///   <item><description>
///     <b>Ordinary text layer</b> - used as is.
///   </description></item>
///   <item><description>
///     <b>Bengali text layer</b> - put back into typing order, then checked. PDF
///     fonts frequently have no Unicode mapping for Bengali conjuncts, so the
///     layer can be damaged beyond repair; such a page is drawn and read by OCR
///     instead, whichever of the two reads more cleanly winning.
///   </description></item>
///   <item><description>
///     <b>No text layer</b> (a scan) - drawn and read by OCR. If the page cannot
///     be drawn, its embedded images are read instead, which is what a scanned
///     page is.
///   </description></item>
/// </list>
/// No route throws: when OCR is unavailable the best text there is gets used,
/// and a warning records exactly what was lost so a teacher is never shown a
/// low score without knowing why.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    /// <summary>
    /// A page with fewer visible characters than this is treated as having no text
    /// layer. Around twenty characters is below any real paragraph but above the
    /// stray page number or header that scanners often leave behind.
    /// </summary>
    public const int MinimumNativeCharacters = 20;

    private readonly IOcrService _ocr;
    private readonly IPdfPageRenderer _renderer;

    public PdfTextExtractor(IOcrService ocr, IPdfPageRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(renderer);

        _ocr = ocr;
        _renderer = renderer;
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public async Task<TextExtractionResult> ExtractAsync(
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var bytes = await StreamHelpers.ReadAllBytesAsync(document, cancellationToken);
        var builder = new TextExtractionResultBuilder();

        using var pdf = PdfDocument.Open(bytes);

        foreach (var page in pdf.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var nativeText = page.Text ?? string.Empty;

            if (CountVisibleCharacters(nativeText) < MinimumNativeCharacters)
            {
                await AddScannedPageAsync(builder, bytes, page, nativeText, cancellationToken);
            }
            else if (BengaliText.ContainsBengali(nativeText))
            {
                await AddBengaliPageAsync(builder, bytes, page.Number, nativeText, cancellationToken);
            }
            else
            {
                builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
            }
        }

        if (pdf.NumberOfPages == 0)
        {
            builder.AddWarning("The PDF contains no pages.");
        }

        return builder.Build();
    }

    private async Task AddBengaliPageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        int pageNumber,
        string nativeText,
        CancellationToken cancellationToken)
    {
        var repaired = BengaliText.RepairDrawingOrder(nativeText);
        var nativeQuality = BengaliText.Assess(repaired);

        if (!nativeQuality.IsDamaged)
        {
            builder.AddSegment(pageNumber, SegmentKind.Page, repaired, TextExtractionMethod.Native);
            return;
        }

        var damage = $"Page {pageNumber}: the PDF's embedded Bangla text is damaged "
            + $"({nativeQuality.SuspectWords} of {nativeQuality.BengaliWords} words unreadable, "
            + $"{nativeQuality.ConjunctRatio:P0} with conjuncts)";

        if (!_ocr.IsAvailable)
        {
            builder.AddWarning(
                $"{damage} and OCR is unavailable ({_ocr.UnavailableReason}). "
                + "Similarity for this page will be understated; a DOCX or PPTX copy gives exact results.");
            builder.AddSegment(pageNumber, SegmentKind.Page, repaired, TextExtractionMethod.Native);
            return;
        }

        var (recognized, error) = await RecognizeRenderedPageAsync(pdf, pageNumber, cancellationToken);

        if (recognized is null)
        {
            builder.AddWarning($"{damage} and OCR failed: {error}");
            builder.AddSegment(pageNumber, SegmentKind.Page, repaired, TextExtractionMethod.Native);
            return;
        }

        var ocrQuality = BengaliText.Assess(recognized);

        // OCR can itself misread, so it has to earn its place: it must read more
        // cleanly than what the PDF carried, and it must actually have read Bangla.
        // The second test matters when the Bangla language data is missing - OCR
        // then returns Latin lookalikes, which contain no damaged Bangla at all.
        if (ocrQuality.BengaliWords * 2 >= nativeQuality.BengaliWords
            && ocrQuality.SuspectRatio <= nativeQuality.SuspectRatio)
        {
            builder.AddWarning($"{damage}, so the page was read by OCR instead.");
            builder.AddSegment(pageNumber, SegmentKind.Page, recognized, TextExtractionMethod.Ocr);
            return;
        }

        builder.AddWarning($"{damage}, and OCR did no better; the embedded text was kept.");
        builder.AddSegment(pageNumber, SegmentKind.Page, repaired, TextExtractionMethod.Native);
    }

    private async Task AddScannedPageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        Page page,
        string nativeText,
        CancellationToken cancellationToken)
    {
        if (!_ocr.IsAvailable)
        {
            builder.AddWarning(
                $"Page {page.Number} has little or no embedded text and OCR is unavailable: {_ocr.UnavailableReason}");
            builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
            return;
        }

        var (recognized, renderError) = await RecognizeRenderedPageAsync(pdf, page.Number, cancellationToken);

        if (recognized is null)
        {
            // Drawing the page failed; the embedded images are the next best
            // thing, since a scanned page is one full-page image.
            var (fromImages, failures) = await RecognizePageImagesAsync(page, cancellationToken);

            builder.AddWarning($"Page {page.Number}: {renderError}");

            foreach (var failure in failures)
            {
                builder.AddWarning($"Page {page.Number}: {failure}");
            }

            recognized = string.IsNullOrWhiteSpace(fromImages) ? null : fromImages;
        }

        if (recognized is null)
        {
            builder.AddWarning($"Page {page.Number} produced no text, either natively or by OCR.");
            builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
            return;
        }

        builder.AddSegment(page.Number, SegmentKind.Page, recognized, TextExtractionMethod.Ocr);
    }

    /// <returns>The recognised text, or null with the reason it could not be had.</returns>
    private async Task<(string? Text, string? Error)> RecognizeRenderedPageAsync(
        byte[] pdf,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var rendered = _renderer.RenderPng(pdf, pageNumber);

        if (!rendered.Succeeded)
        {
            return (null, rendered.Error);
        }

        var result = await _ocr.RecognizeAsync(rendered.Png!, cancellationToken);

        if (!result.Succeeded)
        {
            return (null, result.Error ?? "OCR failed.");
        }

        return string.IsNullOrWhiteSpace(result.Text)
            ? (null, "OCR found no text on the page.")
            : (result.Text.Trim(), null);
    }

    private async Task<(string Text, IReadOnlyList<string> Failures)> RecognizePageImagesAsync(
        Page page,
        CancellationToken cancellationToken)
    {
        var recognized = new List<string>();
        var failures = new List<string>();
        var imageCount = 0;

        foreach (var image in page.GetImages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            imageCount++;

            if (!image.TryGetPng(out var png) || png is null || png.Length == 0)
            {
                failures.Add("an embedded image could not be decoded to PNG.");
                continue;
            }

            var result = await _ocr.RecognizeAsync(png, cancellationToken);

            if (!result.Succeeded)
            {
                failures.Add(result.Error ?? "OCR failed.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                recognized.Add(result.Text.Trim());
            }
        }

        if (imageCount == 0)
        {
            failures.Add("no embedded images to recognise.");
        }

        return (string.Join("\n", recognized), failures);
    }

    /// <summary>
    /// Counts characters that actually convey text, so a page holding nothing but
    /// newlines and spaces is correctly seen as empty.
    /// </summary>
    private static int CountVisibleCharacters(string text)
    {
        var count = 0;

        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character) && !char.IsControl(character))
            {
                count++;
            }
        }

        return count;
    }
}
