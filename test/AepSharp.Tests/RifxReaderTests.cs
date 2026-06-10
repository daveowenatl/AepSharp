using AepSharp.Rifx;

namespace AepSharp.Tests;

public class RifxReaderTests
{
    [Fact]
    public void FromStream_ReadsAepFile_ReturnsEggIdentifier()
    {
        using var stream = File.OpenRead("data/BPC-8.aep");
        var root = RifxReader.FromStream(stream);

        Assert.Equal("Egg!", root.Identifier);
        Assert.NotEmpty(root.Blocks);
    }

    [Fact]
    public void FromStream_InvalidMagic_ThrowsInvalidDataException()
    {
        using var stream = new MemoryStream(new byte[] { 0, 0, 0, 0 });
        Assert.Throws<InvalidDataException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void BtdkList_IsCapturedAsOpaquePayloadNotParsedAsChunks()
    {
        // A text layer's EngineData lives in LIST 'btdk', whose contents are
        // PostScript-style text, NOT RIFF chunks. The reader must capture the
        // payload raw — discovery must not depend on the bytes happening to
        // misparse as an oversized chunk (the old ANON accident).
        var payload = System.Text.Encoding.ASCII.GetBytes("/98 << /0 13 >> data");

        using var ms = new MemoryStream();
        var body = 4                       // root identifier
                 + 8 + 4 + payload.Length; // LIST header + 'btdk' + payload
        ms.Write(System.Text.Encoding.ASCII.GetBytes("RIFX"));
        WriteBe(ms, (uint)body);
        ms.Write(System.Text.Encoding.ASCII.GetBytes("Egg!"));
        ms.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
        WriteBe(ms, (uint)(4 + payload.Length));
        ms.Write(System.Text.Encoding.ASCII.GetBytes("btdk"));
        ms.Write(payload);
        ms.Position = 0;

        var root = RifxReader.FromStream(ms);
        var block = Assert.Single(root.Blocks);
        var list = Assert.IsType<RifxList>(block.Data);
        Assert.Equal("btdk", list.Identifier);
        Assert.Equal(payload, list.RawPayload);
        Assert.Empty(list.Blocks);
    }

    private static void WriteBe(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        stream.Write(buf);
    }
}
