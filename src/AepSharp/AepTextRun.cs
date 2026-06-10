namespace AepSharp;

/// <summary>
/// One styled span of a text layer's copy, decoded from the EngineData StyleRun.
/// A layer with uniform formatting has a single run; mixed formatting (different
/// fonts, sizes, or colours within one text box) produces several.
/// </summary>
public sealed class AepTextRun
{
    /// <summary>The run's text.</summary>
    public string Text { get; internal set; } = "";

    /// <summary>PostScript font name (resolved from the document font set), or null.</summary>
    public string? FontName { get; internal set; }

    /// <summary>Font size in points, or null if absent.</summary>
    public double? FontSize { get; internal set; }

    /// <summary>Fill colour as RGB components in 0..1, or null if absent.</summary>
    public IReadOnlyList<double>? FillColor { get; internal set; }

    /// <summary>Stroke colour as RGB components in 0..1, or null if absent.</summary>
    public IReadOnlyList<double>? StrokeColor { get; internal set; }
}
