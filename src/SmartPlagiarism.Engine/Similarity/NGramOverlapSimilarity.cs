namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Containment of word 3-grams: how many of the analysed document's three-word
/// runs also appear in the source.
///
/// This is the strongest evidence of copying, because matching a run of three
/// consecutive words in order is far less likely to happen by chance than sharing
/// individual words.
///
/// The denominator is the <em>analysed</em> document's n-gram count: the question
/// is "how much of this submission appears in that source". An earlier version
/// divided by the smaller document, which made a 23-word abstract quoted inside a
/// 300-word submission score 100% against it although 7% of the submission was
/// copied. Measured this way it scores 7%, and a short source quoted in full is
/// still found - its passage is located and highlighted - without claiming the
/// submission is a copy of it.
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

        var source = documentA.DistinctNGramHashes(_nGramSize);
        var analysed = documentB.DistinctNGramHashes(_nGramSize);

        if (source.Count == 0 || analysed.Count == 0)
        {
            return 0;
        }

        var shared = 0;

        foreach (var hash in analysed)
        {
            if (source.Contains(hash))
            {
                shared++;
            }
        }

        return (double)shared / analysed.Count;
    }
}
