using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Footage source paths (Pin → Als2 → alas JSON), file-name fallback for unnamed
/// footage, and opti-based footage types/names. Payload shapes mirror production
/// templates; the JSON layout matches py-aep's parse_alas_data.
/// </summary>
public class FootageSourceTests
{
    private static RifxBlock Block(string type, byte[] data) => new() { Type = type, Size = (uint)data.Length, Data = data };

    private static RifxBlock List(string identifier, params RifxBlock[] blocks)
    {
        var list = new RifxList { Identifier = identifier };
        list.Blocks.AddRange(blocks);
        return new RifxBlock { Type = "LIST", Data = list };
    }

    private static byte[] Opti(string fourCc, ushort type, int length, string? nameAt68 = null)
    {
        var opti = new byte[length];
        Encoding.ASCII.GetBytes(fourCc).CopyTo(opti, 0);
        BinaryPrimitives.WriteUInt16BigEndian(opti.AsSpan(4), type);
        if (nameAt68 is not null)
            Encoding.UTF8.GetBytes(nameAt68).CopyTo(opti, 0x44);
        return opti;
    }

    private static AepItem Footage(string name, byte[]? alas, byte[] opti)
    {
        var idta = new byte[20];
        BinaryPrimitives.WriteUInt16BigEndian(idta, 0x07);
        BinaryPrimitives.WriteUInt32BigEndian(idta.AsSpan(16), 42);

        var sspc = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(sspc.AsSpan(42), 1);

        var pin = new List<RifxBlock> { Block("sspc", sspc) };
        if (alas is not null)
            pin.Add(List("Als2", Block("alas", alas)));
        pin.Add(Block("opti", opti));

        var item = new RifxList { Identifier = "Item" };
        item.Blocks.Add(Block("Utf8", Encoding.UTF8.GetBytes(name)));
        item.Blocks.Add(Block("idta", idta));
        item.Blocks.Add(List("Pin ", pin.ToArray()));
        return AepItem.Parse(item, new AepProject());
    }

    private static byte[] Alas(string fullPath) => Encoding.UTF8.GetBytes(
        $$"""{"ascendcount_base":2,"ascendcount_target":3,"fullpath":{{System.Text.Json.JsonSerializer.Serialize(fullPath)}},"platform":2,"target_is_folder":false}""");

    [Fact]
    public void ReadsTheFullPathAndFallsBackToTheFileNameForUnnamedFootage()
    {
        var item = Footage("", Alas("/Users/x/(Footage)/CTA.png"), Opti("png!", 0x01, 322));

        Assert.Equal("/Users/x/(Footage)/CTA.png", item.SourcePath);
        Assert.Equal("CTA.png", item.Name);
        Assert.Equal(FootageType.Image, item.FootageType);
    }

    [Fact]
    public void KeepsAnExplicitItemName()
    {
        var item = Footage("Hero shot", Alas("/Users/x/Video_Footage.mp4"), Opti("MP4 ", 0x05, 58));

        Assert.Equal("Hero shot", item.Name);
        Assert.Equal(FootageType.AudioVideo, item.FootageType);
    }

    [Fact]
    public void TakesTheFileNameOfAWindowsPath()
    {
        var item = Footage("", Alas(@"C:\Projects\Elements\Logo.ai"), Opti("TEXT", 0x08, 596));

        Assert.Equal(@"C:\Projects\Elements\Logo.ai", item.SourcePath);
        Assert.Equal("Logo.ai", item.Name);
        Assert.Equal(FootageType.Vector, item.FootageType);
    }

    [Fact]
    public void PrefixesTheSourceLayerOfALayeredVectorImport()
    {
        var item = Footage("", Alas("/Users/x/Event Logo For AE.ai"), Opti("TEXT", 0x08, 596, nameAt68: "Bracket Shadow"));

        Assert.Equal("Bracket Shadow/Event Logo For AE.ai", item.Name);
    }

    [Theory]
    [InlineData("{\"fullpath\": ")]           // truncated JSON
    [InlineData("{\"fullpath\": 12}")]        // wrong type
    [InlineData("[\"/Users/x/a.png\"]")]      // not an object
    [InlineData("\u0000\u0000\u0001binary")]  // legacy binary alias
    public void MalformedOrBinaryAliasYieldsNoPath(string payload)
    {
        var item = Footage("", Encoding.UTF8.GetBytes(payload), Opti("png!", 0x01, 322));

        Assert.Null(item.SourcePath);
        Assert.Equal("", item.Name);
    }

    [Fact]
    public void MissingAliasYieldsNoPath()
    {
        var item = Footage("Named", alas: null, Opti("png!", 0x01, 322));

        Assert.Null(item.SourcePath);
    }

    [Fact]
    public void SolidNameStopsAtTheTerminator()
    {
        // Production files carry stale bytes after the NUL ("Black Solid 3\0 1").
        var opti = Opti("Soli", 0x09, 282);
        Encoding.ASCII.GetBytes("Black Solid 3\0 1").CopyTo(opti, 26);

        var item = Footage("", alas: null, opti);

        Assert.Equal("Black Solid 3", item.Name);
        Assert.Null(item.SourcePath);
    }
}
