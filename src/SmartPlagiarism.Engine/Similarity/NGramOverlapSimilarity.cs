namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Overlap of word 3-grams: how many of the smaller document's three-word runs
/// also appear in the larger one.
///
/// This is the strongest evidence of copying, because matching a run of three
/// consecutive words in order is far less likely to happen by chance than sharing
/// individual words. It is what the combined score leans on most.
///
/// The denominator is the <em>smaller</em> document's n-gram count - the overlap
/// coefficient - rather than the union used by Jaccard. The question worth asking
/// is "how much of this document appears in that one", and a union denominator
/// would dilute a real match simply because the other document is long. The
/// trade-off is that a short document quoted in full scores 100%, which is
/// arguably the correct answer anyway.
/// </summary>
public sealed class NGramOverlapSimilarity : ISimilarityAlgorithm
{
    /// <summary>
    /// Three words: long enough to be unlikely by chance, short enough to survive
    /// the small edits someone makes while copying.
    /// </summary>
    public const int DefaultNGramSize = 3;

    private readonly int _nGramSize;

    public NGramOverlapSimilarity(int nGramSize = DefaultNGramSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(nGramSize, 1);
        _nGramSize = nGramSize;
    }

    public string Name => $"ngram-{_nGramSize}";

    public double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB)
    {
        ArgumentNullException.ThrowIfNull(documentA);
        ArgumentNullException.ThrowIfNull(documentB);

        var hashesA = documentA.DistinctNGramHashes(_nGramSize);
        var hashesB = documentB.DistinctNGramHashes(_nGramSize);

        if (hashesA.Count == 0 || hashesB.Count == 0)
        {
            return 0;
        }

        var smaller = hashesA.Count <= hashesB.Count ? hashesA : hashesB;
        var larger = ReferenceEquals(smaller, hashesA) ? hashesB : hashesA;

        var shared = 0;

        foreach (var hash in smaller)
        {
            if (larger.Contains(hash))
            {
                shared++;
            }
        }

        return (double)shared / smaller.Count;
    }
}
