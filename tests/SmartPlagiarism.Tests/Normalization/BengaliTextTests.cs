using FluentAssertions;
using SmartPlagiarism.Engine.Text;

namespace SmartPlagiarism.Tests.Normalization;

/// <summary>
/// Repair and damage detection for Bengali text taken from PDFs. Code points are
/// escaped: a vowel sign on its own renders as a dotted circle or not at all, so
/// literals would be unreviewable exactly where precision matters.
/// </summary>
public class BengaliTextTests
{
    private const string Ka = "\u0995";
    private const string Ssa = "\u09B7";
    private const string Da = "\u09A6";
    private const string Na = "\u09A8";
    private const string La = "\u09B2";
    private const string Ra = "\u09B0";
    private const string SignI = "\u09BF";
    private const string SignE = "\u09C7";
    private const string SignAa = "\u09BE";
    private const string SignO = "\u09CB";
    private const string SignAu = "\u09CC";
    private const string AuLength = "\u09D7";
    private const string Virama = "\u09CD";

    // ---------------- drawing-order repair ----------------

    [Fact]
    public void Repair_MovesPreBaseVowelSignsAfterTheirConsonant()
    {
        // Drawn: "িক েক" - typed: "কি কে".
        BengaliText.RepairDrawingOrder($"{SignI}{Ka} {SignE}{Ka}")
            .Should().Be($"{Ka}{SignI} {Ka}{SignE}");
    }

    [Fact]
    public void Repair_RejoinsTwoPartVowels()
    {
        BengaliText.RepairDrawingOrder($"{SignE}{Ka}{SignAa} {SignE}{Ka}{AuLength}")
            .Should().Be($"{Ka}{SignO} {Ka}{SignAu}");
    }

    [Fact]
    public void Repair_MovesTheSignPastAWholeConjunct()
    {
        // Drawn "িক্ষ" - typed "ক্ষি": the sign belongs after the full cluster.
        BengaliText.RepairDrawingOrder($"{SignI}{Ka}{Virama}{Ssa}")
            .Should().Be($"{Ka}{Virama}{Ssa}{SignI}");
    }

    [Fact]
    public void Repair_HandlesSignsThatFollowAnotherSyllable()
    {
        // "দনেলােক" drawn - typed "দনলোকে": neither ে starts a word, but the
        // text is in drawing order, so both move.
        BengaliText.RepairDrawingOrder($"{Da}{Na}{SignE}{La}{SignAa}{SignE}{Ka}")
            .Should().Be($"{Da}{Na}{La}{SignO}{Ka}{SignE}");
    }

    [Fact]
    public void Repair_LeavesCorrectlyTypedTextAlone()
    {
        var typed = $"{Ka}{SignI} {Ka}{SignE}{Ka} {Da}{SignO}{Ra}";

        BengaliText.RepairDrawingOrder(typed).Should().Be(typed);
    }

    [Fact]
    public void Repair_OneTypoDoesNotFlipCorrectTextIntoDrawingOrder()
    {
        // Five correct "কি" and one stray leading sign: repairing would corrupt
        // the five to fix the one.
        var typed = string.Join(' ', Enumerable.Repeat($"{Ka}{SignI}", 5)) + $" {SignI}{Ka}";

        BengaliText.IsInDrawingOrder(typed).Should().BeFalse();
        BengaliText.RepairDrawingOrder(typed).Should().Be(typed);
    }

    // ---------------- damage assessment ----------------

    [Fact]
    public void Assess_CleanTypedProse_IsNotDamaged()
    {
        const string prose =
            "প্লেজিয়ারিজম শনাক্তকরণ ব্যবস্থা শিক্ষার্থীদের জমা দেওয়া প্রতিবেদনগুলোকে পূর্বের সকল জমার সাথে "
            + "তুলনা করে এবং মিলে যাওয়া অংশগুলো চিহ্নিত করে। মা-বাবা এবং/অথবা শিক্ষক।";

        BengaliText.Assess(prose).IsDamaged.Should().BeFalse();
    }

    [Fact]
    public void Assess_WordsStartingWithAVowelSign_AreSuspect()
    {
        var assessment = BengaliText.Assess($"{SignAa}{Ka} {Ka}{SignI} {Ka}{SignE}");

        assessment.BengaliWords.Should().Be(3);
        assessment.SuspectWords.Should().Be(1);
        assessment.IsDamaged.Should().BeTrue();
    }

    [Fact]
    public void Assess_WordsOfNothingButSigns_CountAsDamagedBangla()
    {
        var assessment = BengaliText.Assess($"{SignI}{SignAa} {SignI}{SignAa} {SignI}{SignAa}");

        assessment.BengaliWords.Should().Be(3);
        assessment.SuspectWords.Should().Be(3);
    }

    [Fact]
    public void Assess_AsciiWedgedInsideABengaliWord_IsAnUnmappedGlyph()
    {
        // "শাথী4েদর" - the 4 stood for a conjunct the PDF font could not name.
        var assessment = BengaliText.Assess($"শ{SignAa}থী4{SignE}{Da}{Ra} {Ka}{SignI} {Ka}{SignE}");

        assessment.SuspectWords.Should().Be(1);
    }

    [Fact]
    public void Assess_LongTextWithNoConjunctsAtAll_HasLostThem()
    {
        var withoutConjuncts = string.Join(' ', Enumerable.Repeat($"{Ka}{SignI}{Ra}", 50));
        var withConjuncts = string.Join(
            ' ',
            Enumerable.Range(0, 50).Select(index => index % 4 == 0 ? $"{Ka}{Virama}{Ssa}" : $"{Ka}{SignI}{Ra}"));

        BengaliText.Assess(withoutConjuncts).IsDamaged.Should().BeTrue();
        BengaliText.Assess(withConjuncts).IsDamaged.Should().BeFalse();
    }

    [Fact]
    public void Assess_EnglishText_HasNoBengaliToJudge()
    {
        var assessment = BengaliText.Assess("An entirely English sentence, with 4 numbers and a/b.");

        assessment.BengaliWords.Should().Be(0);
        assessment.IsDamaged.Should().BeFalse();
    }

    [Theory]
    [InlineData("plain English text only", false)]
    [InlineData("\u0995\u09BF", false)]
    [InlineData("\u0995\u09BF\u0995\u09BF\u0995\u09BF\u0995\u09BF\u0995\u09BF", true)]
    public void ContainsBengali_NeedsMoreThanAStrayCharacter(string text, bool expected)
    {
        BengaliText.ContainsBengali(text).Should().Be(expected);
    }
}
