using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Tests for static property-value decoding from a tdbs list (tdb4 + cdat).
/// Layout pinned against ground truth (TESTER + fixtures): tdb4 carries the
/// component count at u16 offset 2 (big-endian); cdat is big-endian doubles and
/// the value is the first &lt;components&gt; of them (the rest are reserved slots).
/// Percent-typed values are stored as fractions (100% = 1.0).
/// </summary>
public class PropertyValueTests
{
    private static RifxList Tdbs(int components, params double[] cdatDoubles)
    {
        var tdb4 = new byte[124];
        tdb4[0] = 0xDB; tdb4[1] = 0x99;
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), (ushort)components);

        var cdat = new byte[cdatDoubles.Length * 8];
        for (var i = 0; i < cdatDoubles.Length; i++)
            BinaryPrimitives.WriteDoubleBigEndian(cdat.AsSpan(i * 8), cdatDoubles[i]);

        return new RifxList
        {
            Identifier = "tdbs",
            Blocks =
            {
                new RifxBlock { Type = "tdb4", Size = 124, Data = tdb4 },
                new RifxBlock { Type = "cdat", Size = (uint)cdat.Length, Data = cdat },
            },
        };
    }

    [Fact]
    public void DecodesAThreeComponentValue()
    {
        // Position-like: 3 components, reserved slots after the value.
        var prop = AepProperty.ParseFromList(Tdbs(3, 206.275, 800.604, 0, 0, 0, 0, 0, 0, 0), "ADBE Position");

        Assert.Equal(new[] { 206.275, 800.604, 0.0 }, prop.Value);
    }

    [Fact]
    public void DecodesAScalarValue()
    {
        // Opacity-like: 1 component (100% stored as 1.0), 4 reserved slots.
        var prop = AepProperty.ParseFromList(Tdbs(1, 1.0, 0, 0, 0, 0), "ADBE Opacity");

        Assert.Equal(new[] { 1.0 }, prop.Value);
    }

    [Fact]
    public void ClampsComponentsToAvailableData()
    {
        // A truncated/hostile cdat shorter than the declared component count must
        // not throw; take what's actually there.
        var prop = AepProperty.ParseFromList(Tdbs(3, 5.0), "ADBE Position");

        Assert.Equal(new[] { 5.0 }, prop.Value);
    }

    [Fact]
    public void NoCdatMeansNoValue()
    {
        // Animated properties store keyframes (no static cdat) — Value stays null.
        var list = new RifxList
        {
            Identifier = "tdbs",
            Blocks = { new RifxBlock { Type = "tdb4", Size = 124, Data = new byte[124] } },
        };
        var prop = AepProperty.ParseFromList(list, "ADBE Position");

        Assert.Null(prop.Value);
    }

    [Fact]
    public void NonTdbsListsHaveNoValue()
    {
        var prop = AepProperty.ParseFromList(new RifxList { Identifier = "tdgp" }, "ADBE Group");

        Assert.Null(prop.Value);
    }
}
