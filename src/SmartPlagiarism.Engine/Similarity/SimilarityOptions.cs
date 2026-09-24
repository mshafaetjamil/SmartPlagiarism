using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Matching;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>Knobs for the analysis pipeline. The defaults are the documented ones.</summary>
public sealed class SimilarityOptions
{
    /// <summary>Words per fingerprint n-gram.</summary>
    public int FingerprintNGramSize { get; set; } = Winnower.DefaultNGramSize;

    /// <summary>Winnowing window.</summary>
    public int FingerprintWindowSize { get; set; } = Winnower.DefaultWindowSize;

    /// <summary>Words per n-gram for scoring and for locating fragments.</summary>
    public int ComparisonNGramSize { get; set; } = NGramOverlapSimilarity.DefaultNGramSize;

    /// <summary>
    /// Fingerprints a document must share to be worth comparing in full. Two is
    /// enough to rule out an isolated hash collision while still catching a single
    /// copied paragraph.
    /// </summary>
    public int MinimumSharedFingerprints { get; set; } = 2;

    /// <summary>
    /// Ceiling on the shortlist. Bounds the expensive stage regardless of corpus
    /// size; candidates are taken in descending order of shared fingerprints, so
    /// the most promising are never the ones dropped.
    /// </summary>
    public int MaxCandidates { get; set; } = 25;

    /// <summary>Matches scoring below this are left out of the report as noise.</summary>
    public decimal MinimumReportedSimilarityPercent { get; set; } = 5m;

    public int MinimumFragmentWords { get; set; } = FragmentLocator.DefaultMinimumFragmentWords;

    public int MaximumFragmentGap { get; set; } = FragmentLocator.DefaultMaximumGap;
}
