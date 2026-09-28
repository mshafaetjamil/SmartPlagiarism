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
///     <b>Pairwise scoring.</b> Fragment localisation and the similarity measures
///     run only against the shortlist, never the whole corpus.
///   </description></item>
/// </list>
///
/// Each stage exists to protect the next one from work it does not need to do.
///
/// <para><b>What the percentages mean.</b> Both count words of the analysed
/// document inside located passages - runs of at least
/// <see cref="SimilarityOptions.MinimumFragmentWords"/> words found in a source.
/// A source's percentage is the share of the document in passages found in that
/// source; the overall percentage is the share in passages found in any source -
/// other than the author's own earlier work - so it is never less than any one
/// such source's and never counts a word twice.
/// Stock phrases and chance three-word overlaps are shorter than a passage and
/// count for nothing, which is what keeps two honest essays on one topic at 0%.
/// The weighted blend of <see cref="CombinedSimilarity"/> is reported alongside
/// each source as supporting evidence - its vocabulary measures notice reworded
/// copying that passages miss - but it is not the headline: it measures how alike
/// two documents are, not how much of this one came from the other.</para>
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
    /// <param name="uncountedDocumentIds">
    /// Documents to report but leave out of the overall percentage: the author's
    /// own earlier work. A resubmission after a rejection is not copying from
    /// anyone, but reuse across courses is something a teacher may want to see.
    /// </param>
    /// <param name="commonFingerprints">
    /// Fingerprints of common text - words most of a class shares, such as a
    /// cover page or a declaration. A passage made mostly of them is neither
    /// counted nor a reason to list its source; how much of the document such
    /// passages cover is reported instead. The host decides what is common.
    /// </param>
    public async Task<SimilarityReport> AnalyzeAsync(
        AnalyzedDocument query,
        IReadOnlyCollection<int>? excludeDocumentIds = null,
        IReadOnlySet<int>? uncountedDocumentIds = null,
        IReadOnlySet<ulong>? commonFingerprints = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var excluded = new HashSet<int>(excludeDocumentIds ?? []);
        var uncounted = uncountedDocumentIds ?? new HashSet<int>();

        if (query.IsEmpty)
        {
            return SimilarityReport.Empty;
        }

        // Stage 1 - exact duplicates.
        var duplicates = await FindExactDuplicatesAsync(query, excluded, cancellationToken);

        if (duplicates.Any(duplicate => !uncounted.Contains(duplicate.DocumentId)))
        {
            return new SimilarityReport(100m, duplicates, duplicates.Count, duplicates.Count);
        }

        // Identical only to the author's own earlier copies: those are listed,
        // and the analysis goes on against everyone else's work.
        var matches = new List<DocumentMatch>(duplicates);
        excluded.UnionWith(duplicates.Select(duplicate => duplicate.DocumentId));

        // Stage 2 - shortlist.
        var fingerprints = Fingerprint(query);

        if (fingerprints.IsEmpty)
        {
            return new SimilarityReport(0m, matches, duplicates.Count, duplicates.Count);
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

        // Stage 3 - detailed comparison, shortlist only.
        var coveredByCountedSources = new bool[query.WordCount];
        var coveredByCommonText = new bool[query.WordCount];
        var commonWords = MarkCommonWords(query, commonFingerprints);
        var compared = duplicates.Count;

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

            var fragments = SetAsideCommonText(
                query,
                FragmentLocator.Locate(
                    source,
                    query,
                    _options.ComparisonNGramSize,
                    _options.MinimumFragmentWords,
                    _options.MaximumFragmentGap),
                commonWords,
                coveredByCommonText);

            if (fragments.Count == 0)
            {
                // Shares fingerprints, but no passage long enough to be more than
                // stock phrases, or only the class's common text - nothing a
                // teacher could act on.
                continue;
            }

            var coveredWords = MarkCovered(
                query,
                fragments,
                commonWords,
                uncounted.Contains(candidate.DocumentId) ? null : coveredByCountedSources);
            var breakdown = _algorithm.Explain(source, query);

            matches.Add(new DocumentMatch(
                candidate.DocumentId,
                ToPercent((double)coveredWords / query.WordCount),
                MatchKind.Analyzed,
                candidate.SharedFingerprintCount,
                new Dictionary<string, double>(breakdown.Components) { [CombinedSimilarity.ScoreName] = breakdown.Combined },
                fragments));
        }

        matches.Sort((left, right) => right.SimilarityPercent.CompareTo(left.SimilarityPercent));

        return new SimilarityReport(
            ToPercent((double)coveredByCountedSources.Count(covered => covered) / query.WordCount),
            matches,
            candidates.Count + duplicates.Count,
            compared,
            ToPercent((double)coveredByCommonText.Count(covered => covered) / query.WordCount));
    }

    /// <summary>
    /// The analysed document's words that belong to common text.
    ///
    /// Only winnowed fingerprints are known to be common, not every n-gram, but
    /// winnowing picks one in every window, so inside a common passage the picked
    /// n-grams overlap and mark it without gaps. At its two ends up to a window's
    /// worth of words can fall outside any pick, so each marked run is widened by
    /// that much: a doubtful word is left uncounted rather than counted as copied.
    /// </summary>
    private bool[] MarkCommonWords(AnalyzedDocument query, IReadOnlySet<ulong>? commonFingerprints)
    {
        var picked = new bool[query.WordCount];

        if (commonFingerprints is null || commonFingerprints.Count == 0)
        {
            return picked;
        }

        foreach (var nGram in query.NGrams(_options.FingerprintNGramSize))
        {
            if (!commonFingerprints.Contains(nGram.Hash))
            {
                continue;
            }

            for (var index = nGram.StartTokenIndex; index < nGram.StartTokenIndex + _options.FingerprintNGramSize; index++)
            {
                picked[index] = true;
            }
        }

        var widening = _options.FingerprintWindowSize - 1;
        var marked = new bool[query.WordCount];

        for (var index = 0; index < picked.Length; index++)
        {
            if (!picked[index])
            {
                continue;
            }

            for (var near = Math.Max(0, index - widening); near <= Math.Min(picked.Length - 1, index + widening); near++)
            {
                marked[near] = true;
            }
        }

        return marked;
    }

    /// <summary>
    /// The fragments still worth reporting once common text is set aside. A
    /// passage can run from a shared cover page straight into copied text, so
    /// this goes word by word: common words are recorded as such and never
    /// counted, and a fragment is kept only if enough words remain to be a
    /// passage in their own right.
    /// </summary>
    private List<MatchedFragment> SetAsideCommonText(
        AnalyzedDocument query,
        IReadOnlyList<MatchedFragment> fragments,
        bool[] commonWords,
        bool[] coveredByCommonText)
    {
        var kept = new List<MatchedFragment>(fragments.Count);

        foreach (var fragment in fragments)
        {
            var (first, length) = query.TokensWithin(fragment.TargetStart, fragment.TargetEnd);
            var ownWords = 0;

            for (var index = first; index < first + length; index++)
            {
                ownWords += commonWords[index] ? 0 : 1;
            }

            var keep = ownWords >= _options.MinimumFragmentWords;

            if (keep)
            {
                kept.Add(fragment);
            }

            for (var index = first; index < first + length; index++)
            {
                if (commonWords[index] || !keep)
                {
                    coveredByCommonText[index] = true;
                }
            }
        }

        return kept;
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
    /// Marks the analysed document's words that lie inside the fragments, for
    /// this source and, when it counts toward the overall figure, in the running
    /// total across sources. Common-text words are never counted.
    /// </summary>
    /// <returns>Words covered by this source's fragments, each counted once.</returns>
    private static int MarkCovered(
        AnalyzedDocument query,
        IReadOnlyList<MatchedFragment> fragments,
        bool[] commonWords,
        bool[]? coveredByCountedSources)
    {
        var coveredHere = new bool[query.WordCount];
        var count = 0;

        foreach (var fragment in fragments)
        {
            var (first, length) = query.TokensWithin(fragment.TargetStart, fragment.TargetEnd);

            for (var index = first; index < first + length; index++)
            {
                if (commonWords[index])
                {
                    continue;
                }

                if (!coveredHere[index])
                {
                    coveredHere[index] = true;
                    count++;
                }

                if (coveredByCountedSources is not null)
                {
                    coveredByCountedSources[index] = true;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Ratio to a percentage with two decimals, matching the decimal(5,2) columns
    /// the result is stored in.
    /// </summary>
    private static decimal ToPercent(double ratio) =>
        Math.Round((decimal)Math.Clamp(ratio, 0, 1) * 100m, 2, MidpointRounding.AwayFromZero);
}
