namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Supplies how rare a word is across the corpus, so that shared uncommon words
/// count for more than shared common ones.
/// </summary>
public interface IInverseDocumentFrequency
{
    /// <summary>Weight for a term. Higher means rarer, and therefore more telling.</summary>
    double Weight(string term);
}

/// <summary>
/// IDF computed from real corpus counts, using the smoothed form
/// <c>ln((1 + N) / (1 + df)) + 1</c>.
///
/// The +1s matter: without them a term appearing in every document would have
/// weight 0 and drop out entirely, and a term the corpus has never seen would
/// divide by zero. The trailing +1 keeps every weight positive, so a shared term
/// always contributes something.
/// </summary>
public sealed class CorpusInverseDocumentFrequency : IInverseDocumentFrequency
{
    private readonly IReadOnlyDictionary<string, int> _documentFrequencies;
    private readonly int _documentCount;

    /// <param name="documentFrequencies">How many documents each term appears in.</param>
    /// <param name="documentCount">Documents the counts were taken over.</param>
    public CorpusInverseDocumentFrequency(
        IReadOnlyDictionary<string, int> documentFrequencies,
        int documentCount)
    {
        ArgumentNullException.ThrowIfNull(documentFrequencies);
        ArgumentOutOfRangeException.ThrowIfNegative(documentCount);

        _documentFrequencies = documentFrequencies;
        _documentCount = documentCount;
    }

    public double Weight(string term)
    {
        var documentFrequency = _documentFrequencies.GetValueOrDefault(term, 0);

        return Math.Log((1.0 + _documentCount) / (1.0 + documentFrequency)) + 1.0;
    }

    /// <summary>
    /// Builds corpus counts from documents already to hand. Useful for a small
    /// corpus and for tests; a large deployment would keep these counts in the
    /// database rather than rebuilding them per analysis.
    /// </summary>
    public static CorpusInverseDocumentFrequency FromDocuments(IEnumerable<AnalyzedDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var frequencies = new Dictionary<string, int>();
        var count = 0;

        foreach (var document in documents)
        {
            count++;

            foreach (var term in document.DistinctTerms)
            {
                frequencies[term] = frequencies.GetValueOrDefault(term) + 1;
            }
        }

        return new CorpusInverseDocumentFrequency(frequencies, count);
    }
}

/// <summary>
/// Treats every word as equally informative.
///
/// Used when no corpus statistics exist yet - the very first submission has
/// nothing to be compared against. Cosine similarity then reduces to plain term
/// frequency, which is a weaker but still sound measure rather than a wrong one.
/// </summary>
public sealed class UniformInverseDocumentFrequency : IInverseDocumentFrequency
{
    public static UniformInverseDocumentFrequency Instance { get; } = new();

    public double Weight(string term) => 1.0;
}
