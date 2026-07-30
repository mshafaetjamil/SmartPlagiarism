using System.Text;
using FluentAssertions;
using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Tests.Files;

/// <summary>
/// The upload whitelist is the boundary between the internet and this system's
/// disk, so every rejection rule is pinned down here.
/// </summary>
public class FileUploadValidatorTests
{
    private readonly FileUploadValidator _validator = new();

    // Real leading bytes for each accepted format, followed by filler.
    private static MemoryStream PdfBytes() => Stream("%PDF-1.7\n%âãÏÓ\nbody");

    private static MemoryStream ZipBytes() => new([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00]);

    private static MemoryStream Stream(string content) => new(Encoding.Latin1.GetBytes(content));

    // ---------------- accepted ----------------

    [Fact]
    public void Validate_GenuinePdf_IsAccepted()
    {
        using var content = PdfBytes();

        var result = _validator.Validate("report.pdf", content.Length, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.Format.Should().BeSameAs(AllowedFileFormats.Pdf);
        result.SafeFileName.Should().Be("report.pdf");
    }

    [Fact]
    public void Validate_GenuineDocx_IsAccepted()
    {
        using var content = ZipBytes();

        var result = _validator.Validate("thesis.docx", content.Length, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.Format.Should().BeSameAs(AllowedFileFormats.Docx);
    }

    [Fact]
    public void Validate_GenuinePptx_IsAccepted()
    {
        using var content = ZipBytes();

        var result = _validator.Validate("defence.pptx", content.Length, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.Format.Should().BeSameAs(AllowedFileFormats.Pptx);
    }

    [Theory]
    [InlineData("REPORT.PDF")]
    [InlineData("Report.Pdf")]
    public void Validate_ExtensionCasing_IsIgnored(string fileName)
    {
        using var content = PdfBytes();

        var result = _validator.Validate(fileName, content.Length, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    [Fact]
    public void Validate_LeavesTheStreamPositionUntouched()
    {
        using var content = PdfBytes();
        content.Position = 3;

        _validator.Validate("report.pdf", content.Length, content);

        content.Position.Should().Be(3, "callers may be mid-read and must not be disturbed");
    }

    // ---------------- extension whitelist ----------------

    [Theory]
    [InlineData("malware.exe")]
    [InlineData("notes.txt")]
    [InlineData("archive.zip")]
    [InlineData("script.pdf.js")]
    [InlineData("noextension")]
    public void Validate_ExtensionOutsideTheWhitelist_IsRejected(string fileName)
    {
        using var content = PdfBytes();

        var result = _validator.Validate(fileName, content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not an accepted file type");
    }

    // ---------------- magic bytes ----------------

    [Fact]
    public void Validate_ZipContentWearingAPdfExtension_IsRejected()
    {
        using var content = ZipBytes();

        var result = _validator.Validate("disguised.pdf", content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not a valid PDF");
    }

    [Fact]
    public void Validate_PdfContentWearingADocxExtension_IsRejected()
    {
        using var content = PdfBytes();

        var result = _validator.Validate("disguised.docx", content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not a valid DOCX");
    }

    [Fact]
    public void Validate_ExecutableRenamedToPdf_IsRejected()
    {
        // "MZ" - a Windows executable.
        using var content = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);

        var result = _validator.Validate("coursework.pdf", content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not a valid PDF");
    }

    [Fact]
    public void Validate_FileShorterThanTheSignature_IsRejected()
    {
        using var content = new MemoryStream([0x25, 0x50]);

        var result = _validator.Validate("truncated.pdf", content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not a valid PDF");
    }

    // ---------------- size ----------------

    [Fact]
    public void Validate_ZeroByteFile_IsRejected()
    {
        using var content = new MemoryStream();

        var result = _validator.Validate("empty.pdf", 0, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("is empty");
    }

    [Fact]
    public void Validate_FileOverTheSizeLimit_IsRejected()
    {
        using var content = PdfBytes();

        // The declared size is what is checked, so this needs no 25 MB allocation.
        var result = _validator.Validate("huge.pdf", UploadConstraints.MaxFileSizeBytes + 1, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("larger than the 25 MB limit");
    }

    [Fact]
    public void Validate_FileExactlyAtTheSizeLimit_IsAccepted()
    {
        using var content = PdfBytes();

        var result = _validator.Validate("borderline.pdf", UploadConstraints.MaxFileSizeBytes, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
    }

    // ---------------- file name ----------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_MissingFileName_IsRejected(string? fileName)
    {
        using var content = PdfBytes();

        var result = _validator.Validate(fileName, content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("file name is required");
    }

    [Fact]
    public void Validate_OverlongFileName_IsRejected()
    {
        using var content = PdfBytes();
        var fileName = new string('a', UploadConstraints.MaxFileNameLength) + ".pdf";

        var result = _validator.Validate(fileName, content.Length, content);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("characters or fewer");
    }

    [Theory]
    [InlineData("../../../etc/passwd.pdf", "passwd.pdf")]
    [InlineData("/absolute/path/report.pdf", "report.pdf")]
    [InlineData("subdir/report.pdf", "report.pdf")]
    public void Validate_StripsAnyDirectoryComponentFromTheClientName(string fileName, string expected)
    {
        using var content = PdfBytes();

        var result = _validator.Validate(fileName, content.Length, content);

        result.IsValid.Should().BeTrue(result.ErrorMessage);
        result.SafeFileName.Should().Be(expected, "a traversal attempt must never survive into storage");
    }
}
