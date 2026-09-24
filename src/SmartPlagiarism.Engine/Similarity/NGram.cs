using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Engine.Similarity;

/// <summary>
/// A run of <c>n</c> consecutive words, reduced to a hash, plus where that run
/// starts. The start index is what lets a hash match be turned back into
/// character offsets for highlighting.
/// </summary>
public readonly record struct NGram(ulong Hash, int StartTokenIndex);

/// <summary>
/// Builds word n-grams and hashes them.
///
/// Hashing uses FNV-1a rather than <see cref="string.GetHashCode()"/> because
/// fingerprints are written to the database and compared across processes and
/// runs; .NET's string hash is randomised per process and would make yesterday's
/// stored fingerprints meaningless today.
/// </summary>
public static class NGramExtractor
{
    private const ulong Fnv1aOffsetBasis = 14695981039346656037;
    private const ulong Fnv1aPrime = 1099511628211;

    /// <summary>Separator hashed between words so "ab c" and "a bc" differ.</summary>
    private const byte WordSeparator = 0x1F;

    /// <summary>
    /// Every n-gram in order. Returns empty when the document has fewer than
    /// <paramref name="size"/> words.
    /// </summary>
    public static IReadOnlyList<NGram> Extract(IReadOnlyList<Token> tokens, int size)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        if (tokens.Count < size)
        {
            return [];
        }

        var nGrams = new NGram[tokens.Count - size + 1];

        for (var start = 0; start < nGrams.Length; start++)
        {
            nGrams[start] = new NGram(Hash(tokens, start, size), start);
        }

        return nGrams;
    }

    /// <summary>Distinct hashes of every n-gram, for set-based comparison.</summary>
    public static HashSet<ulong> ExtractDistinctHashes(IReadOnlyList<Token> tokens, int size)
    {
        var hashes = new HashSet<ulong>();

        foreach (var nGram in Extract(tokens, size))
        {
            hashes.Add(nGram.Hash);
        }

        return hashes;
    }

    private static ulong Hash(IReadOnlyList<Token> tokens, int start, int size)
    {
        var hash = Fnv1aOffsetBasis;

        for (var offset = 0; offset < size; offset++)
        {
            if (offset > 0)
            {
                hash = Step(hash, WordSeparator);
            }

            foreach (var character in tokens[start + offset].Value)
            {
                hash = Step(hash, (byte)(character & 0xFF));
                hash = Step(hash, (byte)(character >> 8));
            }
        }

        return hash;
    }

    private static ulong Step(ulong hash, byte value) => (hash ^ value) * Fnv1aPrime;
}
