namespace AepSharp.EngineModel;

/// <summary>
/// The one byte-level reader for EngineData parenthesized strings, shared by the
/// structural parser (strict: truncation is an error) and the heuristic extractor
/// (tolerant: best effort over arbitrary bytes). Keeping a single implementation
/// prevents the two paths from diverging on escape/termination handling.
/// </summary>
internal static class ParenString
{
    /// <summary>
    /// Reads the raw bytes of a parenthesized string starting at <paramref name="open"/>
    /// (the '(' index), honoring backslash byte-escapes. <paramref name="next"/> is set
    /// just past the closing ')' (or past the end of data when unterminated).
    /// <paramref name="terminated"/> is false when the data ended before a closing ')',
    /// including when a trailing backslash swallowed the final byte.
    /// </summary>
    public static byte[] ReadRaw(ReadOnlySpan<byte> data, int open, out int next, out bool terminated)
    {
        var raw = new List<byte>();
        terminated = false;
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
            {
                terminated = true;
                j++;
                break;
            }
            raw.Add(c);
            j++;
        }
        next = j;
        return raw.ToArray();
    }
}
