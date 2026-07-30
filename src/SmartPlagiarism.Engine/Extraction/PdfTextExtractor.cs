using SmartPlagiarism.Engine.Ocr;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Extracts text from PDFs with PdfPig, falling back to OCR on pages that carry
/// no usable text layer.
///
/// On the fallback: PdfPig reads PDFs but cannot render them, and CLAUDE.md limits
/// this project to PdfPig, the Open XML SDK and Tesseract - so pulling in a
/// rasteriser such as PDFium is not an option. Instead the page's embedded images
/// are handed to OCR, which is exactly what a scanned page is: one full-page
/// image. The gap is a page whose content is vector drawing rather than an image;
/// that yields nothing and is reported as a warning rather than failing.
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

    public PdfTextExtractor(IOcrService ocr)
    {
        _ocr = ocr;
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

            if (CountVisibleCharacters(nativeText) >= MinimumNativeCharacters)
            {
                builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
                continue;
            }

            if (!_ocr.IsAvailable)
            {
                builder.AddWarning(
                    $"Page {page.Number} has little or no embedded text and OCR is unavailable: {_ocr.UnavailableReason}");
                builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
                continue;
            }

            var (recognized, failures) = await RecognizePageImagesAsync(page, cancellationToken);

            foreach (var failure in failures)
            {
                builder.AddWarning($"Page {page.Number}: {failure}");
            }

            if (string.IsNullOrWhiteSpace(recognized))
            {
                builder.AddWarning(
                    $"Page {page.Number} produced no text, either natively or by OCR.");
                builder.AddSegment(page.Number, SegmentKind.Page, nativeText, TextExtractionMethod.Native);
                continue;
            }

            builder.AddSegment(page.Number, SegmentKind.Page, recognized, TextExtractionMethod.Ocr);
        }

        if (pdf.NumberOfPages == 0)
        {
            builder.AddWarning("The PDF contains no pages.");
        }

        return builder.Build();
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
            failures.Add("no embedded images to recognise (a vector-only page cannot be OCRed without a rasteriser).");
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
