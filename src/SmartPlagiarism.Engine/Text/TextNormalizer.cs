using System.Globalization;
using System.Text;

namespace SmartPlagiarism.Engine.Text;

/// <summary>
/// Reduces extracted text to a canonical form for comparison, so that formatting
/// differences do not hide copied wording.
///
/// The pipeline, in order:
/// <list type="number">
///   <item><description>Unicode normalisation to form KC, folding ligatures ("ﬁ" to "fi") and full-width characters onto their plain equivalents.</description></item>
///   <item><description>Invariant lowercasing.</description></item>
///   <item><description>Punctuation and symbols replaced by a space, so "well-known" and "well known" match.</description></item>
///   <item><description>Whitespace runs collapsed to a single space, and the result trimmed.</description></item>
/// </list>
/// Invisible control and formatting characters are dropped entirely rather than
/// turned into spaces - a zero-width space inside a word would otherwise split it
/// in two and defeat the comparison.
///
/// Deliberately not done: joining words hyphenated across a line break. It would
/// help PDF matching, but it also corrupts genuine hyphenated compounds, and it
/// belongs with the similarity engine's tokenizer rather than here.
/// </summary>
public static class TextNormalizer
{
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var folded = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

        var builder = new StringBuilder(folded.Length);
        var pendingSpace = false;

        foreach (var character in folded)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);

            // Whitespace is tested before the invisible-character check on purpose:
            // tab, carriage return and line feed are all UnicodeCategory.Control, and
            // dropping them outright would weld the last word of one line onto the
            // first word of the next.
            if (char.IsWhiteSpace(character) || IsPunctuationOrSymbol(category))
            {
                // Only emit a separator once something follows it, which trims both
                // ends and collapses runs in a single pass.
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (IsInvisible(category))
            {
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(character);
        }

        return builder.ToString();
    }

    /// <summary>Words in an already-normalised string.</summary>
    public static int CountWords(string? normalizedText)
    {
        if (string.IsNullOrEmpty(normalizedText))
        {
            return 0;
        }

        var count = 0;
        var insideWord = false;

        foreach (var character in normalizedText)
        {
            if (character == ' ')
            {
                insideWord = false;
                continue;
            }

            if (!insideWord)
            {
                count++;
                insideWord = true;
            }
        }

        return count;
    }

    private static bool IsInvisible(UnicodeCategory category) => category
        is UnicodeCategory.Control
        or UnicodeCategory.Format
        or UnicodeCategory.PrivateUse
        or UnicodeCategory.OtherNotAssigned;

    private static bool IsPunctuationOrSymbol(UnicodeCategory category) => category
        is UnicodeCategory.ConnectorPunctuation
        or UnicodeCategory.DashPunctuation
        or UnicodeCategory.OpenPunctuation
        or UnicodeCategory.ClosePunctuation
        or UnicodeCategory.InitialQuotePunctuation
        or UnicodeCategory.FinalQuotePunctuation
        or UnicodeCategory.OtherPunctuation
        or UnicodeCategory.MathSymbol
        or UnicodeCategory.CurrencySymbol
        or UnicodeCategory.ModifierSymbol
        or UnicodeCategory.OtherSymbol;
}
