using System.Globalization;
using SmartPlagiarism.Engine.Ocr;
using SmartPlagiarism.Engine.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Extracts text from PDFs with PdfPig, turning to OCR of the drawn page where
/// the embedded text cannot be trusted or does not hold the page's words.
///
/// Each page takes one of four routes:
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
///   <item><description>
///     <b>Mostly a picture</b> - a heading over a pasted screenshot or scan has a
///     text layer, but the page's words are in the picture. Such a page is drawn
///     and read by OCR, and the result kept if it found the picture's words.
///   </description></item>
/// </list>
/// No route throws: when OCR is unavailable the best text there is gets used,
/// and a warning records exactly what was lost so a teacher is never shown a
/// low score without knowing why.
///
/// Once every page is read, lines repeated at the top or bottom of most pages -
/// running headers, footers and page numbers - are removed, as Word keeps them
/// out of a DOCX's body. Left in, they match between every report made from the
/// same template.
/// </summary>
public sealed class PdfTextExtractor : ITextExtractor
{
    /// <summary>
    /// A page with fewer visible characters than this is treated as having no text
    /// layer. Around twenty characters is below any real paragraph but above the
    /// stray page number or header that scanners often leave behind.
    /// </summary>
    public const int MinimumNativeCharacters = 20;

    /// <summary>
    /// A page with a text layer shorter than this, most of it covered by images,
    /// is treated as a picture with a caption: a heading, not a page of prose.
    /// </summary>
    public const int MaximumCaptionCharacters = 300;

    /// <summary>Share of the page images must cover for it to count as mostly a picture.</summary>
    public const double MinimumPictureCoverage = 0.5;

    /// <summary>
    /// Words OCR must find beyond the text layer's for its reading of a
    /// picture page to replace the text layer. Fewer is a logo or a chart's
    /// axis labels, which are not worth trading exact text for.
    /// </summary>
    public const int MinimumPictureWords = 20;

    private readonly IOcrService _ocr;
    private readonly IPdfPageRenderer _renderer;
    private readonly ExtractionLimits _limits;

    public PdfTextExtractor(IOcrService ocr, IPdfPageRenderer renderer, ExtractionLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(renderer);

        _ocr = ocr;
        _renderer = renderer;
        _limits = limits ?? new ExtractionLimits();
    }

    public IReadOnlyCollection<string> SupportedExtensions { get; } = [".pdf"];

    public async Task<TextExtractionResult> ExtractAsync(
        Stream document,
        CancellationToken cancellationToken = default)
    {
        var bytes = await StreamHelpers.ReadAllBytesAsync(document, cancellationToken);
        var builder = new TextExtractionResultBuilder();
        var budget = new OcrBudget(_limits.OcrTimeBudget);

        using var pdf = PdfDocument.Open(bytes);

        var pageCount = Math.Min(pdf.NumberOfPages, _limits.MaxPdfPages);
        var pages = new List<PageText>(pageCount);

        for (var number = 1; number <= pageCount; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pages.Add(await ReadPageAsync(builder, bytes, pdf.GetPage(number), budget, cancellationToken));
        }

        var texts = PdfRunningLines.Remove(pages.Select(page => page.Text).ToList());

        for (var index = 0; index < pages.Count; index++)
        {
            builder.AddSegment(pages[index].Number, SegmentKind.Page, texts[index], pages[index].Method);
        }

        if (pdf.NumberOfPages == 0)
        {
            builder.AddWarning("The PDF contains no pages.");
        }

        if (pdf.NumberOfPages > pageCount)
        {
            builder.AddWarning(string.Create(
                CultureInfo.InvariantCulture,
                $"The PDF has {pdf.NumberOfPages:N0} pages; only the first {pageCount:N0} were read."));
        }

        if (budget.Warning() is { } unread)
        {
            builder.AddWarning(unread);
        }

        return builder.Build();
    }

    private async Task<PageText> ReadPageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        Page page,
        OcrBudget budget,
        CancellationToken cancellationToken)
    {
        var nativeText = NativeText(page);
        var visibleCharacters = CountVisibleCharacters(nativeText);

        if (visibleCharacters < MinimumNativeCharacters)
        {
            return await ReadScannedPageAsync(builder, pdf, page, nativeText, budget, cancellationToken);
        }

        if (visibleCharacters < MaximumCaptionCharacters
            && ImageCoverage(page) >= MinimumPictureCoverage
            && await ReadPicturePageAsync(builder, pdf, page.Number, nativeText, budget, cancellationToken) is { } picture)
        {
            return picture;
        }

        return BengaliText.ContainsBengali(nativeText)
            ? await ReadBengaliPageAsync(builder, pdf, page.Number, nativeText, budget, cancellationToken)
            : new PageText(page.Number, nativeText, TextExtractionMethod.Native);
    }

    private async Task<PageText> ReadBengaliPageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        int pageNumber,
        string nativeText,
        OcrBudget budget,
        CancellationToken cancellationToken)
    {
        var repaired = BengaliText.RepairDrawingOrder(nativeText);
        var nativeQuality = BengaliText.Assess(repaired);
        var native = new PageText(pageNumber, repaired, TextExtractionMethod.Native);

        if (!nativeQuality.IsDamaged)
        {
            return native;
        }

        var damage = $"Page {pageNumber}: the PDF's embedded Bangla text is damaged "
            + $"({nativeQuality.SuspectWords} of {nativeQuality.BengaliWords} words unreadable, "
            + $"{nativeQuality.ConjunctRatio:P0} with conjuncts)";

        if (!_ocr.IsAvailable)
        {
            builder.AddWarning(
                $"{damage} and OCR is unavailable ({_ocr.UnavailableReason}). "
                + "Similarity for this page will be understated; a DOCX or PPTX copy gives exact results.");
            return native;
        }

        if (!budget.HasTimeLeft)
        {
            budget.SkipPage(pageNumber);
            return native;
        }

        var (recognized, error) = await budget.MeasureAsync(() => RecognizeRenderedPageAsync(pdf, pageNumber, cancellationToken));

        if (recognized is null)
        {
            builder.AddWarning($"{damage} and OCR failed: {error}");
            return native;
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
            return new PageText(pageNumber, recognized, TextExtractionMethod.Ocr);
        }

        builder.AddWarning($"{damage}, and OCR did no better; the embedded text was kept.");
        return native;
    }

    private async Task<PageText> ReadScannedPageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        Page page,
        string nativeText,
        OcrBudget budget,
        CancellationToken cancellationToken)
    {
        var native = new PageText(page.Number, nativeText, TextExtractionMethod.Native);

        if (!_ocr.IsAvailable)
        {
            builder.AddWarning(
                $"Page {page.Number} has little or no embedded text and OCR is unavailable: {_ocr.UnavailableReason}");
            return native;
        }

        if (!budget.HasTimeLeft)
        {
            budget.SkipPage(page.Number);
            return native;
        }

        var (recognized, renderError) = await budget.MeasureAsync(
            () => RecognizeRenderedPageAsync(pdf, page.Number, cancellationToken));

        if (recognized is null)
        {
            // Drawing the page failed; the embedded images are the next best
            // thing, since a scanned page is one full-page image.
            var (fromImages, failures) = await budget.MeasureAsync(() => RecognizePageImagesAsync(page, cancellationToken));

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
            return native;
        }

        return new PageText(page.Number, recognized, TextExtractionMethod.Ocr);
    }

    /// <returns>
    /// The page as OCR read it when that found the picture's words, or null to
    /// read the text layer as usual.
    /// </returns>
    private async Task<PageText?> ReadPicturePageAsync(
        TextExtractionResultBuilder builder,
        byte[] pdf,
        int pageNumber,
        string nativeText,
        OcrBudget budget,
        CancellationToken cancellationToken)
    {
        if (!_ocr.IsAvailable)
        {
            builder.AddWarning(
                $"Page {pageNumber} is mostly a picture and OCR is unavailable ({_ocr.UnavailableReason}), "
                + "so any text inside the picture is not read.");
            return null;
        }

        if (!budget.HasTimeLeft)
        {
            budget.SkipPage(pageNumber);
            return null;
        }

        var (recognized, error) = await budget.MeasureAsync(() => RecognizeRenderedPageAsync(pdf, pageNumber, cancellationToken));

        if (recognized is null)
        {
            builder.AddWarning($"Page {pageNumber} is mostly a picture, and reading it by OCR failed: {error}");
            return null;
        }

        // The drawn page includes the text layer's own words, so OCR has found
        // the picture's text only if it read clearly more than those.
        return WordTokenizer.Tokenize(recognized).Count >= WordTokenizer.Tokenize(nativeText).Count + MinimumPictureWords
            ? new PageText(pageNumber, recognized, TextExtractionMethod.Ocr)
            : null;
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

            // The PDF declares the size before anything is decoded, and decoding is
            // what costs the memory.
            if ((long)image.WidthInSamples * image.HeightInSamples > _limits.MaxPicturePixels)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"an embedded image of {image.WidthInSamples:N0} x {image.HeightInSamples:N0} pixels is larger than "
                    + $"the {_limits.MaxPicturePixels / 1_000_000.0:0.##}-megapixel limit for OCR, so it was not read."));
                continue;
            }

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
    /// The share of the page its images cover, capped at 1. Overlapping images
    /// are counted twice, which only matters for pages that are pictures anyway.
    /// </summary>
    private static double ImageCoverage(Page page)
    {
        var pageArea = page.Width * page.Height;

        if (pageArea <= 0)
        {
            return 0;
        }

        try
        {
            var covered = page.GetImages().Sum(image => Math.Abs(image.BoundingBox.Width * image.BoundingBox.Height));
            return Math.Min(1, covered / pageArea);
        }
        catch (Exception)
        {
            // Broad on purpose: an image PdfPig cannot parse only means the page
            // is not known to be a picture, and must not stop it being read.
            return 0;
        }
    }

    /// <summary>
    /// The page's embedded text, with the word and line breaks the PDF does not
    /// store put back (see <see cref="PdfPageLayout"/>), and anything set in a
    /// Bijoy font converted to Unicode. A PDF made from a SutonnyMJ document
    /// stores the Latin codes the font draws as Bangla, so the text layer reads
    /// "Avwg" for আমি; each letter records its font, which is what identifies
    /// those stretches.
    /// </summary>
    private static string NativeText(Page page) => BijoyConverter.Join(PdfPageLayout.Runs(page));

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

    private sealed record PageText(int Number, string Text, TextExtractionMethod Method);
}
