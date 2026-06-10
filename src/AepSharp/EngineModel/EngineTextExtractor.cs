namespace AepSharp.EngineModel;

/// <summary>The text content of an EngineData document, run by run.</summary>
internal sealed class EngineTextDocument
{
    public required IReadOnlyList<string> Runs { get; init; }

    /// <summary>All runs joined and trimmed of trailing line breaks/whitespace.</summary>
    public string Text { get; init; } = "";
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
        };
    }
}
