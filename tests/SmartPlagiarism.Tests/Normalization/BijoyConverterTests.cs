using FluentAssertions;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Tests.Normalization;

/// <summary>
/// Bijoy (SutonnyMJ) to Unicode. Each case is a real word as a Bijoy typist
/// enters it, chosen to exercise one rule; the comment shows the intended word.
/// Expected values are escaped where a lone sign would be unreadable.
/// </summary>
public class BijoyConverterTests
{
    [Theory]
    [InlineData("Avwg evsjvq Mvb MvB", "\u0986\u09AE\u09BF \u09AC\u09BE\u0982\u09B2\u09BE\u09AF\u09BC \u0997\u09BE\u09A8 \u0997\u09BE\u0987")]            // plain letters, আ from "Av"
    [InlineData("evsjv\u2021`k", "\u09AC\u09BE\u0982\u09B2\u09BE\u09A6\u09C7\u09B6")]                                   // ে typed before its consonant
    [InlineData("\u2021jvK", "\u09B2\u09CB\u0995")]                                            // ো typed in two halves
    [InlineData("\u2021K\u0160kj", "\u0995\u09CC\u09B6\u09B2")]                                          // ৌ typed in two halves
    [InlineData("wk\u00B6v", "\u09B6\u09BF\u0995\u09CD\u09B7\u09BE")]                                          // conjunct glyph, ি before শ only
    [InlineData("c\u00D6wZ\u2021e`b", "\u09AA\u09CD\u09B0\u09A4\u09BF\u09AC\u09C7\u09A6\u09A8")]                                  // subjoined র, two pre-base signs
    [InlineData("Kg\u00A9", "\u0995\u09B0\u09CD\u09AE")]                                             // reph typed after its consonant
    [InlineData("Kg\u00A9x", "\u0995\u09B0\u09CD\u09AE\u09C0")]                                           // reph before the vowel sign
    [InlineData("Kgx\u00A9", "\u0995\u09B0\u09CD\u09AE\u09C0")]                                           // reph after the vowel sign
    [InlineData("wk\u00B6v_\u00A9x", "\u09B6\u09BF\u0995\u09CD\u09B7\u09BE\u09B0\u09CD\u09A5\u09C0")]                                    // reph inside a longer word
    [InlineData("Kvh\u00A9", "\u0995\u09BE\u09B0\u09CD\u09AF")]                                           // reph on য
    [InlineData("\u00AF\u2019vb", "\u09B8\u09CD\u09A5\u09BE\u09A8")]                                           // two glyphs meeting at a virama
    [InlineData("\u2021\u00AF\u0153n", "\u09B8\u09CD\u09A8\u09C7\u09B9")]                                           // ে moved past a whole conjunct
    [InlineData("e\u00A8vL\u00A8v", "\u09AC\u09CD\u09AF\u09BE\u0996\u09CD\u09AF\u09BE")]                                       // ্য subjoined twice
    [InlineData("Kv\u00A8", "\u0995\u09CD\u09AF\u09BE")]                                             // vowel sign typed before ্য
    [InlineData("Puv`", "\u099A\u09BE\u0981\u09A6")]                                            // chandrabindu typed before া
    [InlineData("2026 mvj|", "\u09E8\u09E6\u09E8\u09EC \u09B8\u09BE\u09B2\u0964")]                                  // digits and the dari
    public void Convert_ProducesTheTypedWord(string bijoy, string unicode)
    {
        BijoyConverter.Convert(bijoy).Should().Be(unicode);
    }

    [Fact]
    public void Join_MergesAWordSplitAcrossRuns()
    {
        // Word split "‡jvK" into two runs; ে must still move past ল.
        var text = BijoyConverter.Join([new FontRun("\u2021", "SutonnyMJ"), new FontRun("jvK", "SutonnyMJ")]);

        text.Should().Be("\u09B2\u09CB\u0995");
    }

    [Fact]
    public void Join_ConvertsOnlyTheBijoyRuns()
    {
        var text = BijoyConverter.Join(
        [
            new FontRun("Avwg ", "SutonnyMJ"),
            new FontRun("Hello 2026", "Times New Roman"),
        ]);

        text.Should().Be("\u0986\u09AE\u09BF Hello 2026", "English and its digits in a Latin font must be left alone");
    }

    [Fact]
    public void Join_LeavesUnicodeInAnMjFontAlone()
    {
        // SutonnyOMJ is a Unicode font despite the name.
        var text = BijoyConverter.Join([new FontRun("\u0986\u09AE\u09BF \u09AC\u09BE\u0982\u09B2\u09BE\u09AF\u09BC", "SutonnyOMJ")]);

        text.Should().Be("\u0986\u09AE\u09BF \u09AC\u09BE\u0982\u09B2\u09BE\u09AF\u09BC");
    }

    [Theory]
    [InlineData("SutonnyMJ", true)]
    [InlineData("SutonnyOMJ", true)]
    [InlineData("ABCDEF+SutonnyMJ-Bold", true)]
    [InlineData("SutonnyMJ,Bold", true)]
    [InlineData("Times New Roman", false)]
    [InlineData("Kalpurush", false)]
    [InlineData("Nirmala UI", false)]
    [InlineData(null, false)]
    public void IsBijoyFont_RecognisesTheMjFamily(string? fontName, bool expected)
    {
        BijoyConverter.IsBijoyFont(fontName).Should().Be(expected);
    }
}
