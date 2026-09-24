namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Cosine similarity between the documents' TF-IDF vectors.
///
/// Each document becomes a vector over its vocabulary, where a word's weight is
/// how often it occurs multiplied by how rare it is corpus-wide. The cosine of
/// the angle between those vectors ignores document length, so a long report and
/// a short summary covering the same ground still score highly.
///
/// This is the signal that survives paraphrasing: reordered sentences and swapped
/// synonyms leave the weighted vocabulary largely intact, where verbatim n-gram
/// matching would find nothing.
/// </summary>
public sealed class TfIdfCosineSimilarity : ISimilarityAlgorithm
{
    private readonly IInverseDocumentFrequency _idf;

    /// <param name="idf">
    /// Corpus statistics. Pass <see cref="UniformInverseDocumentFrequency.Instance"/>
    /// when there is no corpus yet.
    /// </param>
    public TfIdfCosineSimilarity(IInverseDocumentFrequency idf)
    {
        ArgumentNullException.ThrowIfNull(idf);
        _idf = idf;
    }

    public string Name => "tfidf-cosine";

    public double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB)
    {
        ArgumentNullException.ThrowIfNull(documentA);
        ArgumentNullException.ThrowIfNull(documentB);

        if (documentA.IsEmpty || documentB.IsEmpty)
        {
            return 0;
        }

        var vectorA = documentA.TermFrequencies;
        var vectorB = documentB.TermFrequencies;

        double dotProduct = 0;
        double magnitudeA = 0;
        double magnitudeB = 0;

        // Only terms in A can contribute to the dot product, so the shared loop runs
        // over A and B's magnitude is accumulated separately.
        foreach (var (term, frequency) in vectorA)
        {
            var weight = WeightOf(term, frequency);
            magnitudeA += weight * weight;

            if (vectorB.TryGetValue(term, out var frequencyInB))
            {
                dotProduct += weight * WeightOf(term, frequencyInB);
            }
        }

        foreach (var (term, frequency) in vectorB)
        {
            var weight = WeightOf(term, frequency);
            magnitudeB += weight * weight;
        }

        if (magnitudeA == 0 || magnitudeB == 0)
        {
            return 0;
        }

        var cosine = dotProduct / (Math.Sqrt(magnitudeA) * Math.Sqrt(magnitudeB));

        // Floating-point error can nudge an exact match a hair past 1.
        return Math.Clamp(cosine, 0, 1);
    }

    /// <summary>
    /// Sublinear term frequency: <c>1 + ln(count)</c>, weighted by IDF. Using the
    /// raw count would let a word repeated twenty times dominate a vector out of
    /// all proportion to its actual significance.
    /// </summary>
    private double WeightOf(string term, int frequency) =>
        (1.0 + Math.Log(frequency)) * _idf.Weight(term);
}
