using FluentAssertions;
using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Tests.Ocr;

/// <summary>
/// The command-line service and the fallback that chooses between it and the
/// in-process library. As with the library, nothing here may throw: a missing
/// program or language must come back as a reason.
/// </summary>
public class OcrSelectionTests
{
    [Fact]
    public void Cli_ConfiguredProgramThatDoesNotExist_ReportsIt()
    {
        using var ocr = new TesseractCliOcrService(new OcrOptions
        {
            TesseractExecutable = Path.Combine(Path.GetTempPath(), "no-such-tesseract-" + Guid.NewGuid().ToString("N")),
        });

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Contain("does not exist");
    }

    [Fact]
    public void Cli_DisabledByConfiguration_ReportsThat()
    {
        using var ocr = new TesseractCliOcrService(new OcrOptions { Enabled = false });

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Contain("disabled by configuration");
    }

    [Fact]
    public void Cli_LanguageNobodyHas_IsUnavailableWithAReason()
    {
        // Whether or not Tesseract is installed here, a language that does not
        // exist cannot be available - and saying why must not throw.
        using var ocr = new TesseractCliOcrService(new OcrOptions { Language = "zz-not-a-language" });

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Cli_WhenUnavailable_ReturnsFailureInsteadOfThrowing()
    {
        using var ocr = new TesseractCliOcrService(new OcrOptions { Enabled = false });

        var result = await ocr.RecognizeAsync([1, 2, 3]);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Fallback_UsesTheFirstServiceThatIsAvailable()
    {
        var ocr = new FallbackOcrService(
        [
            new NullOcrService("library missing"),
            new ScriptedOcrService("recognised"),
        ]);

        ocr.IsAvailable.Should().BeTrue();
        ocr.UnavailableReason.Should().BeNull();
        (await ocr.RecognizeAsync([1])).Text.Should().Be("recognised");
    }

    [Fact]
    public async Task Fallback_WhenNothingIsAvailable_NamesEveryReason()
    {
        var ocr = new FallbackOcrService(
        [
            new NullOcrService("library missing"),
            new NullOcrService("program not installed"),
        ]);

        ocr.IsAvailable.Should().BeFalse();
        ocr.UnavailableReason.Should().Contain("library missing").And.Contain("program not installed");

        var result = await ocr.RecognizeAsync([1]);
        result.Succeeded.Should().BeFalse();
    }
}
