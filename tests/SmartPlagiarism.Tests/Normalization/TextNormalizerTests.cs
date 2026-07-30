using FluentAssertions;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Tests.Normalization;

/// <summary>
/// The normalizer decides what counts as "the same wording", so every folding
/// rule is pinned down here.
///
/// Unicode cases use escapes rather than literal characters: a zero-width space
/// or a combining accent pasted into source is invisible in a diff, and two
/// literals that look identical may not be.
/// </summary>
public class TextNormalizerTests
{
    [Fact]
    public void Normalize_Lowercases()
    {
        TextNormalizer.Normalize("The Quick Brown Fox").Should().Be("the quick brown fox");
    }

    [Fact]
    public void Normalize_ReplacesPunctuationWithASpace()
    {
        // "well-known" and "well known" must compare equal.
        TextNormalizer.Normalize("well-known").Should().Be("well known");
    }

    [Theory]
    [InlineData("a,b;c:d!e?f", "a b c d e f")]
    [InlineData("(parentheses) [brackets]", "parentheses brackets")]
    [InlineData("100% of $40 + 2", "100 of 40 2")]
    [InlineData("\"smart quotes\" and 'apostrophes'", "smart quotes and apostrophes")]
    public void Normalize_StripsPunctuationAndSymbols(string input, string expected)
    {
        TextNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("  leading and trailing  ", "leading and trailing")]
    [InlineData("collapse\t\tmany   spaces", "collapse many spaces")]
    [InlineData("line\r\nbreaks\nbecome\rspaces", "line breaks become spaces")]
    [InlineData("non\u00A0breaking\u00A0space", "non breaking space")]
    public void Normalize_CollapsesWhitespaceAndTrims(string input, string expected)
    {
        TextNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_TabsAndNewlinesSeparateWordsRatherThanBeingDropped()
    {
        // These are UnicodeCategory.Control, so an over-eager "strip invisible
        // characters" rule welds the words either side of them together.
        TextNormalizer.Normalize("end of line\nstart of next").Should().Be("end of line start of next");
    }

    [Fact]
    public void Normalize_FoldsLigaturesToPlainLetters()
    {
        // PDF text layers are full of these; U+FB01 is the "fi" ligature.
        TextNormalizer.Normalize("\uFB01nal").Should().Be("final");
    }

    [Fact]
    public void Normalize_FoldsFullWidthCharacters()
    {
        TextNormalizer.Normalize("\uFF34\uFF25\uFF38\uFF34").Should().Be("text");
    }

    [Fact]
    public void Normalize_TreatsComposedAndDecomposedAccentsAsEqual()
    {
        var composed = TextNormalizer.Normalize("caf\u00E9");        // é as a single code point
        var decomposed = TextNormalizer.Normalize("cafe\u0301");     // e + combining acute

        composed.Should().Be("caf\u00E9");
        decomposed.Should().Be(composed, "the same word typed two ways must compare equal");
    }

    [Fact]
    public void Normalize_DropsZeroWidthCharactersRatherThanSplittingWords()
    {
        // Inserting a zero-width space mid-word is a cheap way to defeat a naive
        // detector, so it is removed outright rather than turned into a separator.
        TextNormalizer.Normalize("plag\u200Biarism").Should().Be("plagiarism");
    }

    [Fact]
    public void Normalize_DropsControlCharactersWithoutSplittingWords()
    {
        TextNormalizer.Normalize("text\u0001with\u0002controls").Should().Be("textwithcontrols");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public void Normalize_InputWithNothingToKeep_ReturnsEmpty(string? input)
    {
        TextNormalizer.Normalize(input).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("one", 1)]
    [InlineData("one two three", 3)]
    public void CountWords_CountsSpaceSeparatedTokens(string? input, int expected)
    {
        TextNormalizer.CountWords(input).Should().Be(expected);
    }

    [Fact]
    public void CountWords_MatchesWhatNormalizeProduces()
    {
        var normalized = TextNormalizer.Normalize("  The quick, brown fox -- jumped!  ");

        normalized.Should().Be("the quick brown fox jumped");
        TextNormalizer.CountWords(normalized).Should().Be(5);
    }
}
