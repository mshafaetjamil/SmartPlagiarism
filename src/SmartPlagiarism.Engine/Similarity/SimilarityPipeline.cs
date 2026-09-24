using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Matching;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// The three-stage analysis, cheapest stage first.
///
/// <list type="number">
///   <item><description>
///     <b>Exact hash.</b> If some existing file has the same SHA-256, it is the
///     same file. That is an index lookup, and it ends the analysis at 100%.
///   </description></item>
///   <item><description>
///     <b>Fingerprint shortlist.</b> Winnowing fingerprints narrow a corpus of any
///     size to a handful of documents that demonstrably share text. This is what
///     keeps analysis from being quadratic.
///   </description></item>
///   <item><description>
///     <b>Pairwise scoring.</b> The expensive algorithms and fragment localisation
///     run only against the shortlist, never the whole corpus.
///   </description></item>
/// </list>
///
/// Each stage exists to protect the next one from work it does not need to do.
/// </summary>
public sealed class SimilarityPipeline
{
    private readonly IFingerprintIndex _index;
    private readonly ICorpusReader _corpus;
    private readonly CombinedSimilarity _algorithm;
    private readonly SimilarityOptions _options;

    public SimilarityPipeline(
        IFingerprintIndex index,
        ICorpusReader corpus,
        CombinedSimilarity algorithm,
        SimilarityOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(algorithm);

        _index = index;
        _corpus = corpus;
        _algorithm = algorithm;
        _options = options ?? new SimilarityOptions();
    }

    /// <summary>
    /// Fingerprints a document for storage. Separate from analysis because the
    /// fingerprints of a new document must be saved whether or not it matches
    /// anything - they are what future submissions get shortlisted against.
    /// </summary>
    public FingerprintSet Fingerprint(AnalyzedDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Winnower.Fingerprint(
            document.Tokens,
            _options.FingerprintNGramSize,
            _options.FingerprintWindowSize);
    }

    /// <param name="query">The document being analysed.</param>
    /// <param name="excludeDocumentIds">
    /// Documents to ignore - the submission's own other files, which would
    /// otherwise match themselves.
    /// </param>
    public async Task<SimilarityReport> AnalyzeAsync(
        AnalyzedDocument query,
        IReadOnlyCollection<int>? excludeDocumentIds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var excluded = excludeDocumentIds ?? [];

        if (query.IsEmpty)
        {
            return SimilarityReport.Empty;
        }

        // Stage 1 - exact duplicates.
        var duplicates = await FindExactDuplicatesAsync(query, excluded, cancellationToken);

        if (duplicates.Count > 0)
        {
            return new SimilarityReport(100m, duplicates, duplicates.Count, duplicates.Count);
        }

        // Stage 2 - shortlist.
        var fingerprints = Fingerprint(query);

        if (fingerprints.IsEmpty)
        {
            return SimilarityReport.Empty;
        }

        var candidates = await _index.FindCandidatesAsync(
            new FingerprintQuery(
                new HashSet<ulong>(fingerprints.DistinctHashes),
                fingerprints.NGramSize,
                fingerprints.WindowSize,
                _options.MinimumSharedFingerprints,
                _options.MaxCandidates,
                excluded),
            cancellationToken);

        if (candidates.Count == 0)
        {
            return SimilarityReport.Empty;
        }

        // Stage 3 - detailed comparison, shortlist only.
        var matches = new List<DocumentMatch>(candidates.Count);
        var matchedNGramHashes = new HashSet<ulong>();
        var compared = 0;

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var source = await _corpus.LoadAsync(candidate.DocumentId, cancellationToken);

            if (source is null || source.IsEmpty)
            {
                // Shortlisted on fingerprints but its text is gone or never extracted.
                continue;
            }

            compared++;

            var breakdown = _algorithm.Explain(source, query);
            var similarityPercent = ToPercent(breakdown.Combined);

            if (similarityPercent < _options.MinimumReportedSimilarityPercent)
            {
                continue;
            }

            var fragments = FragmentLocator.Locate(
                source,
                query,
                _options.ComparisonNGramSize,
                _options.MinimumFragmentWords,
                _options.MaximumFragmentGap);

            AccumulateCoverage(query, source, matchedNGramHashes);

            matches.Add(new DocumentMatch(
                candidate.DocumentId,
                similarityPercent,
                MatchKind.Analyzed,
                candidate.SharedFingerprintCount,
                breakdown.Components,
                fragments));
        }

        matches.Sort((left, right) => right.SimilarityPercent.CompareTo(left.SimilarityPercent));

        return new SimilarityReport(
            CalculateCoveragePercent(query, matchedNGramHashes),
            matches,
            candidates.Count,
            compared);
    }

    private async Task<List<DocumentMatch>> FindExactDuplicatesAsync(
        AnalyzedDocument query,
        IReadOnlyCollection<int> excluded,
        CancellationToken cancellationToken)
    {
        var duplicates = new List<DocumentMatch>();

        if (string.IsNullOrWhiteSpace(query.ContentHash))
        {
            return duplicates;
        }

        var identical = await _corpus.FindByContentHashAsync(query.ContentHash, excluded, cancellationToken);

        foreach (var documentId in identical)
        {
            duplicates.Add(new DocumentMatch(
                documentId,
                100m,
                MatchKind.ExactDuplicate,
                SharedFingerprintCount: 0,

                // No algorithm ran: the files are byte-identical, and saying so is
                // more honest than reporting scores nothing computed.
                ComponentScores: new Dictionary<string, double>(),
                Fragments: []));
        }

        return duplicates;
    }

    /// <summary>
    /// Records which of the query's n-grams this source accounts for, so overall
    /// similarity can be coverage across every match rather than the single
    /// highest-scoring one.
    /// </summary>
    private void AccumulateCoverage(
        AnalyzedDocument query,
        AnalyzedDocument source,
        HashSet<ulong> matchedNGramHashes)
    {
        var sourceHashes = source.DistinctNGramHashes(_options.ComparisonNGramSize);

        foreach (var hash in query.DistinctNGramHashes(_options.ComparisonNGramSize))
        {
            if (sourceHashes.Contains(hash))
            {
                matchedNGramHashes.Add(hash);
            }
        }
    }

    private decimal CalculateCoveragePercent(AnalyzedDocument query, HashSet<ulong> matchedNGramHashes)
    {
        var total = query.DistinctNGramHashes(_options.ComparisonNGramSize).Count;

        return total == 0 ? 0m : ToPercent((double)matchedNGramHashes.Count / total);
    }

    /// <summary>
    /// Ratio to a percentage with two decimals, matching the decimal(5,2) columns
    /// the result is stored in.
    /// </summary>
    private static decimal ToPercent(double ratio) =>
        Math.Round((decimal)Math.Clamp(ratio, 0, 1) * 100m, 2, MidpointRounding.AwayFromZero);
}
