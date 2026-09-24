using System.Text;

namespace SmartPlagiarism.Engine.Text;

/// <summary>
/// How trustworthy a piece of Bengali text is.
/// </summary>
/// <param name="BengaliWords">Whitespace-separated words containing Bengali letters.</param>
/// <param name="SuspectWords">
/// Words that cannot occur in correctly encoded text: a vowel sign with no letter
/// before it, or stray ASCII wedged between Bengali characters where a PDF font
/// had no Unicode mapping for a glyph.
/// </param>
/// <param name="ConjunctWords">Words containing a virama, i.e. a conjunct.</param>
public readonly record struct BengaliTextAssessment(int BengaliWords, int SuspectWords, int ConjunctWords)
{
    /// <summary>Below this many Bengali words there is too little to judge.</summary>
    public const int MinimumWordsToJudge = 3;

    /// <summary>
    /// Real Bengali prose has conjuncts in a fifth to a third of its words. Text
    /// this long with almost none has had its conjunct glyphs silently dropped.
    /// </summary>
    public const int MinimumWordsForConjunctCheck = 40;

    public double SuspectRatio => BengaliWords == 0 ? 0 : (double)SuspectWords / BengaliWords;

    public double ConjunctRatio => BengaliWords == 0 ? 0 : (double)ConjunctWords / BengaliWords;

    public bool IsDamaged =>
        BengaliWords >= MinimumWordsToJudge
        && (SuspectRatio > 0.05
            || (BengaliWords >= MinimumWordsForConjunctCheck && ConjunctRatio < 0.04));
}

/// <summary>
/// Bengali-script repair and quality checks for text that came out of a PDF.
///
/// A PDF records glyphs in the order they are drawn, not the order they were
/// typed. Bengali draws the vowel signs ি, ে and ৈ to the <em>left</em> of their
/// consonant, so extraction yields "িক" where the writer typed "কি" - and
/// conjunct glyphs (ক্ত, ন্ত, র্) often have no Unicode mapping at all and come
/// out as stray ASCII or vanish. Unicode text from DOCX and PPTX never has these
/// problems; this exists for PDFs only.
/// </summary>
public static class BengaliText
{
    internal const char Virama = '\u09CD';
    internal const char Nukta = '\u09BC';
    private const char Chandrabindu = '\u0981';
    private const char SignAa = '\u09BE';
    private const char SignE = '\u09C7';
    private const char SignO = '\u09CB';
    private const char SignAu = '\u09CC';
    private const char AuLengthMark = '\u09D7';

    public static bool IsBengali(char character) => character is >= '\u0980' and <= '\u09FF';

    /// <summary>Whether the text has enough Bengali in it to need Bengali handling.</summary>
    public static bool ContainsBengali(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var bengali = 0;

        foreach (var character in text)
        {
            if (IsBengali(character) && ++bengali >= 10)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Puts vowel signs extracted in drawing order back into typing order:
    /// "িক" becomes "কি", "েকা" becomes "কো".
    ///
    /// Only applied when the text is demonstrably in drawing order - see
    /// <see cref="IsInDrawingOrder"/> - because the same local sequence is
    /// ambiguous: in "কেক" the ে belongs to the first ক when typed, but to the
    /// second when drawn. Correct text is returned unchanged.
    /// </summary>
    public static string RepairDrawingOrder(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return IsInDrawingOrder(text) ? MoveVowelSignsAfterConsonants(text) : text;
    }

    /// <summary>
    /// The unconditional part of <see cref="RepairDrawingOrder"/>, for text known
    /// to be in drawing order - such as converted Bijoy, which is always typed
    /// that way.
    /// </summary>
    internal static string MoveVowelSignsAfterConsonants(string text)
    {
        var repaired = new StringBuilder(text.Length);
        var index = 0;

        while (index < text.Length)
        {
            var character = text[index];

            if (!IsPreBaseVowelSign(character) || index + 1 >= text.Length || !IsConsonant(text[index + 1]))
            {
                repaired.Append(character);
                index++;
                continue;
            }

            var clusterEnd = EndOfConsonantCluster(text, index + 1);
            repaired.Append(text, index + 1, clusterEnd - index - 1);
            index = AppendMovedSign(repaired, character, text, clusterEnd);
        }

        return repaired.ToString();
    }

    /// <summary>
    /// Appends a pre-base sign that has been moved past its consonants.
    ///
    /// ো and ৌ are drawn in two halves around the consonant - ে before it, া or ৗ
    /// after - so a ে followed by either half is rejoined into the single sign
    /// that was typed. A chandrabindu sometimes sits between the consonant and the
    /// second half; it belongs after the whole sign.
    /// </summary>
    /// <returns>The index after everything consumed from <paramref name="text"/>.</returns>
    private static int AppendMovedSign(StringBuilder repaired, char sign, string text, int index)
    {
        if (sign != SignE)
        {
            repaired.Append(sign);
            return index;
        }

        var skipped = index + 1 < text.Length && text[index] == Chandrabindu ? 1 : 0;
        var secondHalf = index + skipped < text.Length ? text[index + skipped] : '\0';

        var joined = secondHalf switch
        {
            SignAa => SignO,
            AuLengthMark => SignAu,
            _ => '\0',
        };

        if (joined == '\0')
        {
            repaired.Append(sign);
            return index;
        }

        repaired.Append(joined);

        if (skipped == 1)
        {
            repaired.Append(Chandrabindu);
        }

        return index + skipped + 1;
    }

    /// <summary>
    /// In typed text a pre-base vowel sign always follows its consonant, so it can
    /// never begin a word. Drawn text puts it first in a large share of words.
    /// Requiring a fifth of them keeps a stray typo in correct text from
    /// triggering a repair that would then corrupt everything else.
    /// </summary>
    public static bool IsInDrawingOrder(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var total = 0;
        var leading = 0;

        for (var index = 0; index < text.Length; index++)
        {
            if (!IsPreBaseVowelSign(text[index]))
            {
                continue;
            }

            total++;

            if (index == 0 || !IsBengali(text[index - 1]) || IsDependentSign(text[index - 1]))
            {
                leading++;
            }
        }

        return leading > 0 && leading * 5 >= total;
    }

    /// <summary>Counts the signs of damage that survive <see cref="RepairDrawingOrder"/>.</summary>
    public static BengaliTextAssessment Assess(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return default;
        }

        var bengaliWords = 0;
        var suspectWords = 0;
        var conjunctWords = 0;

        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            // Any Bengali character counts, signs included: a "word" made only of
            // orphaned vowel signs is the worst damage there is, not a non-word.
            if (!word.Any(IsBengali))
            {
                continue;
            }

            bengaliWords++;

            if (word.Contains(Virama))
            {
                conjunctWords++;
            }

            if (IsSuspect(word))
            {
                suspectWords++;
            }
        }

        return new BengaliTextAssessment(bengaliWords, suspectWords, conjunctWords);
    }

    private static bool IsSuspect(string word)
    {
        var start = 0;

        // Leading quotes and brackets are ordinary punctuation, not damage.
        while (start < word.Length && !IsBengali(word[start]) && !char.IsLetterOrDigit(word[start]))
        {
            start++;
        }

        if (start < word.Length && IsDependentSign(word[start]))
        {
            return true;
        }

        for (var index = start + 1; index < word.Length - 1; index++)
        {
            // ASCII wedged between Bengali characters is an unmapped glyph. Hyphen
            // and slash are left alone: "মা-বাবা" and "এবং/অথবা" are real text.
            if (word[index] is > ' ' and < '\u007F' and not '-' and not '/'
                && IsBengali(word[index - 1])
                && IsBengali(word[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// End (exclusive) of the consonant cluster starting at
    /// <paramref name="start"/>: a consonant with optional nukta, then any number
    /// of virama-joined consonants - "ক্ষ", "স্ত্র".
    /// </summary>
    private static int EndOfConsonantCluster(string text, int start)
    {
        var index = start + 1;

        if (index < text.Length && text[index] == Nukta)
        {
            index++;
        }

        while (index + 1 < text.Length && text[index] == Virama && IsConsonant(text[index + 1]))
        {
            index += 2;

            if (index < text.Length && text[index] == Nukta)
            {
                index++;
            }
        }

        return index;
    }

    private static bool IsPreBaseVowelSign(char character) => character is '\u09BF' or '\u09C7' or '\u09C8';

    internal static bool IsConsonant(char character) =>
        character is (>= '\u0995' and <= '\u09B9') or '\u09CE' or '\u09DC' or '\u09DD' or '\u09DF' or '\u09F0' or '\u09F1';

    /// <summary>Signs that attach to a preceding letter and cannot start a word.</summary>
    internal static bool IsDependentSign(char character) =>
        character is (>= '\u0981' and <= '\u0983')
            or Nukta
            or (>= '\u09BE' and <= '\u09CD')
            or AuLengthMark
            or '\u09E2' or '\u09E3';
}
