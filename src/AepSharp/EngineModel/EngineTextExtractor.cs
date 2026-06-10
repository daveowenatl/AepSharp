namespace AepSharp.EngineModel;

/// <summary>The text content of an EngineData document, run by run.</summary>
internal sealed class EngineTextDocument
{
    public required IReadOnlyList<string> Runs { get; init; }

    /// <summary>All runs joined and trimmed of trailing line breaks/whitespace.</summary>
    public string Text { get; init; } = "";

    /// <summary>PostScript names of every font in the document's font set, in order.</summary>
    public IReadOnlyList<string> Fonts { get; init; } = Array.Empty<string>();
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
        };
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
