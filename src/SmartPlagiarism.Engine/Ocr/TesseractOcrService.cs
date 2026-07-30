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
/// </summary>
public sealed class TesseractOcrService : IOcrService, IDisposable
{
    private readonly OcrOptions _options;
    private readonly Lazy<TesseractEngine?> _engine;

    // TesseractEngine is not thread-safe, and analysis runs several documents at
    // once, so recognition is serialised through this.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _unavailableReason;
    private bool _disposed;

    public TesseractOcrService(OcrOptions options)
    {
        _options = options;
        _engine = new Lazy<TesseractEngine?>(TryCreateEngine, LazyThreadSafetyMode.ExecutionAndPublication);
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

        await _gate.WaitAsync(cancellationToken);

        try
        {
            using var image = Pix.LoadFromMemory(imageBytes);
            using var page = engine.Process(image);

            return OcrResult.Success(page.GetText() ?? string.Empty);
        }
        catch (Exception ex)
        {
            // One unreadable image must not abort the document. The caller turns
            // this into a warning against the page it came from.
            return OcrResult.Failure($"Tesseract could not read the page image: {ex.Message}");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns null - never throws - when OCR cannot be set up, recording why.
    /// </summary>
    private TesseractEngine? TryCreateEngine()
    {
        if (!_options.Enabled)
        {
            _unavailableReason = "OCR is disabled by configuration.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(_options.TessDataPath) || !Directory.Exists(_options.TessDataPath))
        {
            _unavailableReason =
                $"the tessdata directory '{_options.TessDataPath}' does not exist (see the README for setup)";
            return null;
        }

        var trainedDataFile = Path.Combine(_options.TessDataPath, $"{_options.Language}.traineddata");

        if (!File.Exists(trainedDataFile))
        {
            _unavailableReason =
                $"'{_options.Language}.traineddata' is missing from '{_options.TessDataPath}' (see the README for setup)";
            return null;
        }

        try
        {
            return new TesseractEngine(_options.TessDataPath, _options.Language, EngineMode.Default);
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

        _gate.Dispose();
    }
}
