namespace SmartPlagiarism.Core.Files;

/// <summary>
/// The upload whitelist. Anything not listed here is rejected.
/// </summary>
public static class AllowedFileFormats
{
    // "%PDF-"
    private static readonly byte[] s_pdfSignature = [0x25, 0x50, 0x44, 0x46, 0x2D];

    // "PK\x03\x04" - the local file header every ZIP archive opens with. DOCX and
    // PPTX are both OOXML, i.e. ZIP containers, so they share this signature.
    private static readonly byte[] s_zipSignature = [0x50, 0x4B, 0x03, 0x04];

    public static AllowedFileFormat Pdf { get; } = new(
        ".pdf",
        "application/pdf",
        [s_pdfSignature]);

    public static AllowedFileFormat Docx { get; } = new(
        ".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [s_zipSignature]);

    public static AllowedFileFormat Pptx { get; } = new(
        ".pptx",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [s_zipSignature]);

    public static IReadOnlyList<AllowedFileFormat> All { get; } = [Pdf, Docx, Pptx];

    /// <summary>Human-readable list for validation messages, e.g. "PDF, DOCX, PPTX".</summary>
    public static string DisplayList { get; } =
        string.Join(", ", All.Select(format => format.Extension.TrimStart('.').ToUpperInvariant()));

    /// <summary>
    /// Office's pre-2007 binary formats, and what to do with them. They are not
    /// read: their text sits in a proprietary binary structure no maintained
    /// .NET library parses reliably, while every Word and PowerPoint since 2007
    /// saves the XML-based format in one step.
    /// </summary>
    public static string? LegacyFormatAdvice(string? extension) => extension?.ToLowerInvariant() switch
    {
        ".doc" => "is in the old Word format (.doc). Open it in Word, choose File > Save As > Word Document (.docx), and upload that.",
        ".ppt" => "is in the old PowerPoint format (.ppt). Open it in PowerPoint, choose File > Save As > PowerPoint Presentation (.pptx), and upload that.",
        _ => null,
    };

    /// <summary>Finds the format for an extension, or null when it is not allowed.</summary>
    public static AllowedFileFormat? Find(string? extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? null
            : All.FirstOrDefault(format =>
                string.Equals(format.Extension, extension, StringComparison.OrdinalIgnoreCase));
}
