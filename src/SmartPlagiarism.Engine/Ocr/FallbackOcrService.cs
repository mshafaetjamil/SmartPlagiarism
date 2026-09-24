namespace SmartPlagiarism.Engine.Ocr;

/// <summary>
/// Uses the first OCR service that can run on this machine.
///
/// The in-process library is tried first because it avoids starting a process
/// per page, but it only loads on Windows; the command-line program covers
/// everywhere else. When none can run, the reason names every one that was
/// tried, so the setup problem is obvious from a single warning.
/// </summary>
public sealed class FallbackOcrService : IOcrService, IDisposable
{
    private readonly IReadOnlyList<IOcrService> _services;
    private readonly Lazy<IOcrService?> _selected;

    public FallbackOcrService(IReadOnlyList<IOcrService> services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Count == 0)
        {
            throw new ArgumentException("At least one OCR service is required.", nameof(services));
        }

        _services = services;
        _selected = new Lazy<IOcrService?>(
            () => _services.FirstOrDefault(service => service.IsAvailable),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _selected.Value is not null;

    public string? UnavailableReason => _selected.Value is null
        ? string.Join("; ", _services.Select(service => service.UnavailableReason))
        : null;

    public Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default) =>
        _selected.Value is { } service
            ? service.RecognizeAsync(imageBytes, cancellationToken)
            : Task.FromResult(OcrResult.Failure(UnavailableReason!));

    public void Dispose()
    {
        foreach (var service in _services.OfType<IDisposable>())
        {
            service.Dispose();
        }
    }
}
