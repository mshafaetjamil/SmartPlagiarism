using SmartPlagiarism.Core.Files;

namespace SmartPlagiarism.Core.Abstractions;

/// <summary>
/// Decides whether an uploaded file may be accepted. Pure: it touches no disk and
/// no database, so every rule is directly unit-testable.
/// </summary>
public interface IFileUploadValidator
{
    /// <param name="fileName">Client-supplied filename. Untrusted.</param>
    /// <param name="sizeBytes">Length reported by the transport.</param>
    /// <param name="content">
    /// Seekable stream over the file. Its position is restored before returning.
    /// </param>
    FileValidationResult Validate(string? fileName, long sizeBytes, Stream content);
}
