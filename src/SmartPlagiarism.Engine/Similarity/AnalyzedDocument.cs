using System.Collections.Concurrent;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// A document prepared for comparison: the original text, its words with their
/// positions, and the derived views the algorithms need.
///
/// The derived views are computed once and cached, because the query document is
/// compared against every shortlisted candidate and recomputing its n-grams each
/// time would dominate the cost.
/// </summary>
public sealed class AnalyzedDocument
{
    private readonly ConcurrentDictionary<int, IReadOnlyList<NGram>> _nGramsBySize = new();
    private readonly ConcurrentDictionary<int, HashSet<ulong>> _distinctNGramHashesBySize = new();
    private readonly Lazy<IReadOnlyDictionary<string, int>> _termFrequencies;
    private readonly Lazy<HashSet<string>> _distinctTerms;

    public AnalyzedDocument(int id, string text, string? contentHash = null)
    {
        Id = id;
        Text = text ?? string.Empty;
        ContentHash = contentHash;
        Tokens = WordTokenizer.Tokenize(Text);

        _termFrequencies = new Lazy<IReadOnlyDictionary<string, int>>(BuildTermFrequencies);
        _distinctTerms = new Lazy<HashSet<string>>(() => [.. Tokens.Select(token => token.Value)]);
    }

    /// <summary>
    /// Identifies the document to the host. This engine never interprets it - it is
    /// whatever key the caller uses, in practice an UploadedFile id.
    /// </summary>
    public int Id { get; }

    /// <summary>The text as extracted, used for offsets and for display.</summary>
    public string Text { get; }

    /// <summary>SHA-256 of the source file, when known, for the exact-duplicate check.</summary>
    public string? ContentHash { get; }

    public IReadOnlyList<Token> Tokens { get; }

    public int WordCount => Tokens.Count;

    public bool IsEmpty => Tokens.Count == 0;

    /// <summary>How often each distinct word occurs. Used by TF-IDF.</summary>
    public IReadOnlyDictionary<string, int> TermFrequencies => _termFrequencies.Value;

    /// <summary>The document's vocabulary. Used by Jaccard.</summary>
    public IReadOnlySet<string> DistinctTerms => _distinctTerms.Value;

    public IReadOnlyList<NGram> NGrams(int size) =>
        _nGramsBySize.GetOrAdd(size, key => NGramExtractor.Extract(Tokens, key));

    public IReadOnlySet<ulong> DistinctNGramHashes(int size) =>
        _distinctNGramHashesBySize.GetOrAdd(size, key =>
        {
            var hashes = new HashSet<ulong>();

            foreach (var nGram in NGrams(key))
            {
                hashes.Add(nGram.Hash);
            }

            return hashes;
        });

    /// <summary>
    /// Character span in <see cref="Text"/> covering tokens
    /// <paramref name="firstTokenIndex"/> through <paramref name="lastTokenIndex"/>
    /// inclusive.
    /// </summary>
    public (int Start, int End) SpanOf(int firstTokenIndex, int lastTokenIndex) =>
        (Tokens[firstTokenIndex].Start, Tokens[lastTokenIndex].End);

    private IReadOnlyDictionary<string, int> BuildTermFrequencies()
    {
        var frequencies = new Dictionary<string, int>();

        foreach (var token in Tokens)
        {
            frequencies[token.Value] = frequencies.GetValueOrDefault(token.Value) + 1;
        }

        return frequencies;
    }
}
