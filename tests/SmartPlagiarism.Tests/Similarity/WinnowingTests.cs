using FluentAssertions;
using SmartPlagiarism.Engine.Fingerprinting;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Tests.Similarity;

/// <summary>
/// Winnowing is the scalability feature, and its value rests on one guarantee:
/// any shared run of at least <c>n + w - 1</c> words selects a common
/// fingerprint. If that fails, the shortlist silently loses real matches, so it
/// is tested directly rather than assumed.
/// </summary>
public class WinnowingTests
{
    private static IReadOnlyList<Token> Tokens(string text) => WordTokenizer.Tokenize(text);

    private const string Prose =
        "the winnowing algorithm selects the minimum hash value within each sliding window "
        + "of consecutive fingerprints which guarantees that any sufficiently long shared "
        + "passage between two documents will select at least one identical fingerprint "
        + "regardless of where the window boundaries happen to fall in either document";

    [Fact]
    public void GuaranteedMatchLength_IsNPlusWMinusOne()
    {
        Winnower.GuaranteedMatchLength(Winnower.DefaultNGramSize, Winnower.DefaultWindowSize)
            .Should().Be(8, "5-word grams with a window of 4 catch any shared run of 8 words");
    }

    [Fact]
    public void Fingerprinting_IsDeterministic()
    {
        // Fingerprints are stored and compared across processes, so the same text
        // must always produce the same hashes.
        var first = Winnower.Fingerprint(Tokens(Prose));
        var second = Winnower.Fingerprint(Tokens(Prose));

        second.DistinctHashes.Should().Equal(first.DistinctHashes);
    }

    [Fact]
    public void IdenticalText_ProducesIdenticalFingerprints()
    {
        var original = Winnower.Fingerprint(Tokens(Prose));
        var copy = Winnower.Fingerprint(Tokens(Prose));

        copy.DistinctHashes.Should().Equal(original.DistinctHashes);
    }

    [Fact]
    public void UnrelatedText_SharesNoFingerprints()
    {
        var first = Winnower.Fingerprint(Tokens(Prose));
        var second = Winnower.Fingerprint(Tokens(
            "photosynthesis converts light energy into chemical energy stored in carbohydrate "
            + "molecules produced from carbon dioxide and water inside plant chloroplasts"));

        first.DistinctHashes.Intersect(second.DistinctHashes).Should().BeEmpty();
    }

    [Fact]
    public void SharedPassageLongerThanTheGuarantee_AlwaysSelectsACommonFingerprint()
    {
        // The whole point of winnowing: this must hold whatever surrounds the shared
        // passage, so it is checked at several different alignments.
        const string shared =
            "any sufficiently long shared passage between two documents will select at least one identical fingerprint";

        for (var padding = 0; padding < 12; padding++)
        {
            var prefix = string.Join(' ', Enumerable.Range(0, padding).Select(index => $"filler{index}"));

            var first = Winnower.Fingerprint(Tokens($"alpha beta gamma {shared} delta epsilon"));
            var second = Winnower.Fingerprint(Tokens($"{prefix} {shared} entirely different trailing material here"));

            first.DistinctHashes.Intersect(second.DistinctHashes)
                .Should().NotBeEmpty($"a shared passage offset by {padding} words must still be detected");
        }
    }

    [Fact]
    public void SelectsFarFewerFingerprintsThanNGrams()
    {
        var tokens = Tokens(Prose);
        var fingerprints = Winnower.Fingerprint(tokens);
        var totalNGrams = tokens.Count - Winnower.DefaultNGramSize + 1;

        fingerprints.Count.Should().BeLessThan(
            totalNGrams,
            "storing every n-gram is exactly what winnowing exists to avoid");
        fingerprints.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DocumentShorterThanOneWindow_StillProducesAFingerprint()
    {
        // Six words: fewer n-grams than the window size.
        var fingerprints = Winnower.Fingerprint(Tokens("a short sentence with few words"));

        fingerprints.IsEmpty.Should().BeFalse("short documents must still be searchable");
    }

    [Fact]
    public void DocumentShorterThanOneNGram_ProducesNothing()
    {
        Winnower.Fingerprint(Tokens("three short words")).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void DistinctHashes_AreSortedAndDeduplicated()
    {
        var fingerprints = Winnower.Fingerprint(Tokens($"{Prose} {Prose}"));

        fingerprints.DistinctHashes.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void FingerprintPositions_PointAtRealTokens()
    {
        var tokens = Tokens(Prose);
        var fingerprints = Winnower.Fingerprint(tokens);

        foreach (var fingerprint in fingerprints.Fingerprints)
        {
            fingerprint.StartTokenIndex.Should().BeInRange(0, tokens.Count - Winnower.DefaultNGramSize);
        }
    }

    // ---------------- storage round-trip ----------------

    [Fact]
    public void PackAndUnpack_RoundTripsExactly()
    {
        var original = Winnower.Fingerprint(Tokens(Prose)).DistinctHashes;

        FingerprintSerializer.Unpack(FingerprintSerializer.Pack(original)).Should().Equal(original);
    }

    [Fact]
    public void Pack_UsesEightBytesPerHash()
    {
        FingerprintSerializer.Pack([1UL, 2UL, 3UL]).Should().HaveCount(24);
    }

    [Fact]
    public void Unpack_RejectsATruncatedBlob()
    {
        var act = () => FingerprintSerializer.Unpack(new byte[9]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CountShared_CountsOnlyHashesPresentInBoth()
    {
        var packed = FingerprintSerializer.Pack([10UL, 20UL, 30UL, 40UL]);

        FingerprintSerializer.CountShared(packed, new HashSet<ulong> { 20UL, 40UL, 99UL }).Should().Be(2);
        FingerprintSerializer.CountShared(packed, new HashSet<ulong>()).Should().Be(0);
    }
}
