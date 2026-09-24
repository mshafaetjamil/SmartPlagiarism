namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Gives the pipeline access to the corpus without the Engine knowing how it is
/// stored. Implemented by Infrastructure against the database.
/// </summary>
public interface ICorpusReader
{
    /// <summary>
    /// Documents whose file contents hash to exactly this value.
    ///
    /// This is the cheapest possible check and runs before anything else: an
    /// identical file needs no analysis, only an index lookup.
    /// </summary>
    Task<IReadOnlyList<int>> FindByContentHashAsync(
        string contentHash,
        IReadOnlyCollection<int> excludeDocumentIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a candidate's stored text for detailed comparison, or null if it has
    /// no extracted text. Called only for shortlisted documents.
    /// </summary>
    Task<AnalyzedDocument?> LoadAsync(int documentId, CancellationToken cancellationToken = default);
}
