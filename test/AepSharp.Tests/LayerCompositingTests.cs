using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// ldta fields past the layer name, per py-aep's LdtaChunk: blending mode (u8 at 99,
/// SDK PF_Xfer), transfer flags (103), track matte type (107), time stretch (s32 at 8 /
/// u32 at 108), parent id (132) and the AE 2023+ matte layer id (160); and how stretch
/// and the source-duration clamp shape composition in/out points.
/// </summary>
public class LayerCompositingTests
{
    private sealed record Ldta(
        uint Id = 7,
        byte Blend = 2,
        byte Transfer = 0,
        byte Matte = 0,
        int StretchDividend = 1,
        uint StretchDivisor = 1,
        uint Parent = 0,
        uint? MatteLayer = null,
        int Length = 164,
        double Start = 0,
        double In = 0,
        double Out = 10);

    private static AepLayer Parse(Ldta d)
    {
        var ldta = new byte[d.Length];
        BinaryPrimitives.WriteUInt32BigEndian(ldta, d.Id);
        BinaryPrimitives.WriteInt32BigEndian(ldta.AsSpan(8), d.StretchDividend);
        WriteTime(ldta, 12, d.Start);
        WriteTime(ldta, 20, d.In);
        WriteTime(ldta, 28, d.Out);
        if (d.Length >= 164)
        {
            ldta[99] = d.Blend;
            ldta[103] = d.Transfer;
            ldta[107] = d.Matte;
            BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(108), d.StretchDivisor);
            BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(132), d.Parent);
            BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(160), d.MatteLayer ?? 0);
        }

        var layer = new RifxList { Identifier = "Layr" };
        layer.Blocks.Add(new RifxBlock { Type = "ldta", Size = (uint)ldta.Length, Data = ldta });
        return AepLayer.Parse(layer, null!);
    }

    private static void WriteTime(byte[] ldta, int offset, double seconds)
    {
        BinaryPrimitives.WriteInt32BigEndian(ldta.AsSpan(offset), (int)Math.Round(seconds * 1000));
        BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(offset + 4), 1000);
    }

    [Theory]
    [InlineData(0, 0, BlendingMode.Normal)]     // cameras, lights, nulls
    [InlineData(2, 0, BlendingMode.Normal)]
    [InlineData(3, 0, BlendingMode.Dissolve)]
    [InlineData(3, 2, BlendingMode.DancingDissolve)]
    [InlineData(4, 0, BlendingMode.Add)]
    [InlineData(5, 0, BlendingMode.Multiply)]
    [InlineData(6, 0, BlendingMode.Screen)]
    [InlineData(12, 0, BlendingMode.ClassicDifference)]
    [InlineData(22, 0, BlendingMode.AlphaAdd)]
    [InlineData(26, 0, BlendingMode.Difference)]
    [InlineData(38, 0, BlendingMode.Divide)]
    [InlineData(200, 0, BlendingMode.Normal)]   // unknown
    public void DecodesBlendingModeFromTheTransferModeValue(byte raw, byte transferFlags, BlendingMode expected)
    {
        Assert.Equal(expected, Parse(new Ldta(Blend: raw, Transfer: transferFlags)).BlendingMode);
    }

    [Fact]
    public void DecodesPreserveTransparency()
    {
        Assert.True(Parse(new Ldta(Transfer: 1)).PreserveTransparency);
        Assert.False(Parse(new Ldta(Transfer: 2)).PreserveTransparency);
    }

    [Theory]
    [InlineData(0, TrackMatteType.None)]
    [InlineData(1, TrackMatteType.Alpha)]
    [InlineData(2, TrackMatteType.AlphaInverted)]
    [InlineData(3, TrackMatteType.Luma)]
    [InlineData(4, TrackMatteType.LumaInverted)]
    [InlineData(9, TrackMatteType.None)]
    public void DecodesTrackMatteType(byte raw, TrackMatteType expected)
    {
        Assert.Equal(expected, Parse(new Ldta(Matte: raw)).TrackMatte);
    }

    [Fact]
    public void DecodesIdParentAndMatteLayer()
    {
        var layer = Parse(new Ldta(Id: 14345, Matte: 1, Parent: 14300, MatteLayer: 14346));

        Assert.Equal(14345u, layer.Id);
        Assert.Equal(14300u, layer.ParentLayerId);
        Assert.Equal(14346u, layer.TrackMatteLayerId);
    }

    [Fact]
    public void ZeroParentAndMatteIdsAreNull()
    {
        var layer = Parse(new Ldta());

        Assert.Null(layer.ParentLayerId);
        Assert.Null(layer.TrackMatteLayerId);
    }

    [Fact]
    public void ShortLdtaKeepsDefaults()
    {
        // Pre-2023 files have a 160-byte ldta (no matte layer id); synthetic ones can be shorter.
        var layer = Parse(new Ldta(Length: 132, StretchDividend: 5));

        Assert.Equal(BlendingMode.Normal, layer.BlendingMode);
        Assert.Equal(TrackMatteType.None, layer.TrackMatte);
        Assert.Equal(1.0, layer.Stretch);
        Assert.Null(layer.ParentLayerId);
        Assert.Null(layer.TrackMatteLayerId);
    }

    [Fact]
    public void StretchScalesInAndOutPoints()
    {
        // 200%: a layer starting at 1 s, trimmed to layer time 0.5–3 s.
        var layer = Parse(new Ldta(StretchDividend: 2, StretchDivisor: 1, Start: 1, In: 0.5, Out: 3));

        Assert.Equal(2.0, layer.Stretch);
        Assert.Equal(2.0, layer.CompositionInPoint, 9);
        Assert.Equal(7.0, layer.CompositionOutPoint, 9);
    }

    [Fact]
    public void TimeReversedLayerReportsTheVisibleRangeInOrder()
    {
        // -80%, as in production flash transitions: start 12.4458 s, layer time 0–1.0344 s.
        var layer = Parse(new Ldta(StretchDividend: -4, StretchDivisor: 5, Start: 12.445, In: 0, Out: 1.035));

        Assert.Equal(-0.8, layer.Stretch, 9);
        Assert.Equal(12.445 - 1.035 * 0.8, layer.CompositionInPoint, 9);
        Assert.Equal(12.445, layer.CompositionOutPoint, 9);
    }

    [Fact]
    public void ZeroStretchReadsAsOneHundredPercent()
    {
        Assert.Equal(1.0, Parse(new Ldta(StretchDividend: 0, StretchDivisor: 0)).Stretch);
    }

    [Fact]
    public void TimeBasedSourceClampsInAndOut()
    {
        // A 5 s precomp placed at 27 s whose out point was dragged to layer time 30 s.
        var layer = Parse(new Ldta(Start: 27, In: -1, Out: 30));
        layer.ClampSourceDuration = 5;

        Assert.Equal(27.0, layer.CompositionInPoint, 9);
        Assert.Equal(32.0, layer.CompositionOutPoint, 9);
    }

    [Fact]
    public void ClampRuleFollowsAfterEffects()
    {
        var layer = Parse(new Ldta());
        var video = new AepItem { ItemType = ItemType.Footage, DurationSeconds = 4 };
        var still = new AepItem { ItemType = ItemType.Footage, DurationSeconds = 0 };
        var comp = new AepItem { ItemType = ItemType.Composition, DurationSeconds = 5 };

        Assert.Equal(4.0, AepLayer.SourceClampDuration(layer, video));
        Assert.Equal(5.0, AepLayer.SourceClampDuration(layer, comp));
        Assert.Null(AepLayer.SourceClampDuration(layer, still));

        var reversed = Parse(new Ldta(StretchDividend: -1, StretchDivisor: 1));
        Assert.Null(AepLayer.SourceClampDuration(reversed, video));

        layer.TimeRemapEnabled = true;
        Assert.Null(AepLayer.SourceClampDuration(layer, video));
    }
}
