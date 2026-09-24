using System.Text;

namespace SmartPlagiarism.Engine.Text;

/// <summary>A stretch of text as a document stores it, with the font it is set in.</summary>
public readonly record struct FontRun(string Text, string? FontName);

/// <summary>
/// Converts Bijoy-encoded Bangla to Unicode.
///
/// Bijoy predates Unicode Bangla and is still common in Bangladesh: text is typed
/// as ordinary Latin characters and a font such as SutonnyMJ draws them as Bangla
/// glyphs. The file therefore holds "Avwg evsjvq" where the reader sees
/// "আমি বাংলায়", so without conversion a Bijoy submission can never match a
/// Unicode one, however much was copied.
///
/// Conversion runs in three steps:
/// <list type="number">
///   <item><description>
///     Each Bijoy code maps to its Unicode letters. Many codes are whole
///     conjuncts ("³" is ক্ত) or subjoined forms ("¨" is ্য).
///   </description></item>
///   <item><description>
///     The reph (র্), which Bijoy types <em>after</em> the consonant it sits
///     on, moves in front of that consonant cluster.
///   </description></item>
///   <item><description>
///     Pre-base vowel signs, which Bijoy types <em>before</em> their consonant,
///     move after it - the same repair PDFs need, see
///     <see cref="BengaliText.MoveVowelSignsAfterConsonants"/>.
///   </description></item>
/// </list>
///
/// The glyph table is adapted from bijoy-to-unicode-file-converter 1.2 (MIT
/// License, Copyright (c) 2021 Al Mehady; see THIRD-PARTY-NOTICES.md). The
/// reordering is this project's own.
/// </summary>
public static class BijoyConverter
{
    /// <summary>
    /// Holds the reph's place between mapping and reordering. A private-use
    /// character, so it cannot collide with anything in real text - and the reph
    /// cannot simply be written as "র্" early, because "র" followed by "্য" (from
    /// "i¨") looks identical and must not move.
    /// </summary>
    private const char RephMarker = '\uE000';

    private const char RephCode = '\u00A9';

    private const string Reph = "\u09B0\u09CD";

    private static readonly Dictionary<string, string> s_glyphs = new(StringComparer.Ordinal)
    {
        ["Av"] = "\u0986", // Av আ
        ["A"] = "\u0985", // A অ
        ["B"] = "\u0987", // B ই
        ["C"] = "\u0988", // C ঈ
        ["D"] = "\u0989", // D উ
        ["E"] = "\u098A", // E ঊ
        ["F"] = "\u098B", // F ঋ
        ["G"] = "\u098F", // G এ
        ["H"] = "\u0990", // H ঐ
        ["I"] = "\u0993", // I ও
        ["J"] = "\u0994", // J ঔ
        ["K"] = "\u0995", // K ক
        ["L"] = "\u0996", // L খ
        ["M"] = "\u0997", // M গ
        ["N"] = "\u0998", // N ঘ
        ["O"] = "\u0999", // O ঙ
        ["P"] = "\u099A", // P চ
        ["Q"] = "\u099B", // Q ছ
        ["R"] = "\u099C", // R জ
        ["S"] = "\u099D", // S ঝ
        ["T"] = "\u099E", // T ঞ
        ["U"] = "\u099F", // U ট
        ["V"] = "\u09A0", // V ঠ
        ["W"] = "\u09A1", // W ড
        ["X"] = "\u09A2", // X ঢ
        ["Y"] = "\u09A3", // Y ণ
        ["Z"] = "\u09A4", // Z ত
        ["_"] = "\u09A5", // _ থ
        ["`"] = "\u09A6", // ` দ
        ["a"] = "\u09A7", // a ধ
        ["b"] = "\u09A8", // b ন
        ["c"] = "\u09AA", // c প
        ["d"] = "\u09AB", // d ফ
        ["e"] = "\u09AC", // e ব
        ["f"] = "\u09AD", // f ভ
        ["g"] = "\u09AE", // g ম
        ["h"] = "\u09AF", // h য
        ["i"] = "\u09B0", // i র
        ["j"] = "\u09B2", // j ল
        ["k"] = "\u09B6", // k শ
        ["l"] = "\u09B7", // l ষ
        ["m"] = "\u09B8", // m স
        ["n"] = "\u09B9", // n হ
        ["o"] = "\u09DC", // o ড়
        ["p"] = "\u09DD", // p ঢ়
        ["q"] = "\u09DF", // q য়
        ["r"] = "\u09CE", // r ৎ
        ["s"] = "\u0982", // s ং
        ["t"] = "\u0983", // t ঃ
        ["u"] = "\u0981", // u ঁ
        ["0"] = "\u09E6", // 0 ০
        ["1"] = "\u09E7", // 1 ১
        ["2"] = "\u09E8", // 2 ২
        ["3"] = "\u09E9", // 3 ৩
        ["4"] = "\u09EA", // 4 ৪
        ["5"] = "\u09EB", // 5 ৫
        ["6"] = "\u09EC", // 6 ৬
        ["7"] = "\u09ED", // 7 ৭
        ["8"] = "\u09EE", // 8 ৮
        ["9"] = "\u09EF", // 9 ৯
        ["\u2022"] = "\u0999\u09CD", // • ঙ্
        ["v"] = "\u09BE", // v া
        ["w"] = "\u09BF", // w ি
        ["x"] = "\u09C0", // x ী
        ["y"] = "\u09C1", // y ু
        ["z"] = "\u09C1", // z ু
        ["\u201C"] = "\u09C1", // “ ু
        ["\u2013"] = "\u09C1", // – ু
        ["~"] = "\u09C2", // ~ ূ
        ["\u0192"] = "\u09C2", // ƒ ূ
        ["\u201A"] = "\u09C2", // ‚ ূ
        ["\u201E\u201E"] = "\u09C3", // „„ ৃ
        ["\u201E"] = "\u09C3", // „ ৃ
        ["\u2026"] = "\u09C3", // … ৃ
        ["\u2020"] = "\u09C7", // † ে
        ["\u2021"] = "\u09C7", // ‡ ে
        ["\u02C6"] = "\u09C8", // ˆ ৈ
        ["\u2030"] = "\u09C8", // ‰ ৈ
        ["\u0160"] = "\u09D7", // Š ৗ
        ["|"] = "\u0964", // | ।
        ["&"] = "\u09CD\u200C", // & ্
        ["^"] = "\u09CD\u09AC", // ^ ্ব
        ["\u2018"] = "\u09CD\u09A4\u09C1", // ‘ ্তু
        ["\u2019"] = "\u09CD\u09A5", // ’ ্থ
        ["\u2039"] = "\u09CD\u0995", // ‹ ্ক
        ["\u0152"] = "\u09CD\u0995\u09CD\u09B0", // Œ ্ক্র
        ["\u201D"] = "\u099A\u09CD", // ” চ্
        ["\u2014"] = "\u09CD\u09A4", // — ্ত
        ["\u02DC"] = "\u09A6\u09CD", // ˜ দ্
        ["\u2122"] = "\u09A6\u09CD", // ™ দ্
        ["\u0161"] = "\u09A8\u09CD", // š ন্
        ["\u203A"] = "\u09A8\u09CD", // › ন্
        ["\u0153"] = "\u09CD\u09A8", // œ ্ন
        ["\u0178"] = "\u09CD\u09AC", // Ÿ ্ব
        ["\u00A1"] = "\u09CD\u09AC", // ¡ ্ব
        ["\u00A2"] = "\u09CD\u09AD", // ¢ ্ভ
        ["\u00A3"] = "\u09CD\u09AD\u09CD\u09B0", // £ ্ভ্র
        ["\u00A4"] = "\u09AE\u09CD", // ¤ ম্
        ["\u00A5"] = "\u09CD\u09AE", // ¥ ্ম
        ["\u00A6"] = "\u09CD\u09AC", // ¦ ্ব
        ["\u00A7"] = "\u09CD\u09AE", // § ্ম
        ["\u00A8"] = "\u09CD\u09AF", // ¨ ্য
        ["\u00AA"] = "\u09CD\u09B0", // ª ্র
        ["\u00AB"] = "\u09CD\u09B0", // « ্র
        ["\u00AC"] = "\u09CD\u09B2", // ¬ ্ল
        ["\u00AD"] = "\u09CD\u09B2", // soft hyphen ্ল
        ["\u00AE"] = "\u09B7\u09CD", // ® ষ্
        ["\u00AF"] = "\u09B8\u09CD", // ¯ স্
        ["\u00B0"] = "\u0995\u09CD\u0995", // ° ক্ক
        ["\u00B1"] = "\u0995\u09CD\u099F", // ± ক্ট
        ["\u00B2"] = "\u0995\u09CD\u09B7\u09CD\u09A3", // ² ক্ষ্ণ
        ["\u00B3"] = "\u0995\u09CD\u09A4", // ³ ক্ত
        ["\u00B4"] = "\u0995\u09CD\u09AE", // ´ ক্ম
        ["\u00B5"] = "\u0995\u09CD\u09B0", // µ ক্র
        ["\u00B6"] = "\u0995\u09CD\u09B7", // ¶ ক্ষ
        ["\u00B7"] = "\u0995\u09CD\u09B8", // · ক্স
        ["\u00B8"] = "\u0997\u09C1", // ¸ গু
        ["\u00B9"] = "\u099C\u09CD\u099E", // ¹ জ্ঞ
        ["\u00BA"] = "\u0997\u09CD\u09A6", // º গ্দ
        ["\u00BB"] = "\u0997\u09CD\u09A7", // » গ্ধ
        ["\u00BC"] = "\u0999\u09CD\u0995", // ¼ ঙ্ক
        ["\u00BD"] = "\u0999\u09CD\u0997", // ½ ঙ্গ
        ["\u00BE"] = "\u099C\u09CD\u099C", // ¾ জ্জ
        ["\u00BF"] = "\u09CD\u09A4\u09CD\u09B0", // ¿ ্ত্র
        ["\u00C0"] = "\u099C\u09CD\u099D", // À জ্ঝ
        ["\u00C1"] = "\u099C\u09CD\u099E", // Á জ্ঞ
        ["\u00C2"] = "\u099E\u09CD\u099A", // Â ঞ্চ
        ["\u00C3"] = "\u099E\u09CD\u099B", // Ã ঞ্ছ
        ["\u00C4"] = "\u099E\u09CD\u099C", // Ä ঞ্জ
        ["\u00C5"] = "\u099E\u09CD\u099D", // Å ঞ্ঝ
        ["\u00C6"] = "\u099F\u09CD\u099F", // Æ ট্ট
        ["\u00C7"] = "\u09A1\u09CD\u09A1", // Ç ড্ড
        ["\u00C8"] = "\u09A3\u09CD\u099F", // È ণ্ট
        ["\u00C9"] = "\u09A3\u09CD\u09A0", // É ণ্ঠ
        ["\u00CA"] = "\u09A3\u09CD\u09A1", // Ê ণ্ড
        ["\u00CB"] = "\u09A4\u09CD\u09A4", // Ë ত্ত
        ["\u00CC"] = "\u09A4\u09CD\u09A5", // Ì ত্থ
        ["\u00CD"] = "\u09A4\u09CD\u09AE", // Í ত্ম
        ["\u00CE"] = "\u09A4\u09CD\u09B0", // Î ত্র
        ["\u00CF"] = "\u09A6\u09CD\u09A6", // Ï দ্দ
        ["\u00D0"] = "-", // Ð -
        ["\u00D1"] = "-", // Ñ -
        ["\u00D2"] = "\u0022", // Ò "
        ["\u00D3"] = "\u0022", // Ó "
        ["\u00D4"] = "'", // Ô '
        ["\u00D5"] = "'", // Õ '
        ["\u00D6"] = "\u09CD\u09B0", // Ö ্র
        ["\u00D7"] = "\u09A6\u09CD\u09A7", // × দ্ধ
        ["\u00D8"] = "\u09A6\u09CD\u09AC", // Ø দ্ব
        ["\u00D9"] = "\u09A6\u09CD\u09AE", // Ù দ্ম
        ["\u00DA"] = "\u09A8\u09CD\u09A0", // Ú ন্ঠ
        ["\u00DB"] = "\u09A8\u09CD\u09A1", // Û ন্ড
        ["\u00DC"] = "\u09A8\u09CD\u09A7", // Ü ন্ধ
        ["\u00DD"] = "\u09A8\u09CD\u09B8", // Ý ন্স
        ["\u00DE"] = "\u09AA\u09CD\u099F", // Þ প্ট
        ["\u00DF"] = "\u09AA\u09CD\u09A4", // ß প্ত
        ["\u00E0"] = "\u09AA\u09CD\u09AA", // à প্প
        ["\u00E1"] = "\u09AA\u09CD\u09B8", // á প্স
        ["\u00E2"] = "\u09AC\u09CD\u099C", // â ব্জ
        ["\u00E3"] = "\u09AC\u09CD\u09A6", // ã ব্দ
        ["\u00E4"] = "\u09AC\u09CD\u09A7", // ä ব্ধ
        ["\u00E5"] = "\u09AD\u09CD\u09B0", // å ভ্র
        ["\u00E6"] = "\u09AE\u09CD\u09A8", // æ ম্ন
        ["\u00E7"] = "\u09AE\u09CD\u09AB", // ç ম্ফ
        ["\u00E8"] = "\u09CD\u09A8", // è ্ন
        ["\u00E9"] = "\u09B2\u09CD\u0995", // é ল্ক
        ["\u00EA"] = "\u09B2\u09CD\u0997", // ê ল্গ
        ["\u00EB"] = "\u09B2\u09CD\u099F", // ë ল্ট
        ["\u00EC"] = "\u09B2\u09CD\u09A1", // ì ল্ড
        ["\u00ED"] = "\u09B2\u09CD\u09AA", // í ল্প
        ["\u00EE"] = "\u09B2\u09CD\u09AB", // î ল্ফ
        ["\u00EF"] = "\u09B6\u09C1", // ï শু
        ["\u00F0"] = "\u09B6\u09CD\u099A", // ð শ্চ
        ["\u00F1"] = "\u09B6\u09CD\u099B", // ñ শ্ছ
        ["\u00F2"] = "\u09B7\u09CD\u09A3", // ò ষ্ণ
        ["\u00F3"] = "\u09B7\u09CD\u099F", // ó ষ্ট
        ["\u00F4"] = "\u09B7\u09CD\u09A0", // ô ষ্ঠ
        ["\u00F5"] = "\u09B7\u09CD\u09AB", // õ ষ্ফ
        ["\u00F6"] = "\u09B8\u09CD\u0996", // ö স্খ
        ["\u00F7"] = "\u09B8\u09CD\u099F", // ÷ স্ট
        ["\u00F8"] = "\u09B8\u09CD\u09A8", // ø স্ন
        ["\u00F9"] = "\u09B8\u09CD\u09AB", // ù স্ফ
        ["\u00FA"] = "\u09CD\u09AA", // ú ্প
        ["\u00FB"] = "\u09B9\u09C1", // û হু
        ["\u00FC"] = "\u09B9\u09C3", // ü হৃ
        ["\u00FD"] = "\u09B9\u09CD\u09A8", // ý হ্ন
        ["\u00FE"] = "\u09B9\u09CD\u09AE", // þ হ্ম
    };

    private static readonly Dictionary<string, string>.AlternateLookup<ReadOnlySpan<char>> s_glyphLookup =
        s_glyphs.GetAlternateLookup<ReadOnlySpan<char>>();

    /// <summary>
    /// Whether a font is a Bijoy font. They are named with an "MJ" suffix -
    /// SutonnyMJ, SutonnyOMJ, and the rest of the family. PDF names carry a
    /// subset prefix and a style suffix ("ABCDEF+SutonnyMJ-Bold"), which are
    /// stripped first.
    /// </summary>
    public static bool IsBijoyFont(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return false;
        }

        var name = fontName.AsSpan();

        var subsetPrefixEnd = name.IndexOf('+');

        if (subsetPrefixEnd >= 0)
        {
            name = name[(subsetPrefixEnd + 1)..];
        }

        var styleSuffixStart = name.IndexOfAny('-', ',');

        if (styleSuffixStart >= 0)
        {
            name = name[..styleSuffixStart];
        }

        return name.Trim().EndsWith("MJ", StringComparison.Ordinal);
    }

    /// <summary>
    /// Concatenates the runs, converting every stretch set in a Bijoy font.
    ///
    /// Adjacent Bijoy runs are merged before conversion: Word splits a word
    /// across runs whenever formatting or editing history changes mid-word, and a
    /// vowel sign in one run must still move past the consonant in the next.
    /// </summary>
    public static string Join(IEnumerable<FontRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var text = new StringBuilder();
        var bijoy = new StringBuilder();

        foreach (var run in runs)
        {
            if (IsBijoyFont(run.FontName))
            {
                bijoy.Append(run.Text);
                continue;
            }

            AppendBijoy();
            text.Append(run.Text);
        }

        AppendBijoy();

        return text.ToString();

        void AppendBijoy()
        {
            if (bijoy.Length == 0)
            {
                return;
            }

            var stretch = bijoy.ToString();

            // An "MJ" font can still hold Unicode - SutonnyOMJ is a Unicode font -
            // and converting real Bangla as if it were Bijoy would destroy it.
            text.Append(stretch.Any(BengaliText.IsBengali) ? stretch : Convert(stretch));
            bijoy.Clear();
        }
    }

    /// <summary>Converts text known to be Bijoy-encoded.</summary>
    public static string Convert(string bijoy)
    {
        ArgumentNullException.ThrowIfNull(bijoy);

        var mapped = MapGlyphs(bijoy);

        // Glyphs that end in a virama meet glyphs that begin with one: "¯" (স্)
        // followed by "’" (্থ) must give স্থ, not স্্থ.
        mapped = mapped.Replace("\u09CD\u09CD", "\u09CD", StringComparison.Ordinal);

        var ordered = BengaliText.MoveVowelSignsAfterConsonants(PlaceRephs(mapped));

        // Canonical composition gives the form Unicode keyboards type: য় as য
        // plus nukta (U+09DF is excluded from composition), ে followed by া as ো.
        return MoveSignsAfterSubjoinedConsonants(ordered).Normalize(NormalizationForm.FormC);
    }

    private static string MapGlyphs(string bijoy)
    {
        var mapped = new StringBuilder(bijoy.Length * 2);
        var index = 0;

        while (index < bijoy.Length)
        {
            if (bijoy[index] == RephCode)
            {
                mapped.Append(RephMarker);
                index++;
                continue;
            }

            // Two-character codes first: "Av" is আ, not অ followed by া.
            if (index + 1 < bijoy.Length && s_glyphLookup.TryGetValue(bijoy.AsSpan(index, 2), out var pair))
            {
                mapped.Append(pair);
                index += 2;
                continue;
            }

            if (s_glyphLookup.TryGetValue(bijoy.AsSpan(index, 1), out var glyph))
            {
                mapped.Append(glyph);
            }
            else
            {
                // Spaces and ordinary punctuation are the same in both encodings.
                mapped.Append(bijoy[index]);
            }

            index++;
        }

        return mapped.ToString();
    }

    /// <summary>
    /// Moves each reph in front of the consonant cluster it was typed after.
    /// Bijoy typists put it on either side of a following vowel sign ("Kg©x" and
    /// "Kgx©" both mean কর্মী), so one vowel sign is stepped over.
    /// </summary>
    private static string PlaceRephs(string text)
    {
        if (!text.Contains(RephMarker))
        {
            return text;
        }

        var placed = new StringBuilder(text.Length + 8);

        foreach (var character in text)
        {
            if (character == RephMarker)
            {
                placed.Insert(StartOfPrecedingCluster(placed), Reph);
            }
            else
            {
                placed.Append(character);
            }
        }

        return placed.ToString();
    }

    /// <summary>
    /// Start of the consonant cluster at the end of <paramref name="text"/>,
    /// allowing one vowel sign after it; the end itself when there is none, so a
    /// reph with nothing to sit on stays where it was typed.
    /// </summary>
    private static int StartOfPrecedingCluster(StringBuilder text)
    {
        var index = text.Length - 1;

        if (index >= 0 && IsVowelSign(text[index]))
        {
            index--;
        }

        if (index >= 0 && text[index] == BengaliText.Nukta)
        {
            index--;
        }

        if (index < 0 || !BengaliText.IsConsonant(text[index]))
        {
            return text.Length;
        }

        while (index >= 2 && text[index - 1] == BengaliText.Virama && BengaliText.IsConsonant(text[index - 2]))
        {
            index -= 2;
        }

        return index;
    }

    /// <summary>
    /// Bijoy lets a vowel sign or chandrabindu be typed before a subjoined form
    /// that attaches to the same consonant - "ক", "া", "্য" for ক্যা - and a
    /// chandrabindu before the vowel sign it sits over. Unicode needs the
    /// consonants first, then the vowel sign, then the chandrabindu.
    /// </summary>
    private static string MoveSignsAfterSubjoinedConsonants(string text)
    {
        var characters = text.ToCharArray();

        for (var index = 0; index + 1 < characters.Length; index++)
        {
            var sign = characters[index];

            if ((IsVowelSign(sign) || sign == '\u0981')
                && index + 2 < characters.Length
                && characters[index + 1] == BengaliText.Virama
                && BengaliText.IsConsonant(characters[index + 2]))
            {
                characters[index] = characters[index + 1];
                characters[index + 1] = characters[index + 2];
                characters[index + 2] = sign;
                index++;
                continue;
            }

            if (sign == '\u0981' && IsVowelSign(characters[index + 1]))
            {
                characters[index] = characters[index + 1];
                characters[index + 1] = sign;
            }
        }

        return new string(characters);
    }

    private static bool IsVowelSign(char character) => character is (>= '\u09BE' and <= '\u09CC') or '\u09D7';
}
