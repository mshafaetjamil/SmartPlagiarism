namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// Jaccard index over the two documents' vocabularies: shared words divided by
/// total distinct words.
///
/// This is the weakest of the three signals on its own - two honest essays on the
/// same subject share most of their vocabulary - which is why the combined score
/// weights it lowest. It earns its place by catching heavy paraphrasing, where
/// wording is reordered but the vocabulary is not.
/// </summary>
public sealed class JaccardSimilarity : ISimilarityAlgorithm
{
    public string Name => "jaccard";

    public double Compute(AnalyzedDocument documentA, AnalyzedDocument documentB)
    {
        ArgumentNullException.ThrowIfNull(documentA);
        ArgumentNullException.ThrowIfNull(documentB);

        if (documentA.IsEmpty || documentB.IsEmpty)
        {
            return 0;
        }

        var smaller = documentA.DistinctTerms.Count <= documentB.DistinctTerms.Count
            ? documentA.DistinctTerms
            : documentB.DistinctTerms;

        var larger = ReferenceEquals(smaller, documentA.DistinctTerms)
            ? documentB.DistinctTerms
            : documentA.DistinctTerms;

        var shared = 0;

        // Walking the smaller set keeps this O(min(|A|, |B|)).
        foreach (var term in smaller)
        {
            if (larger.Contains(term))
            {
                shared++;
            }
        }

        var union = documentA.DistinctTerms.Count + documentB.DistinctTerms.Count - shared;

        return union == 0 ? 0 : (double)shared / union;
    }
}
