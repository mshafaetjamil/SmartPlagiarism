using FluentAssertions;
using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Ocr;

/// <summary>
/// OCR is optional at runtime: the Tesseract wrapper ships no macOS native
/// library, tessdata may not be downloaded, and an administrator may switch it
/// off. None of those may throw - the engine has to keep working without OCR and
/// say why it is missing.
/// </summary>
public class OcrGracefulDegradationTests
{
    private static TesseractOcrService Service(OcrOptions options) => new(options);

    [Fact]
    public void MissingTessDataDirectory_ReportsUnavailableWithAReason()
    {
        using var ocr = Service(new OcrOptions
        {
            Enabled = true,
            TessDataPath = Path.Combine(Path.GetTempPath(), "definitely-not-here-" + Guid.NewGuid().ToString("N")),
        });

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Contain("tessdata").And.Contain("README");
    }

    [Fact]
    public void TessDataDirectoryWithoutTheLanguageFile_ReportsWhichFileIsMissing()
    {
        var emptyTessData = Path.Combine(Path.GetTempPath(), "tessdata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyTessData);

        try
        {
            using var ocr = Service(new OcrOptions { Enabled = true, TessDataPath = emptyTessData });

            ocr.IsAvailable.Should().BeFalse();
            ocr.UnavailableReason.Should().Contain("eng.traineddata");
        }
        finally
        {
            Directory.Delete(emptyTessData, recursive: true);
        }
    }

    [Fact]
    public void DisabledByConfiguration_ReportsThatRatherThanAMissingFile()
    {
        using var ocr = Service(new OcrOptions { Enabled = false, TessDataPath = Path.GetTempPath() });

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Contain("disabled by configuration");
    }

    [Fact]
    public async Task RecognizeAsync_WhenUnavailable_ReturnsFailureInsteadOfThrowing()
    {
        using var ocr = Service(new OcrOptions { Enabled = false });

        var result = await ocr.RecognizeAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.Text.Should().BeEmpty();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NullOcrService_BehavesTheSameWay()
    {
        var ocr = new NullOcrService("tests run without OCR");

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Be("tests run without OCR");

        var result = await ocr.RecognizeAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be("tests run without OCR");
    }

    [Fact]
    public void ProbingIsSafeToRepeat()
    {
        using var ocr = Service(new OcrOptions { Enabled = false });

        // IsAvailable triggers the lazy probe; asking twice must not re-probe or throw.
        ocr.IsAvailable.Should().BeFalse();
        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().NotBeNullOrWhiteSpace();
    }
}
