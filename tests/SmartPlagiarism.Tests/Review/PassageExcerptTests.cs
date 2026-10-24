using FluentAssertions;
using SmartPlagiarism.Core.Review;

namespace SmartPlagiarism.Tests.Review;

/// <summary>Cutting a document down to its matched passages, for a teacher outside its department.</summary>
public class PassageExcerptTests
{
    private const string Gap = "[…]";

    /// <summary>"w0 w1 w2 ... w399": every word distinct, so any piece of it can be located.</summary>
    private static readonly string s_words = string.Join(" ", Enumerable.Range(0, 400).Select(index => $"w{index}"));

    [Fact]
    public void ADocumentShorterThanTheContext_IsKeptWhole()
    {
        const string text = "A short note whose middle sentence was copied, and nothing more.";
        var span = Span(text, "middle sentence was copied", fragment: 0);

        var (excerpt, moved) = PassageExcerpt.Cut(text, [span]);

        excerpt.Should().Be(text);
        moved.Should().Equal(span);
    }

    [Fact]
    public void FarApartPassages_KeepTheirContext_AndEverythingElseIsLeftOut()
    {
        var first = Span(s_words, "w100 w101 w102", fragment: 0);
        var second = Span(s_words, "w300 w301", fragment: 1);

        var (excerpt, moved) = PassageExcerpt.Cut(s_words, [first, second], contextCharacters: 20);

        excerpt.Split(Gap).Should().HaveCount(4, "a gap before, between and after the two passages");
        excerpt.Should().NotContain("w0 ").And.NotContain("w200 ").And.NotContain("w399");
        PassageText(excerpt, moved[0]).Should().Be("w100 w101 w102");
        PassageText(excerpt, moved[1]).Should().Be("w300 w301");
        moved.Select(span => span.FragmentIndex).Should().Equal(0, 1);
    }

    [Fact]
    public void ACut_FallsBetweenWords()
    {
        var span = Span(s_words, "w200", fragment: 0);

        var (excerpt, _) = PassageExcerpt.Cut(s_words, [span], contextCharacters: 12);

        excerpt.Split(Gap, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Should().ContainSingle()
            .Which.Split(' ').Should().OnlyContain(word => s_words.Split(' ').Contains(word), "no word is cut in half");
    }

    [Fact]
    public void NearbyPassages_ShareOneStretchOfText()
    {
        var first = Span(s_words, "w100", fragment: 0);
        var second = Span(s_words, "w103", fragment: 1);

        var (excerpt, moved) = PassageExcerpt.Cut(s_words, [first, second], contextCharacters: 20);

        excerpt.Split(Gap).Should().HaveCount(3, "one gap before and one after, none between");
        PassageText(excerpt, moved[1]).Should().Be("w103");
    }

    [Fact]
    public void OffsetsBeyondTheText_AreClipped_AndEmptyOnesDropped()
    {
        const string text = "Only the last words matched here";

        var (excerpt, moved) = PassageExcerpt.Cut(text, [new HighlightSpan(19, 500, 0), new HighlightSpan(5, 5, 1)]);

        moved.Should().ContainSingle().Which.Should().Be(new HighlightSpan(19, text.Length, 0));
        excerpt.Should().Be(text);
    }

    [Fact]
    public void NoPassages_ShowNothingOfTheDocument()
    {
        var (excerpt, moved) = PassageExcerpt.Cut(s_words, []);

        excerpt.Should().Be(Gap);
        moved.Should().BeEmpty();
    }

    private static HighlightSpan Span(string text, string passage, int fragment)
    {
        var start = text.IndexOf(passage, StringComparison.Ordinal);
        return new HighlightSpan(start, start + passage.Length, fragment);
    }

    private static string PassageText(string excerpt, HighlightSpan span) => excerpt[span.Start..span.End];
}
