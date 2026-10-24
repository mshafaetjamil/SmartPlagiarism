using Tesseract;

namespace SmartPlagiarism.Engine.Ocr;

/// <summary>
/// Tesseract-backed OCR.
///
/// Everything about Tesseract is optional at runtime: the native library may be
/// missing for the current platform, the tessdata directory may not exist, or the
/// language file may not have been downloaded. None of those may take extraction
/// down, so initialisation is probed once, lazily, and any failure is recorded as
/// a reason rather than thrown.
///
/// The library has no way to cancel or time-limit recognition, so each image runs
/// through a <see cref="TimeLimitedEngine{TEngine}"/>, which gives up on one that
/// takes longer than <see cref="OcrOptions.PageTimeout"/>.
/// </summary>
public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly OcrOptions _options;

    // TesseractEngine is not thread-safe, and analysis runs several documents at
    // once, so recognition is serialised through the time-limited engine.
    private readonly Lazy<TimeLimitedEngine<TesseractEngine>?> _engine;

    private string? _unavailableReason;
    private bool _disposed;

    public TesseractOcrService(OcrOptions options)
    {
        _options = options;
        _engine = new Lazy<TimeLimitedEngine<TesseractEngine>?>(TryCreateEngine, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _engine.Value is not null;

    public string? UnavailableReason
    {
        get
        {
            _ = _engine.Value;
            return _unavailableReason;
        }
    }

    public async Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var engine = _engine.Value;

        if (engine is null)
        {
            return OcrResult.Failure(_unavailableReason ?? "OCR is unavailable.");
        }

        if (imageBytes.Length == 0)
        {
            return OcrResult.Failure("The page image was empty.");
        }

        try
        {
            var text = await engine.RunAsync(
                tesseract =>
                {
                    using var image = Pix.LoadFromMemory(imageBytes);
                    using var page = tesseract.Process(image);

                    return page.GetText() ?? string.Empty;
                },
                cancellationToken);

            return OcrResult.Success(text);
        }
        catch (TimeoutException)
        {
            return OcrResult.TimedOut(_options.PageTimeout);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown, not an unreadable image.
            throw;
        }
        catch (Exception ex)
        {
            // One unreadable image must not abort the document. The caller turns
            // this into a warning against the page it came from.
            return OcrResult.Failure($"Tesseract could not read the page image: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns null - never throws - when OCR cannot be set up, recording why.
    /// </summary>
    private TimeLimitedEngine<TesseractEngine>? TryCreateEngine()
    {
        if (!_options.Enabled)
        {
            _unavailableReason = "OCR is disabled by configuration.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.TessDataPath) || !Directory.Exists(_options.TessDataPath))
        {
            _unavailableReason =
                $"the tessdata directory '{_options.TessDataPath}' does not exist (see SETUP.md)";
            return null;
        }

        var missing = _options.Languages
            .Where(language => !File.Exists(Path.Combine(_options.TessDataPath, $"{language}.traineddata")))
            .Select(language => $"'{language}.traineddata'")
            .ToList();

        if (missing.Count > 0)
        {
            _unavailableReason = $"{string.Join(", ", missing)} missing from '{_options.TessDataPath}' (see SETUP.md)";
            return null;
        }

        try
        {
            return new TimeLimitedEngine<TesseractEngine>(NewEngine(), NewEngine, _options.PageTimeout);
        }
        catch (Exception ex)
        {
            // Broad on purpose: a missing native library surfaces as
            // DllNotFoundException or TypeInitializationException depending on
            // platform, and the Tesseract wrapper ships no macOS binary at all.
            _unavailableReason = $"Tesseract could not start: {ex.Message}";
            return null;
        }
    }

    private TesseractEngine NewEngine() => new(_options.TessDataPath, _options.Language, EngineMode.Default);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_engine.IsValueCreated)
        {
            _engine.Value?.Dispose();
        }
    }
}
