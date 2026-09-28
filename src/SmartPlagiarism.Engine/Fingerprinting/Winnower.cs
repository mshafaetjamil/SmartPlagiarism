using System.Buffers.Binary;
using SmartPlagiarism.Engine.Similarity;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Engine.Fingerprinting;

/// <param name="Hash">Hash of the n-gram that was selected.</param>
/// <param name="StartTokenIndex">Where that n-gram starts, for localisation.</param>
public readonly record struct Fingerprint(ulong Hash, int StartTokenIndex);

/// <summary>
/// A document's winnowing fingerprints.
/// </summary>
public sealed class FingerprintSet
{
    internal FingerprintSet(
        IReadOnlyList<Fingerprint> fingerprints,
        IReadOnlyList<ulong> distinctHashes,
        int nGramSize,
        int windowSize)
    {
        Fingerprints = fingerprints;
        DistinctHashes = distinctHashes;
        NGramSize = nGramSize;
        WindowSize = windowSize;
    }

    /// <summary>Selected fingerprints in document order, with their positions.</summary>
    public IReadOnlyList<Fingerprint> Fingerprints { get; }

    /// <summary>
    /// Distinct hashes, ascending. This is what gets stored and what the index
    /// compares; sorted so two sets can be intersected by a merge scan.
    /// </summary>
    public IReadOnlyList<ulong> DistinctHashes { get; }

    public int NGramSize { get; }

    public int WindowSize { get; }

    public int Count => Fingerprints.Count;

    public bool IsEmpty => Fingerprints.Count == 0;

    public static FingerprintSet Empty(int nGramSize, int windowSize) =>
        new([], [], nGramSize, windowSize);
}

/// <summary>
/// Winnowing, as described by Schleimer, Wilkerson and Aiken (2003).
///
/// Comparing every n-gram of every document against every other is quadratic and
/// hopeless past a few hundred documents. Winnowing keeps only a sample: slide a
/// window of <c>w</c> consecutive n-gram hashes across the document and keep the
/// smallest in each window.
///
/// The point is that this is not random sampling. Because neighbouring windows
/// overlap, two documents sharing any run of at least <c>n + w - 1</c> words are
/// guaranteed to select at least one fingerprint in common - the same minimum is
/// visible from both. So the shortlist cannot silently miss a substantial copied
/// passage, while storing only a fraction of the hashes.
///
/// With the defaults here - 5-word grams, window 4 - any shared run of 8 words or
/// more is certain to be caught.
/// </summary>
public static class Winnower
{
    /// <summary>
    /// Five words per gram: long enough that a match means something, short enough
    /// that a copied sentence still produces several.
    /// </summary>
    public const int DefaultNGramSize = 5;

    /// <summary>
    /// Window of four, so roughly one hash in four is kept and any shared run of
    /// <c>5 + 4 - 1 = 8</c> words is guaranteed to be detected.
    /// </summary>
    public const int DefaultWindowSize = 4;

    /// <summary>Shortest shared run of words the defaults are guaranteed to catch.</summary>
    public static int GuaranteedMatchLength(int nGramSize, int windowSize) => nGramSize + windowSize - 1;

    public static FingerprintSet Fingerprint(
        IReadOnlyList<Token> tokens,
        int nGramSize = DefaultNGramSize,
        int windowSize = DefaultWindowSize)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentOutOfRangeException.ThrowIfLessThan(nGramSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowSize, 1);

        var nGrams = NGramExtractor.Extract(tokens, nGramSize);

        if (nGrams.Count == 0)
        {
            return FingerprintSet.Empty(nGramSize, windowSize);
        }

        var selected = SelectWindowMinimums(nGrams, windowSize);

        var distinct = new HashSet<ulong>(selected.Count);

        foreach (var fingerprint in selected)
        {
            distinct.Add(fingerprint.Hash);
        }

        var sorted = distinct.ToArray();
        Array.Sort(sorted);

        return new FingerprintSet(selected, sorted, nGramSize, windowSize);
    }

    /// <summary>
    /// The window minimum, preferring the rightmost on a tie and skipping a
    /// position already selected by the previous window.
    ///
    /// Choosing the rightmost occurrence is what makes the guarantee hold: it means
    /// the same physical n-gram is chosen regardless of where the window happens to
    /// be aligned, so identical passages in two documents select identically.
    /// </summary>
    private static List<Fingerprint> SelectWindowMinimums(IReadOnlyList<NGram> nGrams, int windowSize)
    {
        var selected = new List<Fingerprint>();
        var previouslySelectedIndex = -1;

        // A document shorter than one window still yields its single minimum, so
        // short documents are fingerprinted rather than ignored.
        var lastWindowStart = Math.Max(0, nGrams.Count - windowSize);

        for (var windowStart = 0; windowStart <= lastWindowStart; windowStart++)
        {
            var windowEnd = Math.Min(windowStart + windowSize, nGrams.Count);
            var minimumIndex = windowStart;

            for (var index = windowStart + 1; index < windowEnd; index++)
            {
                // "<=" prefers the rightmost occurrence of a repeated minimum.
                if (nGrams[index].Hash <= nGrams[minimumIndex].Hash)
                {
                    minimumIndex = index;
                }
            }

            if (minimumIndex == previouslySelectedIndex)
            {
                continue;
            }

            previouslySelectedIndex = minimumIndex;
            selected.Add(new Fingerprint(nGrams[minimumIndex].Hash, nGrams[minimumIndex].StartTokenIndex));
        }

        return selected;
    }
}

/// <summary>
/// Packs fingerprint hashes for storage in the DocumentFingerprint blob column,
/// and reads them back.
///
/// Little-endian throughout so a database written on one platform is readable on
/// another.
/// </summary>
public static class FingerprintSerializer
{
    private const int BytesPerHash = sizeof(ulong);

    public static byte[] Pack(IReadOnlyList<ulong> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);

        var buffer = new byte[hashes.Count * BytesPerHash];

        for (var index = 0; index < hashes.Count; index++)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                buffer.AsSpan(index * BytesPerHash, BytesPerHash),
                hashes[index]);
        }

        return buffer;
    }

    public static ulong[] Unpack(ReadOnlySpan<byte> packed)
    {
        if (packed.Length % BytesPerHash != 0)
        {
            throw new ArgumentException(
                $"A packed fingerprint blob must be a multiple of {BytesPerHash} bytes, got {packed.Length}.",
                nameof(packed));
        }

        var hashes = new ulong[packed.Length / BytesPerHash];

        for (var index = 0; index < hashes.Length; index++)
        {
            hashes[index] = BinaryPrimitives.ReadUInt64LittleEndian(
                packed.Slice(index * BytesPerHash, BytesPerHash));
        }

        return hashes;
    }
}
