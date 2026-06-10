using System.Linq;

namespace AepSharp.EngineModel;

/// <summary>The text content of an EngineData document, run by run.</summary>
internal sealed class EngineTextDocument
{
    public required IReadOnlyList<string> Runs { get; init; }

    /// <summary>All runs joined and trimmed of trailing line breaks/whitespace.</summary>
    public string Text { get; init; } = "";

    /// <summary>PostScript names of every font in the document's font set, in order.</summary>
    public IReadOnlyList<string> Fonts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Per-character style spans (the EngineData StyleRun), each covering a slice of
    /// the text with its own font/size/colour. One span for uniform text; multiple
    /// when the copy mixes styles.
    /// </summary>
    public IReadOnlyList<EngineStyledRun> StyledRuns { get; init; } = Array.Empty<EngineStyledRun>();
}

/// <summary>One styled span of text within a document (a StyleRun entry).</summary>
internal sealed class EngineStyledRun
{
    public string Text { get; init; } = "";

    /// <summary>Index into the document font set, or null if absent.</summary>
    public int? FontIndex { get; init; }

    public double? FontSize { get; init; }

    /// <summary>Fill colour as RGB components in 0..1, or null if absent.</summary>
    public IReadOnlyList<double>? Fill { get; init; }

    /// <summary>Stroke colour as RGB components in 0..1, or null if absent.</summary>
    public IReadOnlyList<double>? Stroke { get; init; }
}

/// <summary>
/// Pulls the text out of a parsed EngineData document by structure (not heuristic):
/// the document is the top-level <c>/1</c> dict; its <c>/1</c> is the run array; each
/// run's <c>/0/0</c> is the run's string. Concatenating the runs gives the full copy
/// and fixes the heuristic's multi-run truncation and CJK drop.
/// </summary>
internal static class EngineTextExtractor
{
    public static EngineTextDocument? Extract(EngineValue root)
    {
        if (root is not EngineDict top) return null;
        if (top.Get("1") is not EngineDict document) return null;
        if (document.Get("1") is not EngineArray runArray) return null;

        var runs = new List<string>();
        foreach (var item in runArray.Items)
        {
            if (item is EngineDict run
                && run.Get("0") is EngineDict inner
                && inner.Get("0") is EngineString text)
            {
                runs.Add(text.Value);
            }
        }

        return new EngineTextDocument
        {
            Runs = runs,
            Text = string.Concat(runs).TrimEnd('\r', '\n', ' ', '\t'),
            Fonts = ExtractFonts(top),
            StyledRuns = ExtractStyledRuns(runArray),
        };
    }

    /// <summary>
    /// The per-character StyleRun lives inside each run entry at /0/6/0 (an array of
    /// spans). Each span's /1 is its length in the entry's text; its style dict is at
    /// /0/0/6 (font index /0, size /1, fill /53/0/1, stroke /54/0/1 as ARGB).
    /// </summary>
    private static List<EngineStyledRun> ExtractStyledRuns(EngineArray runArray)
    {
        var result = new List<EngineStyledRun>();
        foreach (var item in runArray.Items)
        {
            if (item is not EngineDict run || run.Get("0") is not EngineDict inner)
                continue;
            var text = (inner.Get("0") as EngineString)?.Value ?? "";
            if (inner.Get("6") is not EngineDict styleRun || styleRun.Get("0") is not EngineArray spans)
                continue;

            var pos = 0;
            foreach (var spanValue in spans.Items)
            {
                if (spanValue is not EngineDict span)
                    continue;
                // A corrupt blob can carry a negative or out-of-int-range span length;
                // clamp to [0, remaining] so the cursor never rewinds (which would
                // duplicate text into later runs) and never overflows.
                var rawLen = (span.Get("1") as EngineNumber)?.Value ?? 0;
                var len = double.IsNaN(rawLen)
                    ? 0
                    : (int)Math.Clamp(rawLen, 0, text.Length - pos);
                var slice = Slice(text, pos, len);
                pos += len;

                var style = (span.Get("0") as EngineDict)?.Get("0") is EngineDict inner2
                    ? inner2.Get("6") as EngineDict
                    : null;

                result.Add(new EngineStyledRun
                {
                    Text = slice.TrimEnd('\r', '\n'),
                    FontIndex = style?.Get("0") is EngineNumber fi ? (int)fi.Value : null,
                    FontSize = (style?.Get("1") as EngineNumber)?.Value,
                    Fill = style is null ? null : Color(style, "53"),
                    Stroke = style is null ? null : Color(style, "54"),
                });
            }
        }
        return result;
    }

    private static string Slice(string text, int start, int length)
    {
        if (start >= text.Length || length <= 0)
            return "";
        return text.Substring(start, Math.Min(length, text.Length - start));
    }

    /// <summary>
    /// A colour is nested at &lt;styleKey&gt;/0/1 as a numeric array. Observed as an
    /// ARGB 4-tuple (leading component 1); return the trailing RGB components.
    /// </summary>
    private static IReadOnlyList<double>? Color(EngineDict style, string key)
    {
        if (style.Get(key) is not EngineDict colour
            || colour.Get("0") is not EngineDict colour0
            || colour0.Get("1") is not EngineArray arr)
            return null;

        var nums = arr.Items.OfType<EngineNumber>().Select(n => n.Value).ToList();
        if (nums.Count == 4)
            nums = nums.Skip(1).ToList();
        return nums.Count > 0 ? nums : null;
    }

    /// <summary>
    /// Font set lives in the resource dict: /0 -> /1 -> /0 is an array of font entries,
    /// each entry's /0/0/0 being the PostScript name string.
    /// </summary>
    private static IReadOnlyList<string> ExtractFonts(EngineDict top)
    {
        if (top.Get("0") is not EngineDict resource
            || resource.Get("1") is not EngineDict fontSet
            || fontSet.Get("0") is not EngineArray entries)
            return Array.Empty<string>();

        var fonts = new List<string>();
        foreach (var entry in entries.Items)
        {
            if (entry is EngineDict e
                && e.Get("0") is EngineDict a
                && a.Get("0") is EngineDict b
                && b.Get("0") is EngineString name)
            {
                fonts.Add(name.Value);
            }
        }
        return fonts;
    }
}
