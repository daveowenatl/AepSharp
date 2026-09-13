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

    /// <summary>First paragraph's justification code (0..6), or null if absent.</summary>
    public int? Justification { get; init; }

    /// <summary>True when the layer's text frame is a box (paragraph text).</summary>
    public bool IsBoxText { get; init; }

    /// <summary>Box [width, height], or null for point text / an incomplete outline.</summary>
    public IReadOnlyList<double>? BoxSize { get; init; }

    /// <summary>Box top-left [x, y] in layer coordinates, or null for point text.</summary>
    public IReadOnlyList<double>? BoxPosition { get; init; }
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

    /// <summary>Tracking in thousandths of an em, or null if absent.</summary>
    public double? Tracking { get; init; }

    /// <summary>Font caps: 0 normal, 1 small caps, 2 all caps, 3 all small caps; null if absent.</summary>
    public int? FontCaps { get; init; }

    /// <summary>
    /// Line spacing in points: the explicit leading, or font size × the first
    /// paragraph's auto-leading factor while auto leading is on. Null when absent.
    /// </summary>
    public double? Leading { get; init; }
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
        if (top.Get(EngineDataSchema.Document) is not EngineDict document) return null;
        if (document.Get(EngineDataSchema.RunArray) is not EngineArray runArray) return null;

        var runs = new List<string>();
        foreach (var item in runArray.Items)
        {
            if (item is EngineDict run
                && run.Get(EngineDataSchema.Entry) is EngineDict inner
                && inner.Get(EngineDataSchema.Entry) is EngineString text)
            {
                runs.Add(text.Value);
            }
        }

        var paragraphStyle = FirstParagraphStyle(runArray);
        var outline = BoxOutline(top, out var isBoxText);
        return new EngineTextDocument
        {
            Runs = runs,
            Text = string.Concat(runs).TrimEnd('\r', '\n', ' ', '\t'),
            Fonts = ExtractFonts(top),
            StyledRuns = ExtractStyledRuns(runArray, paragraphStyle),
            Justification = paragraphStyle?.Get(EngineDataSchema.Justification) is EngineNumber j ? (int)j.Value : null,
            IsBoxText = isBoxText,
            BoxSize = outline is null ? null : [Math.Abs(outline[12] - outline[0]), Math.Abs(outline[13] - outline[1])],
            BoxPosition = outline is null ? null : [outline[0], outline[1]],
        };
    }

    /// <summary>
    /// The first document entry's first paragraph style, at
    /// <c>/0/5/0[0]/0/0/5</c> of the run entry (path per py-aep's text parser).
    /// </summary>
    private static EngineDict? FirstParagraphStyle(EngineArray runArray)
    {
        if (runArray.Items.Count == 0
            || runArray.Items[0] is not EngineDict run
            || run.Get(EngineDataSchema.Entry) is not EngineDict inner
            || inner.Get(EngineDataSchema.ParagraphRun) is not EngineDict paragraphRun
            || paragraphRun.Get(EngineDataSchema.Entry) is not EngineArray { Items.Count: > 0 } spans
            || spans.Items[0] is not EngineDict span
            || span.Get(EngineDataSchema.Entry) is not EngineDict holder
            || holder.Get(EngineDataSchema.Entry) is not EngineDict holder2)
            return null;
        return holder2.Get(EngineDataSchema.ParagraphRun) as EngineDict;
    }

    /// <summary>
    /// The layer's text frame lives in the resource dict at <c>/0/8/0[0]/0</c>. A box
    /// (paragraph) text frame carries a <c>/1</c> dict whose <c>/0</c> is the box outline
    /// as a flat [x, y, ...] array of 16 vertices tracing the corners with repeats (vertex
    /// 0 is the top-left, vertex 6 the bottom-right); point text has none. Size is
    /// |v6 − v0| and position is v0, as py-aep computes them (needs at least 7 vertices).
    /// </summary>
    private static double[]? BoxOutline(EngineDict top, out bool isBoxText)
    {
        isBoxText = false;
        if (top.Get(EngineDataSchema.ResourceDict) is not EngineDict resource
            || resource.Get(EngineDataSchema.FrameSet) is not EngineDict frameSet
            || frameSet.Get(EngineDataSchema.Entry) is not EngineArray { Items.Count: > 0 } frames
            || frames.Items[0] is not EngineDict frameEntry
            || frameEntry.Get(EngineDataSchema.Entry) is not EngineDict frame
            || frame.Get(EngineDataSchema.FrameBox) is not EngineDict box)
            return null;

        isBoxText = true;
        if (box.Get(EngineDataSchema.BoxOutline) is not EngineArray coords
            || coords.Items.Count < 14
            || !coords.Items.All(c => c is EngineNumber))
            return null;
        return coords.Items.Cast<EngineNumber>().Select(n => n.Value).ToArray();
    }

    /// <summary>
    /// The per-character StyleRun lives inside each run entry at /0/6/0 (an array of
    /// spans). Each span's /1 is its length in the entry's text; its style dict is at
    /// /0/0/6 (font index /0, size /1, fill /53/0/1, stroke /54/0/1 as ARGB).
    /// </summary>
    private static List<EngineStyledRun> ExtractStyledRuns(EngineArray runArray, EngineDict? paragraphStyle)
    {
        var result = new List<EngineStyledRun>();
        foreach (var item in runArray.Items)
        {
            if (item is not EngineDict run || run.Get(EngineDataSchema.Entry) is not EngineDict inner)
                continue;
            var text = (inner.Get(EngineDataSchema.Entry) as EngineString)?.Value ?? "";
            if (inner.Get(EngineDataSchema.StyleRun) is not EngineDict styleRun || styleRun.Get(EngineDataSchema.Entry) is not EngineArray spans)
                continue;

            var pos = 0;
            foreach (var spanValue in spans.Items)
            {
                if (spanValue is not EngineDict span)
                    continue;
                // A corrupt blob can carry a negative or out-of-int-range span length;
                // clamp to [0, remaining] so the cursor never rewinds (which would
                // duplicate text into later runs) and never overflows.
                var rawLen = (span.Get(EngineDataSchema.SpanLength) as EngineNumber)?.Value ?? 0;
                var len = double.IsNaN(rawLen)
                    ? 0
                    : (int)Math.Clamp(rawLen, 0, text.Length - pos);
                var slice = Slice(text, pos, len);
                pos += len;

                var style = (span.Get(EngineDataSchema.Entry) as EngineDict)?.Get(EngineDataSchema.Entry) is EngineDict inner2
                    ? inner2.Get(EngineDataSchema.StyleRun) as EngineDict
                    : null;

                var fontSize = (style?.Get(EngineDataSchema.FontSize) as EngineNumber)?.Value;
                result.Add(new EngineStyledRun
                {
                    Text = slice.TrimEnd('\r', '\n'),
                    FontIndex = style?.Get(EngineDataSchema.FontIndex) is EngineNumber fi ? (int)fi.Value : null,
                    FontSize = fontSize,
                    Tracking = (style?.Get(EngineDataSchema.Tracking) as EngineNumber)?.Value,
                    FontCaps = style?.Get(EngineDataSchema.FontCaps) is EngineNumber caps ? (int)caps.Value : null,
                    Leading = style is null ? null : Leading(style, fontSize, paragraphStyle),
                    Fill = style is null ? null : Color(style, EngineDataSchema.FillColor),
                    Stroke = style is null ? null : Color(style, EngineDataSchema.StrokeColor),
                });
            }
        }
        return result;
    }

    // With auto leading on (the default), After Effects stores a sentinel in the explicit
    // leading key and displays font size × the paragraph's auto-leading factor (1.2 by
    // default). Per py-aep's TextDocument.leading.
    private static double? Leading(EngineDict style, double? fontSize, EngineDict? paragraphStyle)
    {
        if (style.Get(EngineDataSchema.Leading) is not EngineNumber explicitLeading)
            return null;
        var auto = style.Get(EngineDataSchema.AutoLeading) is not EngineBoolean { Value: false };
        if (auto && paragraphStyle is not null && fontSize is not null)
        {
            var factor = (paragraphStyle.Get(EngineDataSchema.AutoLeadingFactor) as EngineNumber)?.Value ?? 1.2;
            return fontSize * factor;
        }
        return explicitLeading.Value;
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
            || colour.Get(EngineDataSchema.Entry) is not EngineDict colour0
            || colour0.Get(EngineDataSchema.ColorValue) is not EngineArray arr)
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
        if (top.Get(EngineDataSchema.ResourceDict) is not EngineDict resource
            || resource.Get(EngineDataSchema.FontSet) is not EngineDict fontSet
            || fontSet.Get(EngineDataSchema.Entry) is not EngineArray entries)
            return Array.Empty<string>();

        var fonts = new List<string>();
        foreach (var entry in entries.Items)
        {
            if (entry is EngineDict e
                && e.Get(EngineDataSchema.Entry) is EngineDict a
                && a.Get(EngineDataSchema.Entry) is EngineDict b
                && b.Get(EngineDataSchema.Entry) is EngineString name)
            {
                fonts.Add(name.Value);
            }
        }
        return fonts;
    }
}
