namespace AepSharp;

/// <summary>How a property's value changes into or out of a keyframe.</summary>
public enum KeyframeInterpolation
{
    Linear = 1,
    Bezier = 2,
    Hold = 3,
}

/// <summary>
/// Temporal ease on one side of a keyframe. Speed is in the property's stored
/// units per second (fractions per second for percent properties such as
/// Opacity and Scale); influence is a percentage of the segment duration (0–100).
/// </summary>
public readonly record struct AepKeyframeEase(double Speed, double Influence);

/// <summary>A single keyframe of an animated property.</summary>
public sealed class AepKeyframe
{
    /// <summary>Keyframe time as stored: timebase units relative to the layer's start.</summary>
    public int TimeUnits { get; internal set; }

    /// <summary>
    /// Units per second for <see cref="TimeUnits"/> (the containing composition's
    /// internal timebase). Zero when the property was parsed without a composition.
    /// </summary>
    public uint TimeBase { get; internal set; }

    /// <summary>
    /// Keyframe time in seconds of layer time (add the layer's
    /// <see cref="AepLayer.StartTime"/> for composition time). NaN when the
    /// timebase is unknown.
    /// </summary>
    public double Time => TimeBase == 0 ? double.NaN : (double)TimeUnits / TimeBase;

    public KeyframeInterpolation InInterpolation { get; internal set; }
    public KeyframeInterpolation OutInterpolation { get; internal set; }

    /// <summary>Keyframe value as stored (percent properties are fractions). Null for keyframe kinds without a numeric value.</summary>
    public IReadOnlyList<double>? Value { get; internal set; }

    /// <summary>Incoming ease, one entry per dimension (a single entry for 1D and spatial properties).</summary>
    public IReadOnlyList<AepKeyframeEase> InEase { get; internal set; } = Array.Empty<AepKeyframeEase>();

    /// <summary>Outgoing ease, one entry per dimension (a single entry for 1D and spatial properties).</summary>
    public IReadOnlyList<AepKeyframeEase> OutEase { get; internal set; } = Array.Empty<AepKeyframeEase>();

    /// <summary>Incoming spatial tangent, relative to <see cref="Value"/>. Null for non-spatial properties.</summary>
    public IReadOnlyList<double>? InSpatialTangent { get; internal set; }

    /// <summary>Outgoing spatial tangent, relative to <see cref="Value"/>. Null for non-spatial properties.</summary>
    public IReadOnlyList<double>? OutSpatialTangent { get; internal set; }

    public bool TemporalContinuous { get; internal set; }
    public bool TemporalAutoBezier { get; internal set; }
    public bool Roving { get; internal set; }
    public bool SpatialContinuous { get; internal set; }
    public bool SpatialAutoBezier { get; internal set; }

    /// <summary>Timeline label colour index.</summary>
    public int Label { get; internal set; }
}
