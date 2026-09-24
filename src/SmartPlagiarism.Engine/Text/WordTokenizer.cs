using System.Text;

namespace SmartPlagiarism.Engine.Text;

/// <summary>
/// Splits original text into normalised words that still know where they came
/// from.
///
/// This deliberately does not normalise the whole string first.
/// <see cref="TextNormalizer"/> changes length - the "fi" ligature becomes two
/// characters, full-width forms collapse - so offsets taken against normalised
/// text would not line up with the document a reader sees. Instead the scan runs
/// over the original, and normalisation is applied per word while the word's own
/// span is recorded.
///
/// Word boundaries are <see cref="TextNormalizer"/>'s own rule, so the two agree:
/// "wasn't" becomes "wasn" and "t" in both, while Bengali vowel signs and
/// zero-width joiners stay inside the word they belong to.
/// </summary>
public static class WordTokenizer
{
    public static IReadOnlyList<Token> Tokenize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var tokens = new List<Token>();
        var word = new StringBuilder();
        var start = -1;

        for (var index = 0; index < text.Length; index++)
        {
            if (!TextNormalizer.IsWordSeparator(text[index]))
            {
                if (start < 0)
                {
                    start = index;
                }

                word.Append(text[index]);
                continue;
            }

            if (start >= 0)
            {
                AddToken(tokens, word, start, index - start);
                start = -1;
            }
        }

        if (start >= 0)
        {
            AddToken(tokens, word, start, text.Length - start);
        }

        return tokens;
    }

    private static void AddToken(List<Token> tokens, StringBuilder word, int start, int length)
    {
        // Normalising the single word applies the same folding as the full-text
        // pipeline - ligatures, full-width forms, casing, dropping invisible
        // characters - without disturbing the span, which is measured against the
        // original. A run made only of invisible characters normalises to nothing
        // and is skipped.
        var value = TextNormalizer.Normalize(word.ToString());

        if (value.Length > 0)
        {
            tokens.Add(new Token(value, start, length));
        }

        word.Clear();
    }
}
