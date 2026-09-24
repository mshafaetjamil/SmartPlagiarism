using FluentAssertions;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Tests.Normalization;

/// <summary>
/// The tokenizer must agree with <see cref="TextNormalizer"/> on what a word is,
/// and its offsets must point into the original text, or highlighting lands on
/// the wrong characters.
///
/// Unicode cases use escapes for the same reason as the normalizer tests.
/// </summary>
public class WordTokenizerTests
{
    // "বাংলা ভাষা" - the vowel sign U+09BE is a spacing combining mark, not a letter.
    private const string Bengali = "বাংলা ভাষা";

    [Theory]
    [InlineData("The Quick, brown FOX!")]
    [InlineData("wasn't well-known")]
    [InlineData("ﬁnd the ＡＢＣ")]
    [InlineData("plagi​arism check")]
    [InlineData(Bengali)]
    public void Tokenize_AgreesWithNormalizer(string text)
    {
        var joined = string.Join(' ', WordTokenizer.Tokenize(text).Select(token => token.Value));

        joined.Should().Be(TextNormalizer.Normalize(text));
    }

    [Fact]
    public void Tokenize_RecordsOffsetsIntoOriginalText()
    {
        var tokens = WordTokenizer.Tokenize("Hello, World!");

        tokens.Should().Equal(new Token("hello", 0, 5), new Token("world", 7, 5));
    }

    [Fact]
    public void Tokenize_KeepsBengaliVowelSignsInsideTheWord()
    {
        var tokens = WordTokenizer.Tokenize(Bengali);

        tokens.Should().HaveCount(2);
        tokens[0].Should().Be(new Token("বাংলা", 0, 5));
        tokens[1].Should().Be(new Token("ভাষা", 6, 4));
    }

    [Fact]
    public void Tokenize_ZeroWidthSpaceDoesNotSplitAWord()
    {
        var tokens = WordTokenizer.Tokenize("plagi​arism");

        tokens.Should().Equal(new Token("plagiarism", 0, 11));
    }

    [Fact]
    public void Tokenize_LigatureSpanCoversOriginalCharacters()
    {
        // "ﬁnd" is three characters in the source but "find" once folded.
        var tokens = WordTokenizer.Tokenize("ﬁnd");

        tokens.Should().Equal(new Token("find", 0, 3));
    }
}
