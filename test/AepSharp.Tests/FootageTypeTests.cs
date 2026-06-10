using System.Text;
using Xunit;

namespace AepSharp.Tests;

/// <summary>
/// Footage type values + alias source-path parsing. The type values were verified
/// empirically across 128 real templates (~2,100 footage items): the `opti+4`
/// uint16 maps to Image=0x01, Placeholder=0x02, AudioVideo=0x05, Vector=0x08,
/// Solid=0x09, Photoshop=0x109. boltframe (the reference parser) only named
/// Solid and Placeholder; the rest are added here.
/// </summary>
public class FootageTypeTests
{
    [Theory]
    [InlineData(0x01, FootageType.Image)]
    [InlineData(0x02, FootageType.Placeholder)]
    [InlineData(0x05, FootageType.AudioVideo)]
    [InlineData(0x08, FootageType.Vector)]
    [InlineData(0x09, FootageType.Solid)]
    [InlineData(0x109, FootageType.Photoshop)]
    public void FootageTypeValuesMapToNames(int rawValue, FootageType expected) =>
        Assert.Equal(expected, (FootageType)(ushort)rawValue);

    [Fact]
    public void ParsesFullPathFromJsonAlias()
    {
        var json = "{\"fullpath\":\"/Users/robbyw/Downloads/hero.png\",\"platform\":2,\"target_is_folder\":false}";
        var bytes = Encoding.UTF8.GetBytes(json);
        Assert.Equal("/Users/robbyw/Downloads/hero.png", AepItem.ParseAliasPath(bytes));
    }

    [Fact]
    public void ToleratesTrailingPaddingAfterJson()
    {
        var json = "{\"fullpath\":\"/x/y.mp4\"}";
        var bytes = Encoding.UTF8.GetBytes(json).Concat(new byte[] { 0, 0, 0 }).ToArray();
        Assert.Equal("/x/y.mp4", AepItem.ParseAliasPath(bytes));
    }

    [Fact]
    public void ReturnsNullForBinaryAliasFormat() =>
        // older Mac binary alias record (no JSON) — not decoded
        Assert.Null(AepItem.ParseAliasPath(new byte[] { 0x00, 0x00, 0x01, 0x5A, 0x66, 0x69, 0x6C, 0x65 }));

    [Fact]
    public void ReturnsNullWhenFullPathMissing() =>
        Assert.Null(AepItem.ParseAliasPath(Encoding.UTF8.GetBytes("{\"platform\":2}")));

    [Fact]
    public void ReturnsNullForEmptyFullPath() =>
        Assert.Null(AepItem.ParseAliasPath(Encoding.UTF8.GetBytes("{\"fullpath\":\"\"}")));

    [Fact]
    public void ReturnsNullForMalformedJson() =>
        Assert.Null(AepItem.ParseAliasPath(Encoding.UTF8.GetBytes("{\"fullpath\": not valid }")));
}
