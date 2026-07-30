namespace SmartPlagiarism.Engine.Extraction;

/// <summary>
/// Pulls text out of one document format.
///
/// Implementations report problems through
/// <see cref="TextExtractionResult.Warnings"/> and only throw when the document
/// itself cannot be opened at all.
/// </summary>
public interface ITextExtractor
{
    /// <summary>Lowercase extensions, including the dot.</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <param name="document">The document's bytes. Not disposed by the extractor.</param>
    Task<TextExtractionResult> ExtractAsync(Stream document, CancellationToken cancellationToken = default);
}
