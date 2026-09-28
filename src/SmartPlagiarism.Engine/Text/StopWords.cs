namespace SmartPlagiarism.Engine.Text;

/// <summary>
/// English and Bangla function words - "the", "of", "এবং", "করা" - left out when
/// documents are compared by vocabulary.
///
/// Every text in a language shares them, so a vocabulary measure that counts
/// them finds any two English essays substantially similar: two independently
/// written essays on one topic scored 45% on TF-IDF cosine with them included.
/// They stay in n-grams, where word order already stops them matching on their
/// own and where they are part of any copied sentence.
///
/// Stored through <see cref="TextNormalizer"/>, so a word here matches the token
/// the tokenizer makes of it.
/// </summary>
public static class StopWords
{
    private static readonly HashSet<string> s_words = new(
        English.Concat(Bangla).Select(word => TextNormalizer.Normalize(word)),
        StringComparer.Ordinal);

    /// <param name="token">A token as <see cref="WordTokenizer"/> produces it.</param>
    public static bool Contains(string token) => s_words.Contains(token);

    private static string[] English =>
    [
        "a", "about", "above", "after", "again", "against", "al", "all", "also", "am", "an", "and",
        "any", "are", "aren", "as", "at", "be", "because", "been", "before", "being", "below", "between",
        "both", "but", "by", "can", "could", "couldn", "d", "did", "didn", "do", "does", "doesn",
        "doing", "don", "down", "during", "each", "eg", "et", "etc", "few", "for", "from", "further",
        "had", "hadn", "has", "hasn", "have", "haven", "having", "he", "her", "here", "hers", "herself",
        "him", "himself", "his", "how", "i", "ie", "if", "in", "into", "is", "isn", "it",
        "its", "itself", "just", "ll", "m", "may", "me", "might", "more", "most", "must", "mustn",
        "my", "myself", "no", "nor", "not", "now", "o", "of", "off", "on", "once", "only",
        "or", "other", "our", "ours", "ourselves", "out", "over", "own", "per", "re", "s", "same",
        "shall", "shan", "she", "should", "shouldn", "so", "some", "such", "t", "than", "that", "the",
        "their", "theirs", "them", "themselves", "then", "there", "these", "they", "this", "those", "through", "to",
        "too", "under", "until", "up", "upon", "ve", "very", "via", "was", "wasn", "we", "were",
        "weren", "what", "when", "where", "which", "while", "who", "whom", "why", "will", "with", "won",
        "would", "wouldn", "y", "you", "your", "yours", "yourself", "yourselves",
    ];

    private static string[] Bangla =>
    [
        "\u098F\u09AC\u0982", "\u0993", "\u098F", "\u098F\u0987", "\u098F\u099F\u09BF", "\u098F\u099F\u09BE",
        "\u0993\u0987", "\u0993\u099F\u09BE", "\u09B8\u09C7", "\u09B8\u09C7\u0987", "\u09A4\u09BE", "\u09A4\u09BE\u09B0",
        "\u09A4\u09BE\u0981\u09B0", "\u09A4\u09BE\u09B0\u09BE", "\u09A4\u09BE\u0981\u09B0\u09BE", "\u09A4\u09BE\u09A6\u09C7\u09B0", "\u09A4\u09BE\u0981\u09A6\u09C7\u09B0", "\u09A4\u09BE\u0995\u09C7",
        "\u09A4\u09BE\u0981\u0995\u09C7", "\u09A4\u09BF\u09A8\u09BF", "\u0986\u09AE\u09BF", "\u0986\u09AE\u09B0\u09BE", "\u0986\u09AE\u09BE\u09B0", "\u0986\u09AE\u09BE\u09A6\u09C7\u09B0",
        "\u09A4\u09C1\u09AE\u09BF", "\u09A4\u09CB\u09AE\u09BE\u09B0", "\u09A4\u09CB\u09AE\u09B0\u09BE", "\u0986\u09AA\u09A8\u09BF", "\u0986\u09AA\u09A8\u09BE\u09B0", "\u0986\u09AA\u09A8\u09BE\u09B0\u09BE",
        "\u09AF\u09C7", "\u09AF\u09BE", "\u09AF\u09BE\u09B0", "\u09AF\u09BE\u09A6\u09C7\u09B0", "\u09AF\u09BE\u0995\u09C7", "\u09AF\u09BF\u09A8\u09BF",
        "\u09AF\u09BE\u09B0\u09BE", "\u09AF\u09C7\u09AE\u09A8", "\u09AF\u0996\u09A8", "\u09A4\u0996\u09A8", "\u098F\u0996\u09A8", "\u09AF\u09A6\u09BF",
        "\u09A4\u09AC\u09C7", "\u09A4\u09BE\u0987", "\u0995\u09BE\u09B0\u09A3", "\u0995\u09BF\u09A8\u09CD\u09A4\u09C1", "\u0985\u09A5\u09AC\u09BE", "\u09AC\u09BE",
        "\u0986\u09B0", "\u09A8\u09BE", "\u09A8\u09AF\u09BC", "\u09A8\u09C7\u0987", "\u0995\u09BF", "\u0995\u09C0",
        "\u0995\u09CB\u09A8", "\u0995\u09CB\u09A8\u09CB", "\u0995\u09C7\u0989", "\u0995\u09BF\u099B\u09C1", "\u09B8\u09AC", "\u09B8\u0995\u09B2",
        "\u09B8\u09AC\u09BE\u0987", "\u0985\u09A8\u09C7\u0995", "\u0995\u09AF\u09BC\u09C7\u0995", "\u09AA\u09CD\u09B0\u09A4\u09BF", "\u09AA\u09CD\u09B0\u09BE\u09AF\u09BC", "\u09B6\u09C1\u09A7\u09C1",
        "\u0996\u09C1\u09AC", "\u0986\u09B0\u0993", "\u0986\u09AC\u09BE\u09B0", "\u0987", "\u09A4\u09CB", "\u09A5\u09C7\u0995\u09C7",
        "\u099C\u09A8\u09CD\u09AF", "\u09B8\u0999\u09CD\u0997\u09C7", "\u09B8\u09BE\u09A5\u09C7", "\u09AE\u09A7\u09CD\u09AF\u09C7", "\u0989\u09AA\u09B0", "\u09A8\u09BF\u099A\u09C7",
        "\u09AA\u09B0\u09C7", "\u0986\u0997\u09C7", "\u09A6\u09BF\u0995\u09C7", "\u09A6\u09CD\u09AC\u09BE\u09B0\u09BE", "\u09B9\u09A4\u09C7", "\u09AA\u09B0\u09CD\u09AF\u09A8\u09CD\u09A4",
        "\u09A8\u09BF\u09AF\u09BC\u09C7", "\u09A6\u09BF\u09AF\u09BC\u09C7", "\u0995\u09B0\u09C7", "\u09B9\u09AF\u09BC\u09C7", "\u09AC\u09B2\u09C7", "\u09AE\u09A4\u09CB",
        "\u09B9\u09BF\u09B8\u09C7\u09AC\u09C7", "\u09AD\u09BE\u09AC\u09C7", "\u098F\u09B0", "\u0993\u09B0", "\u098F\u0995", "\u098F\u0995\u099F\u09BF",
        "\u098F\u0995\u099F\u09BE", "\u09A6\u09C1\u0987", "\u0995\u09B0\u09BE", "\u0995\u09B0\u09C7\u09A8", "\u0995\u09B0\u09C7\u099B\u09C7", "\u0995\u09B0\u09C7\u099B\u09C7\u09A8",
        "\u0995\u09B0\u09A4\u09C7", "\u0995\u09B0\u09AC\u09C7", "\u0995\u09B0\u099B\u09C7", "\u09B9\u09AF\u09BC", "\u09B9\u09AF\u09BC\u09C7\u099B\u09C7", "\u09B9\u09AF\u09BC\u09C7\u099B\u09BF\u09B2",
        "\u09B9\u09AC\u09C7", "\u09B9\u099A\u09CD\u099B\u09C7", "\u09B9\u09B2\u09CB", "\u09B9\u09B2", "\u09B9\u09B2\u09C7", "\u09B9\u0993\u09AF\u09BC\u09BE",
        "\u099B\u09BF\u09B2", "\u099B\u09BF\u09B2\u09C7\u09A8", "\u0986\u099B\u09C7", "\u0986\u099B\u09C7\u09A8", "\u09B0\u09AF\u09BC\u09C7\u099B\u09C7", "\u09A5\u09BE\u0995\u09C7",
        "\u09A5\u09BE\u0995\u09BE", "\u09A5\u09BE\u0995\u09AC\u09C7", "\u09AF\u09BE\u09AF\u09BC", "\u09AF\u09BE\u09AC\u09C7", "\u0997\u09C7\u099B\u09C7", "\u0997\u09BF\u09AF\u09BC\u09C7",
        "\u09A6\u09C7\u09AF\u09BC", "\u09A6\u09C7\u0993\u09AF\u09BC\u09BE", "\u09A6\u09BF\u09A4\u09C7", "\u09A8\u09C7\u09AF\u09BC", "\u09A8\u09C7\u0993\u09AF\u09BC\u09BE", "\u09AA\u09BE\u09B0\u09C7",
        "\u09AA\u09BE\u09B0\u09C7\u09A8", "\u09AA\u09BE\u09B0\u09AC\u09C7", "\u098F\u0996\u09BE\u09A8\u09C7", "\u09B8\u09C7\u0996\u09BE\u09A8\u09C7", "\u09AF\u09C7\u0996\u09BE\u09A8\u09C7", "\u098F\u09AE\u09A8",
        "\u0985\u09A8\u09CD\u09AF", "\u0985\u09A8\u09CD\u09AF\u09BE\u09A8\u09CD\u09AF", "\u09A8\u09BF\u099C\u09C7\u09B0", "\u09A8\u09BF\u099C",
    ];
}
