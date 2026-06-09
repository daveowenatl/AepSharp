using System.Text;
using System.Text.RegularExpressions;

namespace AepSharp;

/// <summary>
/// Decodes the Adobe "EngineData" / CoolType text-document blob that carries a
/// text layer's on-screen copy. In a parsed .aep this blob is the bytes of the
/// anomalous (<see cref="Rifx.RifxBlock.IsAnomalous"/>) block inside a text
/// layer's subtree — the reader captures it as ANON because its leading bytes
/// (" &lt;&lt; ") look like a bogus chunk size.
///
/// The format is a nested PostScript-ish dictionary. Strings are written as
/// <c>( FE FF &lt;UTF-16BE bytes&gt; )</c> with byte-level <c>\(</c> <c>\)</c>
/// <c>\\</c> escapes. A text doc embeds its font table, font version strings,
/// CJK line-break (kinsoku) tables and blend names before the actual run text.
///
/// This is a targeted heuristic extractor, NOT a full EngineData parser: it
/// returns the last non-empty UTF-16BE string that isn't recognizable font /
/// structural noise. Robust for typical layers; a copy that looks exactly like
/// a font/option token could be mispicked. A structural dictionary parse is the
/// eventual upgrade.
/// </summary>
internal static class EngineData
{
    // Font version strings and Adobe-internal markers (substring match).
    private static readonly Regex StructuralNoise =
        new(@"Version|hotconv|ttfautohint|makeotf|CoolType|AdobeInvis", RegexOptions.Compiled);

    // Exact structural option/token names, content-store tokens, and the
    // single-digit kinsoku artifacts ("1 6 0 5 3"). Real numeric copy is always
    // multi-character ("409", "2.99", "13,250"), so a lone digit is never content.
    private static readonly Regex ExactNoise =
        new(@"^(Hard|Soft|Normal RGB|DVA)$|^TkD-|^[0-9]$", RegexOptions.Compiled);

    // PostScript font names, e.g. "Heebo-ExtraBold", "MyriadPro-Regular" —
    // letters/digits in hyphen-joined parts, no spaces. Real ad copy has spaces,
    // punctuation, or is a plain word, so it won't match.
    private static readonly Regex FontNameShape =
        new(@"^[A-Za-z][A-Za-z0-9]*(-[A-Za-z0-9]+)+$", RegexOptions.Compiled);

    // Non-hyphenated font tokens the corpus surfaced: PostScript names ending in
    // MT/PS/PSMT (e.g. "ArialMT", "TimesNewRomanPSMT") and internal names with a
    // numeric id suffix (e.g. "GrotaSansBook_91628"). Single token, no spaces.
    private static readonly Regex FontTokenShape =
        new(@"^[A-Za-z][A-Za-z0-9]*(MT|PS|PSMT)$|^[A-Za-z][A-Za-z0-9]*_[0-9]+$", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the display copy from an EngineData blob. Returns "" when the
    /// layer's text run is empty.
    /// </summary>
    public static string ExtractDisplayText(ReadOnlySpan<byte> data)
    {
        string? lastDisplay = null;

        var i = 0;
        while (i < data.Length)
        {
            if (data[i] != (byte)'(')
            {
                i++;
                continue;
            }

            var raw = ReadParenString(data, i, out var next);
            i = next;

            // Require the UTF-16 BOM; everything we care about is UTF-16BE.
            if (raw.Length < 2 || raw[0] != 0xFE || raw[1] != 0xFF)
                continue;

            var text = Encoding.BigEndianUnicode.GetString(raw, 2, raw.Length - 2);
            if (!IsDisplayCandidate(text))
                continue;

            var cleaned = text.TrimEnd('\r', '\n', ' ', '\t');
            if (cleaned.Length > 0)
                lastDisplay = cleaned;
        }

        return lastDisplay ?? "";
    }

    /// <summary>
    /// Reads the raw bytes of a parenthesized string starting at <paramref name="open"/>
    /// (the '(' index), honoring backslash byte-escapes. <paramref name="next"/> is set
    /// to the index just past the closing ')'.
    /// </summary>
    private static byte[] ReadParenString(ReadOnlySpan<byte> data, int open, out int next)
    {
        var raw = new List<byte>();
        var j = open + 1;
        while (j < data.Length)
        {
            var c = data[j];
            if (c == (byte)'\\')
            {
                // Escape: the next byte is literal (this also re-aligns UTF-16
                // pairs when Adobe escapes a '(' ')' or '\\' inside the stream).
                if (j + 1 < data.Length)
                    raw.Add(data[j + 1]);
                j += 2;
                continue;
            }
            if (c == (byte)')')
                break;
            raw.Add(c);
            j++;
        }
        next = j + 1;
        return raw.ToArray();
    }

    private static bool IsDisplayCandidate(string text)
    {
        if (!HasMeaningfulContent(text)) return false;   // empty runs + stray punctuation tokens
        if (ContainsCjk(text)) return false;   // kinsoku tables
        if (StructuralNoise.IsMatch(text)) return false;   // font version strings, CoolType, etc.
        if (ExactNoise.IsMatch(text)) return false;   // Hard / Soft / Normal RGB / TkD-...
        if (FontNameShape.IsMatch(text)) return false;   // hyphenated PostScript font names
        if (FontTokenShape.IsMatch(text)) return false;   // ArialMT / GrotaSansBook_91628
        if (IsHexColorish(text)) return false;   // 000000 / ffffff / 00FFF6 / 24FF00
        return true;
    }

    /// <summary>
    /// True for 6- or 8-char hex color/code tokens (e.g. "000000", "ffffff",
    /// "00FFF6"). To avoid eating real all-letter words that happen to be hex
    /// (FACADE, DECADE), it only matches when there's a digit or the whole token
    /// is one repeated character.
    /// </summary>
    private static bool IsHexColorish(string text)
    {
        if (text.Length != 6 && text.Length != 8) return false;
        var allHex = true;
        var hasDigit = false;
        var allSame = true;
        foreach (var ch in text)
        {
            var isHex = ch is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
            if (!isHex) { allHex = false; break; }
            if (ch is >= '0' and <= '9') hasDigit = true;
            if (ch != text[0]) allSame = false;
        }
        return allHex && (hasDigit || allSame);
    }

    /// <summary>
    /// True when the string has at least one "real" character — i.e. not made up
    /// solely of whitespace, control chars, soft hyphens, or typographic
    /// punctuation (curly quotes, dashes, ellipsis at U+2000-U+206F). This rejects
    /// the composite-font/kinsoku tokens that leak through while keeping genuine
    /// short copy like "$" and "%".
    /// </summary>
    private static bool HasMeaningfulContent(string text)
    {
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch)) continue;
            if (char.IsControl(ch)) continue;
            if (ch == '­') continue;   // soft hyphen
            if (ch >= ' ' && ch <= '⁯') continue;   // general punctuation
            return true;
        }
        return false;
    }

    private static bool ContainsCjk(string text)
    {
        foreach (var ch in text)
        {
            if ((ch >= '　' && ch <= '鿿') ||   // CJK symbols + ideographs
                (ch >= '＀' && ch <= '￯') ||   // halfwidth/fullwidth forms
                (ch >= '぀' && ch <= 'ヿ'))     // hiragana + katakana
                return true;
        }
        return false;
    }
}
