namespace AepSharp.EngineModel;

/// <summary>
/// Named keys for the EngineData document layout. EngineData keys are positional
/// numbers, so the same digit means different things at different depths — these
/// constants pin the meaning at each site. All mappings were validated against
/// ground truth (a purpose-built multi-style template plus production files); see
/// the key-path notes on <see cref="EngineTextExtractor"/>.
/// </summary>
internal static class EngineDataSchema
{
    // --- top level (the blob is a bare dict body) ---

    /// <summary>Top-level resource dict: font set, kinsoku tables, defaults.</summary>
    public const string ResourceDict = "0";

    /// <summary>Top-level text document dict.</summary>
    public const string Document = "1";

    // --- resource dict ---

    /// <summary>In the resource dict: the font set; its entry array sits under <see cref="Entry"/>.</summary>
    public const string FontSet = "1";

    // --- document dict ---

    /// <summary>In the document dict: the paragraph-run array. Each run's text is at Entry/Entry.</summary>
    public const string RunArray = "1";

    /// <summary>
    /// Generic positional wrapper ("/0") used throughout: run -> content, span ->
    /// style holder, font entry -> descriptor, colour -> value holder.
    /// </summary>
    public const string Entry = "0";

    // --- inside a run's content dict ---

    /// <summary>The per-character StyleRun dict; its span array sits under <see cref="Entry"/>.</summary>
    public const string StyleRun = "6";

    // --- inside a StyleRun span ---

    /// <summary>Number of characters of the run text this span covers.</summary>
    public const string SpanLength = "1";

    // --- inside a span's character-style dict (validated against the multi-style template) ---

    /// <summary>Index into the document font set.</summary>
    public const string FontIndex = "0";

    /// <summary>Font size in points.</summary>
    public const string FontSize = "1";

    /// <summary>Fill colour; the tuple lives at Entry/<see cref="ColorValue"/>.</summary>
    public const string FillColor = "53";

    /// <summary>Stroke colour; the tuple lives at Entry/<see cref="ColorValue"/>.</summary>
    public const string StrokeColor = "54";

    /// <summary>Inside a colour dict's Entry: the ARGB numeric array.</summary>
    public const string ColorValue = "1";
}
