namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// One weighted score from several algorithms, so a single number can be shown to
/// a teacher without hiding which evidence produced it.
///
/// <para><b>Default weights and why.</b></para>
/// <list type="bullet">
///   <item><description>
///     <b>N-gram overlap, 0.50</b> - matching three consecutive words in order is
///     the least likely thing to happen by accident, so verbatim copying is the
///     strongest evidence available and carries the most weight.
///   </description></item>
///   <item><description>
///     <b>TF-IDF cosine, 0.35</b> - catches reworded and reordered copying that
///     n-grams miss entirely. Weighted below n-grams because two students writing
///     honestly on the same prescribed topic will legitimately score high here.
///   </description></item>
///   <item><description>
///     <b>Jaccard, 0.15</b> - vocabulary overlap alone is weak evidence, since
///     subject matter dictates most of it. It is kept because it degrades
///     gracefully on heavy paraphrasing, but it never dominates.
///   </description></item>
/// </list>
///
/// The weights are deliberately conservative about false positives: a high
/// combined score requires verbatim matching, which is what a teacher can act on.
/// They are constructor parameters, so they can be tuned without touching this
/// class.
/// </summary>
public sealed class CombinedSimilarity : ISimilarityAlgorithm
{
    private readonly IReadOnlyList<WeightedAlgorithm> _algorithms;

    public CombinedSimilarity(IReadOnlyList<WeightedAlgorithm> algorithms)
    {
        ArgumentNullException.ThrowIfNull(algorithms);

        if (algorithms.Count == 0)
        {
            throw new ArgumentException("At least one algorithm is required.", nameof(algorithms));
        }

        var totalWeight = algorithms.Sum(entry => entry.Weight);

        if (totalWeight <= 0)
        {
            throw new ArgumentException("Weights must sum to more than zero.", nameof(algorithms));
        }

        _algorithms = algorithms;
        TotalWeight = totalWeight;
    }

    /// <summary>The standard blend described on this class.</summary>
    public static CombinedSimilarity CreateDefault(IInverseDocumentFrequency idf) =>
        new(
        [
            new WeightedAlgorithm(new NGramOverlapSimilarity(), 0.50),
            new WeightedAlgorithm(new TfIdfCosineSimilarity(idf), 0.35),
            new WeightedAlgorithm(new JaccardSimilarity(), 0.15),
        ]);

    public string Name => "combined";

    /// <summary>Sum of the configured weights; the score is divided by this.</summary>
    public double TotalWeight { get; }

    public double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB)
    {
        double weightedTotal = 0;

        foreach (var entry in _algorithms)
        {
            weightedTotal += entry.Algorithm.Compute(documentA, documentB) * entry.Weight;
        }

        return Math.Clamp(weightedTotal / TotalWeight, 0, 1);
    }

    /// <summary>
    /// Every component's score alongside the combined one, so a report can show
    /// what drove the number rather than asking anyone to trust it.
    /// </summary>
    public SimilarityBreakdown Explain(AnalyzedDocument documentA, AnalyzedDocument documentB)
    {
        var components = new Dictionary<string, double>(_algorithms.Count);
        double weightedTotal = 0;

        foreach (var entry in _algorithms)
        {
            var score = entry.Algorithm.Compute(documentA, documentB);
            components[entry.Algorithm.Name] = score;
            weightedTotal += score * entry.Weight;
        }

        return new SimilarityBreakdown(Math.Clamp(weightedTotal / TotalWeight, 0, 1), components);
    }
}

/// <param name="Weight">Relative contribution; need not sum to 1.</param>
public sealed record WeightedAlgorithm(ISimilarityAlgorithm Algorithm, double Weight);

/// <param name="Combined">The weighted score in <c>[0, 1]</c>.</param>
/// <param name="Components">Each algorithm's own score, by name.</param>
public sealed record SimilarityBreakdown(double Combined, IReadOnlyDictionary<string, double> Components);
