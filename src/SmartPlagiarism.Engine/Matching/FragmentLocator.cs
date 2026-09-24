using SmartPlagiarism.Engine.Similarity;

namespace SmartPlagiarism.Engine.Matching;

/// <summary>
/// Turns "these two documents are similar" into "these exact passages are shared".
///
/// A score tells a teacher a submission is suspicious; only the passages let them
/// judge it. This finds them by matching word n-grams between the documents and
/// then stitching consecutive matches back into whole passages.
///
/// The stitching is what matters. A copied paragraph produces dozens of adjacent
/// n-gram matches, and reporting each one separately would be useless. Matches
/// that continue in step in both documents - the same diagonal - belong to one
/// passage and are merged into a single fragment.
/// </summary>
public static class FragmentLocator
{
    /// <summary>
    /// Words a fragment must span to be reported. Shorter runs are mostly stock
    /// phrases - "on the other hand", "as shown in figure" - and reporting them
    /// buries the real matches in noise.
    /// </summary>
    public const int DefaultMinimumFragmentWords = 8;

    /// <summary>
    /// N-gram matches this far apart on the same diagonal are still treated as one
    /// passage. Bridges the single changed word someone drops into copied text,
    /// which would otherwise split one paragraph into several fragments.
    /// </summary>
    public const int DefaultMaximumGap = 3;

    public static IReadOnlyList<MatchedFragment> Locate(
        AnalyzedDocument source,
        AnalyzedDocument target,
        int nGramSize = NGramOverlapSimilarity.DefaultNGramSize,
        int minimumFragmentWords = DefaultMinimumFragmentWords,
        int maximumGap = DefaultMaximumGap)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var sourceNGrams = source.NGrams(nGramSize);
        var targetNGrams = target.NGrams(nGramSize);

        if (sourceNGrams.Count == 0 || targetNGrams.Count == 0)
        {
            return [];
        }

        var sourcePositionsByHash = BuildPositionIndex(sourceNGrams);

        // Keyed by diagonal (sourceIndex - targetIndex): a passage copied verbatim
        // advances through both documents together, so every one of its n-gram
        // matches sits on the same diagonal.
        var runsByDiagonal = new Dictionary<int, MatchRun>();
        var completed = new List<MatchRun>();

        for (var targetIndex = 0; targetIndex < targetNGrams.Count; targetIndex++)
        {
            if (!sourcePositionsByHash.TryGetValue(targetNGrams[targetIndex].Hash, out var sourceIndices))
            {
                continue;
            }

            foreach (var sourceIndex in sourceIndices)
            {
                var diagonal = sourceIndex - targetIndex;

                if (runsByDiagonal.TryGetValue(diagonal, out var run)
                    && targetIndex - run.LastTargetIndex <= maximumGap + 1)
                {
                    run.Extend(targetIndex, sourceIndex);
                    continue;
                }

                if (run is not null)
                {
                    completed.Add(run);
                }

                runsByDiagonal[diagonal] = new MatchRun(sourceIndex, targetIndex);
            }
        }

        completed.AddRange(runsByDiagonal.Values);

        return BuildFragments(source, target, completed, nGramSize, minimumFragmentWords);
    }

    private static Dictionary<ulong, List<int>> BuildPositionIndex(IReadOnlyList<NGram> nGrams)
    {
        var index = new Dictionary<ulong, List<int>>(nGrams.Count);

        for (var position = 0; position < nGrams.Count; position++)
        {
            if (!index.TryGetValue(nGrams[position].Hash, out var positions))
            {
                positions = [];
                index[nGrams[position].Hash] = positions;
            }

            positions.Add(position);
        }

        return index;
    }

    private static List<MatchedFragment> BuildFragments(
        AnalyzedDocument source,
        AnalyzedDocument target,
        List<MatchRun> runs,
        int nGramSize,
        int minimumFragmentWords)
    {
        var fragments = new List<MatchedFragment>();

        foreach (var run in runs)
        {
            // A run of k n-grams covers k + n - 1 words.
            var spannedNGrams = run.LastTargetIndex - run.FirstTargetIndex + 1;
            var spannedWords = spannedNGrams + nGramSize - 1;

            if (spannedWords < minimumFragmentWords)
            {
                continue;
            }

            var (targetStart, targetEnd) = target.SpanOf(
                run.FirstTargetIndex,
                run.LastTargetIndex + nGramSize - 1);

            var (sourceStart, sourceEnd) = source.SpanOf(
                run.FirstSourceIndex,
                run.LastSourceIndex + nGramSize - 1);

            // Below 1 only where gaps were bridged, so the score reflects how much of
            // the reported span genuinely matched.
            var similarity = (double)run.MatchedNGrams / spannedNGrams;

            fragments.Add(new MatchedFragment(
                sourceStart,
                sourceEnd,
                targetStart,
                targetEnd,
                Math.Round(Math.Clamp(similarity, 0, 1), 4)));
        }

        // Longest first: the passage a teacher should read is the biggest one.
        fragments.Sort((left, right) => right.TargetLength.CompareTo(left.TargetLength));

        return fragments;
    }

    private sealed class MatchRun(int firstSourceIndex, int firstTargetIndex)
    {
        public int FirstSourceIndex { get; } = firstSourceIndex;

        public int FirstTargetIndex { get; } = firstTargetIndex;

        public int LastSourceIndex { get; private set; } = firstSourceIndex;

        public int LastTargetIndex { get; private set; } = firstTargetIndex;

        public int MatchedNGrams { get; private set; } = 1;

        public void Extend(int targetIndex, int sourceIndex)
        {
            LastTargetIndex = targetIndex;
            LastSourceIndex = sourceIndex;
            MatchedNGrams++;
        }
    }
}
