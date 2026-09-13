using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Keyframe decoding and interpolation. Record layouts follow py-aep; expected
/// interpolated values were computed with py-aep 0.16.0's interpolation resolver
/// (itself validated against After Effects) for the same synthetic keyframes, so
/// these tests pin AepSharp to that reference rather than to its own output.
/// </summary>
public class KeyframeTests
{
    private const uint TimeBase = 1000; // keyframe units per second in these tests

    private sealed record Kf(
        double Time,
        double[] Value,
        KeyframeInterpolation In = KeyframeInterpolation.Linear,
        KeyframeInterpolation Out = KeyframeInterpolation.Linear,
        (double Speed, double Influence)[]? InEase = null,
        (double Speed, double Influence)[]? OutEase = null,
        double[]? InTangent = null,
        double[]? OutTangent = null,
        bool TemporalAutoBezier = false,
        bool SpatialAutoBezier = false);

    // ---- builders -------------------------------------------------------------

    private static byte[] Item(Kf kf, int dimensions, bool spatial)
    {
        var size = spatial ? 8 + 8 + 5 * 8 + 3 * dimensions * 8 : 8 + 5 * dimensions * 8;
        var bytes = new byte[size];
        BinaryPrimitives.WriteInt32BigEndian(bytes, (int)Math.Round(kf.Time * TimeBase));
        bytes[4] = (byte)kf.In;
        bytes[5] = (byte)kf.Out;
        bytes[7] = (byte)(kf.TemporalAutoBezier ? 1 << 4 : 0);

        void D(int offset, double value) => BinaryPrimitives.WriteDoubleBigEndian(bytes.AsSpan(offset), value);
        var inEase = kf.InEase ?? Enumerable.Repeat((0.0, 100.0 / 6), spatial ? 1 : dimensions).ToArray();
        var outEase = kf.OutEase ?? Enumerable.Repeat((0.0, 100.0 / 6), spatial ? 1 : dimensions).ToArray();

        if (spatial)
        {
            bytes[8 + 3] = (byte)(kf.SpatialAutoBezier ? 1 << 1 : 0);
            var o = 16;
            D(o + 8, inEase[0].Speed);
            D(o + 16, inEase[0].Influence / 100);
            D(o + 24, outEase[0].Speed);
            D(o + 32, outEase[0].Influence / 100);
            for (var d = 0; d < dimensions; d++)
            {
                D(o + 40 + d * 8, kf.Value[d]);
                D(o + 40 + (dimensions + d) * 8, kf.InTangent?[d] ?? 0);
                D(o + 40 + (2 * dimensions + d) * 8, kf.OutTangent?[d] ?? 0);
            }
            return bytes;
        }

        for (var d = 0; d < dimensions; d++)
        {
            D(8 + d * 8, kf.Value[d]);
            D(8 + (dimensions + d) * 8, inEase[d].Speed);
            D(8 + (2 * dimensions + d) * 8, inEase[d].Influence / 100);
            D(8 + (3 * dimensions + d) * 8, outEase[d].Speed);
            D(8 + (4 * dimensions + d) * 8, outEase[d].Influence / 100);
        }
        return bytes;
    }

    private static RifxList AnimatedTdbs(int dimensions, bool spatial, params Kf[] keyframes)
    {
        var tdb4 = new byte[124];
        BinaryPrimitives.WriteUInt16BigEndian(tdb4, 0xDB99);
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), (ushort)dimensions);
        if (spatial)
            tdb4[5] = 1 << 3;

        var items = keyframes.Select(k => Item(k, dimensions, spatial)).ToList();
        var lhd3 = new byte[52];
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(10), (ushort)keyframes.Length);
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(18), (ushort)items[0].Length);
        lhd3[23] = 4;

        var list = new RifxList { Identifier = "list" };
        list.Blocks.Add(new RifxBlock { Type = "lhd3", Size = (uint)lhd3.Length, Data = lhd3 });
        list.Blocks.Add(new RifxBlock { Type = "ldat", Size = (uint)(items.Count * items[0].Length), Data = items.SelectMany(i => i).ToArray() });

        var tdbs = new RifxList { Identifier = "tdbs" };
        tdbs.Blocks.Add(new RifxBlock { Type = "tdb4", Size = 124, Data = tdb4 });
        tdbs.Blocks.Add(new RifxBlock { Type = "LIST", Data = list });
        return tdbs;
    }

    // Wraps the property in a layer Transform group so keyframe times are resolved
    // through the same path compositions use.
    private static AepProperty Animated(string matchName, int dimensions, bool spatial, params Kf[] keyframes) =>
        AnimatedLayer(0, matchName, dimensions, spatial, keyframes).FindTransformProperty(matchName)!;

    private static AepLayer AnimatedLayer(double startSeconds, string matchName, int dimensions, bool spatial, params Kf[] keyframes)
    {
        var ldta = new byte[132];
        BinaryPrimitives.WriteInt32BigEndian(ldta.AsSpan(12), (int)Math.Round(startSeconds * TimeBase));
        BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(16), TimeBase);
        BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(24), TimeBase);

        var group = new RifxList { Identifier = "tdgp" };
        group.Blocks.Add(Tdmn(matchName));
        group.Blocks.Add(new RifxBlock { Type = "LIST", Data = AnimatedTdbs(dimensions, spatial, keyframes) });

        var root = new RifxList { Identifier = "tdgp" };
        root.Blocks.Add(Tdmn("ADBE Transform Group"));
        root.Blocks.Add(new RifxBlock { Type = "LIST", Data = group });

        var layer = new RifxList { Identifier = "Layr" };
        layer.Blocks.Add(new RifxBlock { Type = "ldta", Size = 132, Data = ldta });
        layer.Blocks.Add(new RifxBlock { Type = "LIST", Data = root });
        return AepLayer.Parse(layer, null!, TimeBase);
    }

    private static RifxBlock Tdmn(string matchName)
    {
        var bytes = new byte[40];
        Encoding.ASCII.GetBytes(matchName).CopyTo(bytes, 0);
        return new RifxBlock { Type = "tdmn", Size = 40, Data = bytes };
    }

    private static void AssertValue(IEnumerable<double> expected, IReadOnlyList<double>? actual, int precision = 9)
    {
        Assert.NotNull(actual);
        foreach (var (e, a) in expected.Zip(actual))
            Assert.Equal(e, a, precision);
    }

    // ---- decoding -------------------------------------------------------------

    [Fact]
    public void DecodesOneDimensionalKeyframes()
    {
        var opacity = Animated("ADBE Opacity", 1, false,
            new Kf(0.5, [0.0], Out: KeyframeInterpolation.Bezier, OutEase: [(0, 70)]),
            new Kf(1.5, [1.0], In: KeyframeInterpolation.Bezier, InEase: [(2.5, 33.3)]));

        Assert.True(opacity.IsAnimated);
        Assert.False(opacity.IsSpatial);
        Assert.Equal(1, opacity.Dimensions);
        Assert.Equal(2, opacity.Keyframes.Count);
        Assert.Equal(0.5, opacity.Keyframes[0].Time, 9);
        Assert.Equal(500, opacity.Keyframes[0].TimeUnits);
        Assert.Equal(KeyframeInterpolation.Bezier, opacity.Keyframes[0].OutInterpolation);
        Assert.Equal(70.0, opacity.Keyframes[0].OutEase[0].Influence, 9); // stored as 0.7, exposed as percent
        Assert.Equal(new[] { 1.0 }, opacity.Keyframes[1].Value);
        Assert.Equal(2.5, opacity.Keyframes[1].InEase[0].Speed, 9);
    }

    [Fact]
    public void DecodesSpatialKeyframesWithTangentsAndFlags()
    {
        var position = Animated("ADBE Position", 3, true,
            new Kf(0, [960, 518, 0], OutTangent: [174.39, 0, 0], SpatialAutoBezier: true),
            new Kf(0.4338, [655.5, 612.6, 0], InTangent: [-276.42, 0, 0]));

        Assert.True(position.IsSpatial);
        var first = position.Keyframes[0];
        Assert.Equal(new[] { 960.0, 518, 0 }, first.Value);
        Assert.Equal(new[] { 174.39, 0, 0 }, first.OutSpatialTangent);
        Assert.True(first.SpatialAutoBezier);
        Assert.Single(first.OutEase);
        Assert.Equal(new[] { -276.42, 0, 0 }, position.Keyframes[1].InSpatialTangent);
    }

    [Fact]
    public void KeyframeTimeIsNaNWithoutATimeBase()
    {
        var property = AepProperty.ParseFromList(AnimatedTdbs(1, false, new Kf(1, [0.0]), new Kf(2, [1.0])), "ADBE Opacity");

        Assert.True(double.IsNaN(property.Keyframes[0].Time));
    }

    [Fact]
    public void TruncatedKeyframeDataYieldsCompleteItemsOnly()
    {
        var tdbs = AnimatedTdbs(1, false, new Kf(0, [0.0]), new Kf(1, [1.0]));
        var list = tdbs.SublistFind("list")!;
        var ldat = list.FindByType("ldat")!;
        ldat.Data = ((byte[])ldat.Data)[..60]; // one full 48-byte item plus a fragment

        var property = AepProperty.ParseFromList(tdbs, "ADBE Opacity");

        Assert.Single(property.Keyframes);
    }

    [Fact]
    public void DecodesExpressionAndEnabledFlag()
    {
        var tdbs = AnimatedTdbs(1, false, new Kf(0, [0.0]));
        var source = "wiggle(2, 10)";
        var utf8 = new byte[8 + source.Length];
        Encoding.ASCII.GetBytes("Utf8").CopyTo(utf8, 0);
        BinaryPrimitives.WriteUInt32BigEndian(utf8.AsSpan(4), (uint)source.Length);
        Encoding.UTF8.GetBytes(source).CopyTo(utf8, 8);
        tdbs.Blocks.Add(new RifxBlock { Type = "Utf8", Size = (uint)utf8.Length, Data = utf8 });

        var enabled = AepProperty.ParseFromList(tdbs, "ADBE Opacity");
        Assert.Equal(source, enabled.Expression);
        Assert.True(enabled.ExpressionEnabled);

        ((byte[])tdbs.FindByType("tdb4")!.Data)[119] = 1;
        Assert.False(AepProperty.ParseFromList(tdbs, "ADBE Opacity").ExpressionEnabled);
    }

    // ---- interpolation (expected values from py-aep 0.16.0) -------------------

    [Fact]
    public void HoldKeepsTheLeftValue()
    {
        var p = Animated("ADBE Opacity", 1, false,
            new Kf(0, [5.0], Out: KeyframeInterpolation.Hold),
            new Kf(1, [9.0], In: KeyframeInterpolation.Hold),
            new Kf(2, [1.0]));

        AssertValue([5.0], p.ValueAtTime(0.5));
        AssertValue([5.0], p.ValueAtTime(1.5));
    }

    [Fact]
    public void LinearInterpolatesAndClampsOutsideTheKeyframes()
    {
        var p = Animated("ADBE Position", 3, true,
            new Kf(1, [0, 100, 0], InEase: [(0, 0)], OutEase: [(0, 0)]),
            new Kf(3, [200, 300, 0], InEase: [(0, 0)], OutEase: [(0, 0)]));

        AssertValue([0, 100, 0], p.ValueAtTime(0.5));
        AssertValue([50, 150, 0], p.ValueAtTime(1.5));
        AssertValue([200, 300, 0], p.ValueAtTime(3.5));
    }

    [Fact]
    public void BezierEasyEase()
    {
        var p = Animated("ADBE Opacity", 1, false,
            new Kf(0.5, [0.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 70)], [(0, 70)]),
            new Kf(1.5, [1.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 70)], [(0, 70)]));

        AssertValue([0.061866754488293686], p.ValueAtTime(0.75));
        AssertValue([0.5], p.ValueAtTime(1.0));
        AssertValue([0.9638788574963293], p.ValueAtTime(1.3));
    }

    [Fact]
    public void BezierWithSpeeds()
    {
        var p = Animated("ADBE Rotate Z", 1, false,
            new Kf(0, [10.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 33.333)], [(40, 20)]),
            new Kf(2, [50.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(5, 60)], [(0, 33.333)]));

        AssertValue([25.17815029040404], p.ValueAtTime(0.4));
        AssertValue([39.89540562076522], p.ValueAtTime(1.0));
        AssertValue([48.13743081972129], p.ValueAtTime(1.7));
    }

    [Fact]
    public void BezierEasesEachDimensionOfANonSpatialProperty()
    {
        var p = Animated("ADBE Scale", 3, false,
            new Kf(0, [1, 2, 1], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 20), (0, 20), (0, 20)], [(0, 80), (1, 50), (0, 20)]),
            new Kf(1, [2, 1, 1], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 50), (0, 10), (0, 20)], [(0, 20), (0, 20), (0, 20)]));

        AssertValue([1.0421269143804237, 2.097949755217628, 1.0], p.ValueAtTime(0.25));
        AssertValue([1.4650775728062704, 1.7725488818879704, 1.0], p.ValueAtTime(0.6));
    }

    [Fact]
    public void SpatialBezierOnAStraightPath()
    {
        var p = Animated("ADBE Position", 3, true,
            new Kf(1.5, [-2070, 875, 0], KeyframeInterpolation.Linear, KeyframeInterpolation.Bezier, [(0, 50)], [(0, 33.5)]),
            new Kf(2.1, [-2070, 540, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(6.75, 64)], [(0, 33.5)]));

        AssertValue([-2070, 642.4652900207752, 0], p.ValueAtTime(1.8));
        AssertValue([-2070, 549.1146304575277, 0], p.ValueAtTime(2.0));
    }

    [Fact]
    public void SpatialBezierAlongACurvedPath()
    {
        var p = Animated("ADBE Position", 3, true,
            new Kf(0, [0, 0, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 16.667)], [(300, 40)], [0, 0, 0], [200, -300, 0]),
            new Kf(1, [600, 0, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(100, 30)], [(0, 16.667)], [-150, -200, 0], [0, 0, 0]));

        AssertValue([104.96719324196586, -119.27316691248201, 0], p.ValueAtTime(0.3), 6);
        AssertValue([277.7135600061323, -189.14954252348252, 0], p.ValueAtTime(0.5), 6);
        AssertValue([537.7970706032434, -71.25593026258846, 0], p.ValueAtTime(0.8), 6);
    }

    [Fact]
    public void LinearSpatialSegmentsIgnoreTangents()
    {
        // Matches py-aep. After Effects may follow a bent path here; production
        // templates only carry tangents collinear with their motion.
        var p = Animated("ADBE Position", 3, true,
            new Kf(0, [0, 0, 0], OutTangent: [100, 100, 0]),
            new Kf(1, [400, 0, 0], InTangent: [-100, 100, 0]));

        AssertValue([200, 0, 0], p.ValueAtTime(0.5));
    }

    [Fact]
    public void TemporalAutoBezier()
    {
        var p = Animated("ADBE Opacity", 1, false,
            new Kf(0, [0.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 0)], [(0, 0)], TemporalAutoBezier: true),
            new Kf(1, [10.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 0)], [(0, 0)], TemporalAutoBezier: true),
            new Kf(3, [0.0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 0)], [(0, 0)], TemporalAutoBezier: true));

        AssertValue([5.625], p.ValueAtTime(0.5));
        AssertValue([5.625], p.ValueAtTime(2.0));
    }

    [Fact]
    public void SpatialAutoBezier()
    {
        var p = Animated("ADBE Position", 3, true,
            new Kf(0, [0, 0, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 16.667)], [(0, 16.667)], SpatialAutoBezier: true),
            new Kf(1, [300, 300, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 16.667)], [(0, 16.667)], SpatialAutoBezier: true),
            new Kf(2, [600, 0, 0], KeyframeInterpolation.Bezier, KeyframeInterpolation.Bezier, [(0, 16.667)], [(0, 16.667)], SpatialAutoBezier: true));

        AssertValue([132.91304168975452, 170.77715794094328, 0], p.ValueAtTime(0.5), 6);
        AssertValue([467.0869583102455, 170.77715794094323, 0], p.ValueAtTime(1.5), 6);
    }

    [Fact]
    public void TransformValueAtUsesCompositionTime()
    {
        // Keyframe times are layer-relative; a layer starting at 2 s reaches its
        // first keyframe at composition time 2 s.
        var layer = AnimatedLayer(2, "ADBE Opacity", 1, false, new Kf(0, [0.0]), new Kf(1, [1.0]));

        AssertValue([0.25], layer.FindTransformProperty("ADBE Opacity")!.ValueAtTime(0.25));
        AssertValue([0.25], layer.TransformValueAt("ADBE Opacity", 2.25));
        AssertValue([0.0], layer.TransformValueAt("ADBE Opacity", 1.0));
    }
}
