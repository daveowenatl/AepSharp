namespace AepSharp;

/// <summary>
/// Keyframe evaluation for callers that build their own keyframes (a retiming step that
/// rewrites a property's animation, a test that needs After Effects' ease curve). The same
/// interpolator <see cref="AepProperty.ValueAtTime"/> uses for keyframes read from a file.
/// </summary>
public static class Keyframes
{
    /// <summary>
    /// The value at <paramref name="time"/> seconds interpolated from <paramref name="keyframes"/>
    /// (in time order): hold, linear, and Bezier with temporal ease; spatial paths when
    /// <paramref name="spatial"/>. Before the first key or after the last, that key's value.
    /// Null for an empty list.
    /// </summary>
    public static IReadOnlyList<double>? Evaluate(double time, IReadOnlyList<AepKeyframe> keyframes, bool spatial = false) =>
        KeyframeInterpolator.Interpolate(time, keyframes, spatial);
}
