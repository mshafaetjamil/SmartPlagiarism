using SmartPlagiarism.Engine.Ocr;

namespace SmartPlagiarism.Engine.Extraction;

/// <summary>Chooses the extractor for a document, by extension.</summary>
public interface IExtractorFactory
{
    IReadOnlyCollection<string> SupportedExtensions { get; }

    /// <param name="fileNameOrExtension">A filename or a bare extension; either works.</param>
    /// <returns>The matching extractor, or null when the format is not supported.</returns>
    ITextExtractor? Find(string? fileNameOrExtension);
}

/// <inheritdoc cref="IExtractorFactory"/>
public sealed class ExtractorFactory : IExtractorFactory
{
    private readonly Dictionary<string, ITextExtractor> _byExtension;

    public ExtractorFactory(IEnumerable<ITextExtractor> extractors)
    {
        ArgumentNullException.ThrowIfNull(extractors);

        _byExtension = new Dictionary<string, ITextExtractor>(StringComparer.OrdinalIgnoreCase);

        foreach (var extractor in extractors)
        {
            foreach (var extension in extractor.SupportedExtensions)
            {
                _byExtension[extension] = extractor;
            }
        }
    }

    /// <summary>
    /// The three built-in extractors, for tests and for hosts not using a
    /// dependency-injection container.
    /// </summary>
    public static ExtractorFactory CreateDefault(IOcrService ocr) =>
        new([new PdfTextExtractor(ocr), new DocxTextExtractor(), new PptxTextExtractor()]);

    public IReadOnlyCollection<string> SupportedExtensions => _byExtension.Keys;

    public ITextExtractor? Find(string? fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return null;
        }

        // Path.GetExtension returns "" for a bare ".pdf", so fall back to the input.
        var extension = Path.GetExtension(fileNameOrExtension);

        if (string.IsNullOrEmpty(extension))
        {
            extension = fileNameOrExtension.StartsWith('.') ? fileNameOrExtension : $".{fileNameOrExtension}";
        }

        return _byExtension.GetValueOrDefault(extension);
    }
}
