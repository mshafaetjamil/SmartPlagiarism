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
    private const char Virama = '্';
    private const char Nukta = '়';
    private const char SignAa = 'া';
    private const char SignE = 'ে';
    private const char SignO = 'ো';
    private const char SignAu = 'ৌ';
    private const char AuLengthMark = 'ৗ';

    public static bool IsBengali(char character) => character is >= 'ঀ' and <= '৿';

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

        if (!IsInDrawingOrder(text))
        {
            return text;
        }

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
            index = clusterEnd;

            // ো and ৌ are drawn in two halves around the consonant: ে before it,
            // া or ৗ after. Rejoin them into the single vowel sign that was typed.
            if (character == SignE && index < text.Length && text[index] == SignAa)
            {
                repaired.Append(SignO);
                index++;
            }
            else if (character == SignE && index < text.Length && text[index] == AuLengthMark)
            {
                repaired.Append(SignAu);
                index++;
            }
            else
            {
                repaired.Append(character);
            }
        }

        return repaired.ToString();
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

    private static bool IsPreBaseVowelSign(char character) => character is 'ি' or 'ে' or 'ৈ';

    private static bool IsConsonant(char character) =>
        character is (>= 'ক' and <= 'হ') or 'ৎ' or 'ড়' or 'ঢ়' or 'য়' or 'ৰ' or 'ৱ';

    /// <summary>Signs that attach to a preceding letter and cannot start a word.</summary>
    private static bool IsDependentSign(char character) =>
        character is (>= 'ঁ' and <= 'ঃ')
            or Nukta
            or (>= 'া' and <= '্')
            or AuLengthMark
            or 'ৢ' or 'ৣ';
}
