using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Layer timing (ldta start/in/out) and Transform group decoding. Timing layout
/// pinned against a production template whose rendered output confirms the
/// precomp start frames: start, in and out are (s32 dividend, u32 divisor) pairs
/// at ldta offsets 12, 20 and 28; in/out are in layer time.
/// </summary>
public class LayerTransformTests
{
    private static RifxList Layer(
        (int dividend, uint divisor) start,
        (int dividend, uint divisor) inPoint,
        (int dividend, uint divisor) outPoint,
        params RifxBlock[] extraBlocks)
    {
        var ldta = new byte[132];
        WriteTime(ldta, 12, start);
        WriteTime(ldta, 20, inPoint);
        WriteTime(ldta, 28, outPoint);

        var layer = new RifxList { Identifier = "Layr" };
        layer.Blocks.Add(new RifxBlock { Type = "ldta", Size = (uint)ldta.Length, Data = ldta });
        layer.Blocks.AddRange(extraBlocks);
        return layer;
    }

    private static void WriteTime(byte[] ldta, int offset, (int dividend, uint divisor) time)
    {
        BinaryPrimitives.WriteInt32BigEndian(ldta.AsSpan(offset), time.dividend);
        BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(offset + 4), time.divisor);
    }

    private static RifxBlock Tdmn(string matchName)
    {
        var bytes = new byte[40];
        Encoding.ASCII.GetBytes(matchName).CopyTo(bytes, 0);
        return new RifxBlock { Type = "tdmn", Size = 40, Data = bytes };
    }

    private static RifxBlock List(RifxList list) => new() { Type = "LIST", Data = list };

    private static RifxList Tdbs(int components, double[]? cdatDoubles, int? keyframes)
    {
        var tdb4 = new byte[124];
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), (ushort)components);
        var tdbs = new RifxList { Identifier = "tdbs" };
        tdbs.Blocks.Add(new RifxBlock { Type = "tdb4", Size = 124, Data = tdb4 });

        if (cdatDoubles is not null)
        {
            var cdat = new byte[cdatDoubles.Length * 8];
            for (var i = 0; i < cdatDoubles.Length; i++)
                BinaryPrimitives.WriteDoubleBigEndian(cdat.AsSpan(i * 8), cdatDoubles[i]);
            tdbs.Blocks.Add(new RifxBlock { Type = "cdat", Size = (uint)cdat.Length, Data = cdat });
        }

        if (keyframes is { } count)
        {
            var lhd3 = new byte[52];
            BinaryPrimitives.WriteUInt32BigEndian(lhd3.AsSpan(8), (uint)count);
            var list = new RifxList { Identifier = "list" };
            list.Blocks.Add(new RifxBlock { Type = "lhd3", Size = 20, Data = lhd3 });
            tdbs.Blocks.Add(List(list));
        }

        return tdbs;
    }

    private static RifxBlock TransformGroup(params (string matchName, RifxList tdbs)[] properties)
    {
        var group = new RifxList { Identifier = "tdgp" };
        foreach (var (matchName, tdbs) in properties)
        {
            group.Blocks.Add(Tdmn(matchName));
            group.Blocks.Add(List(tdbs));
        }

        var root = new RifxList { Identifier = "tdgp" };
        root.Blocks.Add(Tdmn("ADBE Transform Group"));
        root.Blocks.Add(List(group));
        return List(root);
    }

    [Fact]
    public void DecodesStartInAndOutTimes()
    {
        // A precomp placed at frame 508 of a 23.976 fps comp, 6.006 s long.
        var layer = AepLayer.Parse(Layer((508000, 23976), (0, 23976), (144000, 23976)), null!);

        Assert.Equal(21.188, layer.StartTime, 3);
        Assert.Equal(0.0, layer.InPoint, 3);
        Assert.Equal(6.006, layer.OutPoint, 3);
        Assert.Equal(21.188, layer.CompositionInPoint, 3);
        Assert.Equal(27.194, layer.CompositionOutPoint, 3);
    }

    [Fact]
    public void ZeroDivisorTimeIsZero()
    {
        var layer = AepLayer.Parse(Layer((5, 0), (0, 0), (10, 0)), null!);

        Assert.Equal(0.0, layer.StartTime);
        Assert.Equal(0.0, layer.OutPoint);
    }

    [Fact]
    public void NegativeStartTimeIsPreserved()
    {
        // Layers can be slid to start before the composition begins.
        var layer = AepLayer.Parse(Layer((-24000, 24000), (0, 24000), (48000, 24000)), null!);

        Assert.Equal(-1.0, layer.StartTime);
        Assert.Equal(1.0, layer.CompositionOutPoint);
    }

    [Fact]
    public void ExposesStaticTransformValues()
    {
        var layer = AepLayer.Parse(Layer((0, 1), (0, 1), (1, 1),
            TransformGroup(
                ("ADBE Anchor Point", Tdbs(3, [10, 20, 0], null)),
                ("ADBE Position", Tdbs(3, [959.093, 817, 0, 0, 0], null)),
                ("ADBE Scale", Tdbs(3, [0.3, 0.3, 1], null)),
                ("ADBE Rotate Z", Tdbs(1, [45, 0], null)),
                ("ADBE Opacity", Tdbs(1, [0.5, 0], null)))), null!);

        Assert.NotNull(layer.Transform);
        Assert.Equal(new[] { 10.0, 20.0, 0.0 }, layer.AnchorPoint);
        Assert.Equal(new[] { 959.093, 817.0, 0.0 }, layer.Position);
        Assert.Equal(new[] { 0.3, 0.3, 1.0 }, layer.Scale);
        Assert.Equal(45.0, layer.Rotation);
        Assert.Equal(0.5, layer.Opacity);
    }

    [Fact]
    public void AnimatedTransformPropertyReportsKeyframesAndNoStaticValue()
    {
        var layer = AepLayer.Parse(Layer((0, 1), (0, 1), (1, 1),
            TransformGroup(("ADBE Opacity", Tdbs(1, null, keyframes: 4)))), null!);

        var opacity = layer.FindTransformProperty("ADBE Opacity");
        Assert.NotNull(opacity);
        Assert.True(opacity.IsAnimated);
        Assert.Equal(4, opacity.KeyframeCount);
        Assert.Null(layer.Opacity);
    }

    [Fact]
    public void UnstoredTransformValuesAreNull()
    {
        // After Effects omits values left at their defaults; callers apply defaults.
        var layer = AepLayer.Parse(Layer((0, 1), (0, 1), (1, 1),
            TransformGroup(("ADBE Position", Tdbs(3, [1, 2, 0], null)))), null!);

        Assert.Null(layer.AnchorPoint);
        Assert.Null(layer.Scale);
        Assert.Null(layer.Rotation);
        Assert.Null(layer.Opacity);
    }

    [Fact]
    public void TruncatedKeyframeHeaderIsNotAnimated()
    {
        var tdbs = Tdbs(1, [1.0], null);
        var list = new RifxList { Identifier = "list" };
        list.Blocks.Add(new RifxBlock { Type = "lhd3", Size = 4, Data = new byte[4] });
        tdbs.Blocks.Add(List(list));

        var prop = AepProperty.ParseFromList(tdbs, "ADBE Opacity");

        Assert.False(prop.IsAnimated);
    }

    [Fact]
    public void KeyframeCountUsesTheFullU32Field()
    {
        // 0x0001_0002 = 65538: a u16 read at offset 10 would see only 2.
        var layer = AepLayer.Parse(Layer((0, 1), (0, 1), (1, 1),
            TransformGroup(("ADBE Position", Tdbs(3, null, keyframes: 65538)))), null!);

        Assert.Equal(65538, layer.FindTransformProperty("ADBE Position")!.KeyframeCount);
    }

    [Fact]
    public void FixtureLayerCarriesTransformGroupAndTiming()
    {
        var project = AepProject.Open("data/Item-01.aep");
        var layer = project.Items.Values
            .Where(i => i.ItemType == ItemType.Composition)
            .SelectMany(c => c.CompositionLayers)
            .First();

        Assert.NotNull(layer.Transform);
        Assert.True(layer.CompositionOutPoint > layer.CompositionInPoint);
    }
}
