using System.Diagnostics;
using System.Text;

namespace SmartPlagiarism.Engine.Ocr;

/// <summary>
/// OCR through the <c>tesseract</c> command-line program.
///
/// The Tesseract NuGet package only ships a Windows library, so on macOS and
/// Linux the in-process <see cref="TesseractOcrService"/> cannot start. The
/// program itself installs everywhere in one command (<c>brew install
/// tesseract</c>, <c>apt install tesseract-ocr</c>), so running it as a child
/// process gives the same recognition on every platform. The image goes in on
/// stdin and the text comes back on stdout; nothing touches the disk.
///
/// Like the library service, availability is probed once and failure is
/// recorded as a reason, never thrown.
/// </summary>
public sealed class TesseractCliOcrService : IOcrService, IDisposable
{
    private static readonly TimeSpan s_probeTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Searched after PATH. A process launched from an IDE or as a service on macOS
    /// does not inherit the shell's PATH, so Homebrew's directory would otherwise
    /// be missed even with Tesseract installed.
    /// </summary>
    private static readonly string[] s_knownDirectories =
    [
        "/opt/homebrew/bin",
        "/usr/local/bin",
        "/usr/bin",
        @"C:\Program Files\Tesseract-OCR",
    ];

    private readonly OcrOptions _options;
    private readonly Lazy<string?> _executable;

    // One page at a time: Tesseract already spreads a page across cores, and
    // several at once would only contend for them.
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _unavailableReason;

    public TesseractCliOcrService(OcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _executable = new Lazy<string?>(Probe, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsAvailable => _executable.Value is not null;

    public string? UnavailableReason
    {
        get
        {
            _ = _executable.Value;
            return _unavailableReason;
        }
    }

    public async Task<OcrResult> RecognizeAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        var executable = _executable.Value;

        if (executable is null)
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
            return await RunAsync(executable, imageBytes, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<OcrResult> RunAsync(string executable, byte[] imageBytes, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.PageTimeout);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, ["stdin", "stdout", "-l", _options.Language, .. TessDataArguments()]),
        };

        try
        {
            process.Start();

            // Both streams are drained while the image is still being written, or a
            // full pipe on one side would deadlock the other.
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.StandardInput.BaseStream.WriteAsync(imageBytes, timeout.Token);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeout.Token);

            var text = await output;
            var errorText = await errors;

            return process.ExitCode == 0
                ? OcrResult.Success(text)
                : OcrResult.Failure($"Tesseract exited with code {process.ExitCode}: {FirstLine(errorText)}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Stop(process);
            return OcrResult.TimedOut(_options.PageTimeout);
        }
        catch (OperationCanceledException)
        {
            Stop(process);
            throw;
        }
        catch (Exception ex)
        {
            // A broken pipe, a crash mid-page: that page fails, the document goes on.
            Stop(process);
            return OcrResult.Failure($"Tesseract could not read the page image: {ex.Message}");
        }
    }

    /// <summary>
    /// Finds the program and confirms it has every configured language. Returns
    /// null - never throws - recording why when it cannot be used.
    /// </summary>
    private string? Probe()
    {
        if (!_options.Enabled)
        {
            _unavailableReason = "OCR is disabled by configuration.";
            return null;
        }

        var executable = FindExecutable();

        if (executable is null)
        {
            _unavailableReason = string.IsNullOrWhiteSpace(_options.TesseractExecutable)
                ? "the tesseract program is not installed (see SETUP.md)"
                : $"the configured tesseract program '{_options.TesseractExecutable}' does not exist";
            return null;
        }

        string listing;

        try
        {
            using var process = Process.Start(CreateStartInfo(executable, ["--list-langs", .. TessDataArguments()]))!;
            process.StandardInput.Close();

            // The listing is a few hundred bytes - far below a pipe buffer - so the
            // process can finish before its output is read, and waiting first is
            // what lets the timeout catch a program that hangs.
            if (!process.WaitForExit(s_probeTimeout))
            {
                Stop(process);
                _unavailableReason = "the tesseract program did not respond";
                return null;
            }

            listing = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        }
        catch (Exception ex)
        {
            _unavailableReason = $"the tesseract program could not be run: {ex.Message}";
            return null;
        }

        var installed = listing
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var missing = _options.Languages.Where(language => !installed.Contains(language)).ToList();

        if (missing.Count > 0)
        {
            _unavailableReason =
                $"tesseract has no language data for {string.Join(", ", missing)} (see SETUP.md)";
            return null;
        }

        return executable;
    }

    private string? FindExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_options.TesseractExecutable))
        {
            return File.Exists(_options.TesseractExecutable) ? _options.TesseractExecutable : null;
        }

        var fileName = OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract";
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(s_knownDirectories)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// The project's own tessdata when it exists, so the configured languages are
    /// the ones used; otherwise Tesseract's install-time data.
    /// </summary>
    private string[] TessDataArguments() =>
        !string.IsNullOrWhiteSpace(_options.TessDataPath) && Directory.Exists(_options.TessDataPath)
            ? ["--tessdata-dir", _options.TessDataPath]
            : [];

    private static ProcessStartInfo CreateStartInfo(string executable, IEnumerable<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void Stop(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Never started, or already gone - either way there is nothing to stop.
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

        return string.IsNullOrEmpty(line) ? "no error output" : line;
    }

    public void Dispose() => _gate.Dispose();
}
