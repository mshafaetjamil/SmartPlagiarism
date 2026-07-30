using SmartPlagiarism.Core.Abstractions;

namespace SmartPlagiarism.Core.Files;

/// <summary>
/// <inheritdoc cref="IFileUploadValidator"/>
///
/// Checks run cheapest-first, so an oversized file is rejected without reading a
/// byte of it.
///
/// Known limit: DOCX and PPTX are both ZIP containers and share the same leading
/// bytes, so a DOCX renamed to .pptx passes this check. Distinguishing them means
/// opening the archive and inspecting its parts, which is Phase 4's extraction
/// step - and that fails loudly on a mismatch.
/// </summary>
public class FileUploadValidator : IFileUploadValidator
{
    public FileValidationResult Validate(string? fileName, long sizeBytes, Stream content)
    {
        ArgumentNullException.ThrowIfNull(content);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            return FileValidationResult.Invalid("A file name is required.");
        }

        // GetFileName strips any directory component, so "../../etc/passwd" becomes
        // "passwd". Belt and braces: the client name is never used to build a path.
        var safeFileName = Path.GetFileName(fileName);

        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return FileValidationResult.Invalid("A file name is required.");
        }

        if (safeFileName.Length > UploadConstraints.MaxFileNameLength)
        {
            return FileValidationResult.Invalid(
                $"File names must be {UploadConstraints.MaxFileNameLength} characters or fewer.");
        }

        if (sizeBytes <= 0)
        {
            return FileValidationResult.Invalid($"'{safeFileName}' is empty.");
        }

        if (sizeBytes > UploadConstraints.MaxFileSizeBytes)
        {
            return FileValidationResult.Invalid(
                $"'{safeFileName}' is larger than the {UploadConstraints.MaxFileSizeBytes / (1024 * 1024)} MB limit.");
        }

        var format = AllowedFileFormats.Find(Path.GetExtension(safeFileName));

        if (format is null)
        {
            return FileValidationResult.Invalid(
                $"'{safeFileName}' is not an accepted file type. Allowed types: {AllowedFileFormats.DisplayList}.");
        }

        if (!StartsWithKnownSignature(content, format))
        {
            return FileValidationResult.Invalid(
                $"The contents of '{safeFileName}' are not a valid {format.Extension.TrimStart('.').ToUpperInvariant()} file.");
        }

        return FileValidationResult.Valid(format, safeFileName);
    }

    private static bool StartsWithKnownSignature(Stream content, AllowedFileFormat format)
    {
        if (!content.CanSeek)
        {
            throw new ArgumentException("Upload streams must be seekable to be validated.", nameof(content));
        }

        var originalPosition = content.Position;

        try
        {
            var header = new byte[format.MaxSignatureLength];
            content.Seek(0, SeekOrigin.Begin);

            // A file shorter than the signature simply cannot match.
            var bytesRead = content.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);

            return format.Signatures.Any(signature =>
                bytesRead >= signature.Length
                && header.AsSpan(0, signature.Length).SequenceEqual(signature));
        }
        finally
        {
            content.Seek(originalPosition, SeekOrigin.Begin);
        }
    }
}
