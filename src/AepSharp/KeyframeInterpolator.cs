namespace AepSharp;

/// <summary>
/// Evaluates keyframed values the way After Effects does, for the pre-expression
/// value. Ported from py-aep's interpolation resolver (MIT,
/// github.com/forticheprod/py-aep), which in turn ports lottie-web (MIT,
/// github.com/airbnb/lottie-web): temporal ease becomes a unit-square cubic Bezier
/// (BezierEaser.js), spatial segments are sampled into a 150-segment polyline and
/// walked by arc length (bez.js), and auto-Bezier tangents/speeds follow the rules
/// py-aep measured against After Effects.
/// </summary>
internal static class KeyframeInterpolator
{
    private const double DefaultInfluence = 100.0 / 6.0;
    private const int CurveSegments = 150;
    private const double AutoBezierChordScale = 1.0 / 6.0;

    internal static IReadOnlyList<double>? Interpolate(double time, IReadOnlyList<AepKeyframe> keyframes, bool isSpatial)
    {
        var n = keyframes.Count;
        if (n == 0)
            return null;
        if (n == 1 || time <= keyframes[0].Time)
            return keyframes[0].Value;
        if (time >= keyframes[n - 1].Time)
            return keyframes[n - 1].Value;

        var right = 1;
        while (right < n && keyframes[right].Time < time)
            right++;
        var left = right - 1;
        var a = keyframes[left];
        var b = keyframes[right];
        double t0 = a.Time, t1 = b.Time;

        if (Math.Abs(time - t0) < 1e-12)
            return a.Value;
        if (Math.Abs(time - t1) < 1e-12)
            return b.Value;
        if (a.Value is not { } v0 || b.Value is not { } v1 || v0.Count != v1.Count)
            return a.Value;

        if (a.OutInterpolation == KeyframeInterpolation.Hold)
            return v0;
        if (b.InInterpolation == KeyframeInterpolation.Hold)
            return v1;

        if (a.OutInterpolation == KeyframeInterpolation.Linear)
            return Lerp(v0, v1, (time - t0) / (t1 - t0));

        // Bezier
        if (isSpatial)
        {
            IReadOnlyList<double>? outTangent = null, inTangent = null;
            if (keyframes.Any(k => k.SpatialAutoBezier))
            {
                outTangent = SpatialTangents(keyframes, left).Out;
                inTangent = SpatialTangents(keyframes, right).In;
            }
            return SpatialBezier(time, t0, t1, a, b, v0, v1, outTangent, inTangent);
        }

        (double Speed, double Influence)? outOverride = null, inOverride = null;
        if (keyframes.Any(k => k.TemporalAutoBezier))
        {
            outOverride = AutoTemporalEase(keyframes, left).Out;
            inOverride = AutoTemporalEase(keyframes, right).In;
        }

        var result = new double[v0.Count];
        for (var d = 0; d < v0.Count; d++)
        {
            var outEase = EaseAt(a.OutEase, d);
            var inEase = EaseAt(b.InEase, d);
            if (outEase is null || inEase is null)
            {
                result[d] = v0[d] + (v1[d] - v0[d]) * ((time - t0) / (t1 - t0));
                continue;
            }
            result[d] = Bezier1D(time, t0, t1, v0[d], v1[d],
                outOverride ?? (outEase.Value.Speed, outEase.Value.Influence),
                inOverride ?? (inEase.Value.Speed, inEase.Value.Influence));
        }
        return result;
    }

    private static AepKeyframeEase? EaseAt(IReadOnlyList<AepKeyframeEase> ease, int dimension) =>
        ease.Count == 0 ? null : ease[dimension < ease.Count ? dimension : 0];

    private static double[] Lerp(IReadOnlyList<double> v0, IReadOnlyList<double> v1, double ratio)
    {
        var result = new double[v0.Count];
        for (var d = 0; d < v0.Count; d++)
            result[d] = v0[d] + (v1[d] - v0[d]) * ratio;
        return result;
    }

    // Speed/influence -> normalised cubic-Bezier control points, as bodymovin exports them.
    private static double Bezier1D(double time, double t0, double t1, double v0, double v1,
        (double Speed, double Influence) outEase, (double Speed, double Influence) inEase)
    {
        var dt = t1 - t0;
        if (dt == 0)
            return v0;
        var dv = v1 - v0;
        var cx1 = outEase.Influence / 100.0;
        var cx2 = 1.0 - inEase.Influence / 100.0;
        double cy1, cy2;
        if (Math.Abs(dv) > 1e-12)
        {
            cy1 = outEase.Speed * cx1 * dt / dv;
            cy2 = 1.0 - inEase.Speed * (1.0 - cx2) * dt / dv;
        }
        else
        {
            cy1 = cx1;
            cy2 = cx2;
        }
        var progress = new BezierEasing(cx1, cy1, cx2, cy2).Get((time - t0) / dt);
        return v0 + dv * progress;
    }

    private static double[] SpatialBezier(double time, double t0, double t1, AepKeyframe a, AepKeyframe b,
        IReadOnlyList<double> v0, IReadOnlyList<double> v1, IReadOnlyList<double>? outTangentOverride, IReadOnlyList<double>? inTangentOverride)
    {
        var dt = t1 - t0;
        var dims = v0.Count;
        if (dt == 0)
            return v0.ToArray();

        var outTangent = outTangentOverride ?? a.OutSpatialTangent ?? new double[dims];
        var inTangent = inTangentOverride ?? b.InSpatialTangent ?? new double[dims];
        var straight = IsZero(outTangent) && IsZero(inTangent);

        var outEase = a.OutEase.Count > 0 ? a.OutEase[0] : (AepKeyframeEase?)null;
        var inEase = b.InEase.Count > 0 ? b.InEase[0] : (AepKeyframeEase?)null;
        double progress;
        if (outEase is { } oe && inEase is { } ie && (oe.Influence > 0 || ie.Influence > 0))
        {
            double derivative0, derivative1;
            if (straight)
            {
                derivative0 = derivative1 = Distance(v0, v1);
            }
            else
            {
                derivative0 = 3.0 * Length(outTangent);
                derivative1 = 3.0 * Length(inTangent);
            }
            var outFraction = oe.Influence / 100.0;
            var inFraction = ie.Influence / 100.0;
            var ds0 = derivative0 > 1e-12 ? oe.Speed / derivative0 : 0.0;
            var ds1 = derivative1 > 1e-12 ? ie.Speed / derivative1 : 0.0;
            var cy1 = Math.Clamp(ds0 * outFraction * dt, 0.0, 1.0);
            var cy2 = Math.Clamp(1.0 - ds1 * inFraction * dt, 0.0, 1.0);
            progress = new BezierEasing(outFraction, cy1, 1.0 - inFraction, cy2).Get((time - t0) / dt);
        }
        else
        {
            progress = (time - t0) / dt;
        }

        if (straight)
            return Lerp(v0, v1, progress);
        return new BezierPath(v0, v1, outTangent, inTangent).PointAt(progress);
    }

    // AE's auto-Bezier spatial tangent: one sixth of the chord between neighbours, in = -out.
    private static (double[] Out, double[] In) SpatialTangents(IReadOnlyList<AepKeyframe> keyframes, int index)
    {
        var keyframe = keyframes[index];
        var dims = keyframe.Value?.Count ?? 0;
        if (!keyframe.SpatialAutoBezier || keyframe.Value is null)
            return ((keyframe.OutSpatialTangent ?? new double[dims]).ToArray(), (keyframe.InSpatialTangent ?? new double[dims]).ToArray());
        if (keyframes.Count < 2 || keyframes.Any(k => k.Value is null))
            return (new double[dims], new double[dims]);

        var low = keyframes[Math.Max(index - 1, 0)].Value!;
        var high = keyframes[Math.Min(index + 1, keyframes.Count - 1)].Value!;
        var outTangent = new double[dims];
        var inTangent = new double[dims];
        for (var d = 0; d < dims; d++)
        {
            outTangent[d] = (high[d] - low[d]) * AutoBezierChordScale;
            inTangent[d] = -outTangent[d];
        }
        return (outTangent, inTangent);
    }

    // AE's auto-Bezier temporal ease for scalar properties (py-aep's _compute_auto_temporal_ease).
    private static ((double, double) Out, (double, double) In) AutoTemporalEase(IReadOnlyList<AepKeyframe> keyframes, int index)
    {
        var n = keyframes.Count;
        var keyframe = keyframes[index];
        var outSpeed = keyframe.OutEase.Count > 0 ? keyframe.OutEase[0].Speed : 0.0;
        var outInfluence = keyframe.OutEase.Count > 0 ? keyframe.OutEase[0].Influence : 0.0;
        var inSpeed = keyframe.InEase.Count > 0 ? keyframe.InEase[0].Speed : 0.0;
        var inInfluence = keyframe.InEase.Count > 0 ? keyframe.InEase[0].Influence : 0.0;

        if (!keyframe.TemporalAutoBezier || outInfluence > 0 || inInfluence > 0 || keyframe.Value is not { Count: 1 } value)
            return ((outSpeed, outInfluence), (inSpeed, inInfluence));

        static double? Scalar(AepKeyframe k) => k.Value is { Count: 1 } v ? v[0] : null;
        if (index == 0 && n > 1)
        {
            var dt = keyframes[1].Time - keyframe.Time;
            if (Scalar(keyframes[1]) is { } next && dt > 0)
                outSpeed = (next - value[0]) / dt;
        }
        else if (index == n - 1 && n > 1)
        {
            var dt = keyframe.Time - keyframes[index - 1].Time;
            if (Scalar(keyframes[index - 1]) is { } previous && dt > 0)
                inSpeed = (value[0] - previous) / dt;
        }
        else if (n > 2)
        {
            var dt = keyframes[index + 1].Time - keyframes[index - 1].Time;
            if (Scalar(keyframes[index - 1]) is { } previous && Scalar(keyframes[index + 1]) is { } next && dt > 0)
                outSpeed = inSpeed = (next - previous) / dt;
        }
        return ((outSpeed, DefaultInfluence), (inSpeed, DefaultInfluence));
    }

    private static bool IsZero(IReadOnlyList<double> vector) => vector.All(x => Math.Abs(x) < 1e-12);

    private static double Length(IReadOnlyList<double> vector) => Math.Sqrt(vector.Sum(x => x * x));

    private static double Distance(IReadOnlyList<double> a, IReadOnlyList<double> b)
    {
        var sum = 0.0;
        for (var d = 0; d < a.Count; d++)
            sum += (b[d] - a[d]) * (b[d] - a[d]);
        return Math.Sqrt(sum);
    }

    /// <summary>Unit-square cubic-Bezier easing (port of lottie-web BezierEaser.js).</summary>
    private readonly struct BezierEasing
    {
        private const int NewtonIterations = 4;
        private const double NewtonMinSlope = 0.001;
        private const double SubdivisionPrecision = 0.0000001;
        private const int SubdivisionMaxIterations = 10;
        private const int SampleTableSize = 11;
        private const double SampleStep = 1.0 / (SampleTableSize - 1.0);

        private readonly double _x1, _y1, _x2, _y2;
        private readonly double[] _samples;

        public BezierEasing(double x1, double y1, double x2, double y2)
        {
            (_x1, _y1, _x2, _y2) = (x1, y1, x2, y2);
            _samples = new double[SampleTableSize];
            for (var i = 0; i < SampleTableSize; i++)
                _samples[i] = Calc(i * SampleStep, x1, x2);
        }

        public double Get(double x)
        {
            if (_x1 == _y1 && _x2 == _y2)
                return x;
            if (x == 0.0)
                return 0.0;
            if (x == 1.0)
                return 1.0;
            return Calc(TForX(x), _y1, _y2);
        }

        private double TForX(double x)
        {
            var intervalStart = 0.0;
            var current = 1;
            const int last = SampleTableSize - 1;
            while (current != last && _samples[current] <= x)
            {
                intervalStart += SampleStep;
                current++;
            }
            current--;

            var denominator = _samples[current + 1] - _samples[current];
            var dist = denominator == 0.0 ? 0.0 : (x - _samples[current]) / denominator;
            var guess = intervalStart + dist * SampleStep;

            var slope = Slope(guess, _x1, _x2);
            if (slope >= NewtonMinSlope)
            {
                for (var i = 0; i < NewtonIterations; i++)
                {
                    var s = Slope(guess, _x1, _x2);
                    if (s == 0.0)
                        return guess;
                    guess -= (Calc(guess, _x1, _x2) - x) / s;
                }
                return guess;
            }
            if (slope == 0.0)
                return guess;

            double a = intervalStart, b = intervalStart + SampleStep, t = 0.0;
            for (var i = 0; i < SubdivisionMaxIterations; i++)
            {
                t = a + (b - a) / 2.0;
                var currentX = Calc(t, _x1, _x2) - x;
                if (currentX > 0.0)
                    b = t;
                else
                    a = t;
                if (Math.Abs(currentX) <= SubdivisionPrecision)
                    break;
            }
            return t;
        }

        private static double A(double a1, double a2) => 1.0 - 3.0 * a2 + 3.0 * a1;
        private static double B(double a1, double a2) => 3.0 * a2 - 6.0 * a1;
        private static double C(double a1) => 3.0 * a1;
        private static double Calc(double t, double a1, double a2) => ((A(a1, a2) * t + B(a1, a2)) * t + C(a1)) * t;
        private static double Slope(double t, double a1, double a2) => 3.0 * A(a1, a2) * t * t + 2.0 * B(a1, a2) * t + C(a1);
    }

    /// <summary>Spatial Bezier segment sampled into a polyline for arc-length lookup (port of lottie-web bez.js).</summary>
    private sealed class BezierPath
    {
        private readonly List<double[]> _points = new();
        private readonly List<double> _partialLengths = new();
        private readonly double _length;

        public BezierPath(IReadOnlyList<double> v0, IReadOnlyList<double> v1, IReadOnlyList<double> outTangent, IReadOnlyList<double> inTangent)
        {
            var dims = v0.Count;
            var p1 = new double[dims];
            var p2 = new double[dims];
            for (var d = 0; d < dims; d++)
            {
                p1[d] = v0[d] + outTangent[d];
                p2[d] = v1[d] + inTangent[d];
            }

            var segments = IsCollinear(v0, v1, p1, p2) ? 2 : CurveSegments;
            double[]? last = null;
            for (var k = 0; k < segments; k++)
            {
                var t = segments > 1 ? (double)k / (segments - 1) : 0.0;
                var u = 1.0 - t;
                var point = new double[dims];
                for (var d = 0; d < dims; d++)
                    point[d] = u * u * u * v0[d] + 3.0 * u * u * t * p1[d] + 3.0 * u * t * t * p2[d] + t * t * t * v1[d];
                var step = last is null ? 0.0 : Distance(last, point);
                _length += step;
                _points.Add(point);
                _partialLengths.Add(step);
                last = point;
            }
        }

        public double[] PointAt(double progress)
        {
            if (progress <= 0.0)
                return _points[0].ToArray();
            if (progress >= 1.0)
                return _points[^1].ToArray();

            var distance = _length * progress;
            var added = 0.0;
            for (var j = 0; j < _points.Count; j++)
            {
                added += _partialLengths[j];
                if (distance == 0.0 || j == _points.Count - 1)
                    return _points[j].ToArray();
                if (distance >= added && distance < added + _partialLengths[j + 1])
                {
                    var fraction = (distance - added) / _partialLengths[j + 1];
                    var a = _points[j];
                    var b = _points[j + 1];
                    var result = new double[a.Length];
                    for (var d = 0; d < a.Length; d++)
                        result[d] = a[d] + (b[d] - a[d]) * fraction;
                    return result;
                }
            }
            return _points[^1].ToArray();
        }

        private static bool IsCollinear(IReadOnlyList<double> v0, IReadOnlyList<double> v1, double[] p1, double[] p2)
        {
            if (v0.Count == 2 && (v0[0] != v1[0] || v0[1] != v1[1]))
                return OnLine2D(v0[0], v0[1], v1[0], v1[1], p1[0], p1[1]) && OnLine2D(v0[0], v0[1], v1[0], v1[1], p2[0], p2[1]);
            if (v0.Count == 3 && (v0[0] != v1[0] || v0[1] != v1[1] || v0[2] != v1[2]))
                return OnLine3D(v0, v1, p1) && OnLine3D(v0, v1, p2);
            return false;
        }

        private static bool OnLine2D(double x1, double y1, double x2, double y2, double x3, double y3)
        {
            var det = x1 * y2 + y1 * x3 + x2 * y3 - x3 * y2 - y3 * x1 - x2 * y1;
            return det > -0.001 && det < 0.001;
        }

        private static bool OnLine3D(IReadOnlyList<double> a, IReadOnlyList<double> b, IReadOnlyList<double> c)
        {
            if (a[2] == 0.0 && b[2] == 0.0 && c[2] == 0.0)
                return OnLine2D(a[0], a[1], b[0], b[1], c[0], c[1]);
            var dist1 = Distance(a, b);
            var dist2 = Distance(a, c);
            var dist3 = Distance(b, c);
            double diff;
            if (dist1 > dist2)
                diff = dist1 > dist3 ? dist1 - dist2 - dist3 : dist3 - dist2 - dist1;
            else if (dist3 > dist2)
                diff = dist3 - dist2 - dist1;
            else
                diff = dist2 - dist1 - dist3;
            return diff > -0.0001 && diff < 0.0001;
        }
    }
}
