using System.Text;
using System.Text.Json;
using AepSharp.Rifx;

namespace AepSharp.Tests;

public class RifxDumpTests
{
    private static RifxList ReadFixture(string name)
    {
        using var stream = File.OpenRead(Path.Combine("data", name));
        return RifxReader.FromStream(stream);
    }

    [Fact]
    public void Reader_StampsRootListOffsetAfterHeader()
    {
        var root = ReadFixture("Item-01.aep");
        // RIFX magic (4) + file size (4) = 8, then the list identifier begins.
        Assert.Equal(8, root.Offset);
        Assert.Equal("Egg!", root.Identifier);
    }

    [Fact]
    public void Reader_StampsMonotonicBlockOffsets()
    {
        var root = ReadFixture("Item-01.aep");
        Assert.NotEmpty(root.Blocks);

        long previous = -1;
        foreach (var block in root.Blocks)
        {
            Assert.True(block.Offset > previous,
                $"block '{block.Type}' offset {block.Offset} not greater than {previous}");
            previous = block.Offset;
        }
    }

    [Fact]
    public void Reader_FirstBlockOffsetFollowsRootIdentifier()
    {
        var root = ReadFixture("Item-01.aep");
        // Root identifier occupies offsets 8..11, so the first block's type tag starts at 12.
        Assert.Equal(12, root.Blocks[0].Offset);
    }

    [Fact]
    public void ToText_IncludesHeaderOffsetsAndNestedIdentifiers()
    {
        var text = RifxDump.ToText(ReadFixture("Item-01.aep"));
        Assert.StartsWith("RIFX  Egg!", text);
        Assert.Contains("[0x", text);
        Assert.Contains("declared=", text);
        Assert.Contains("consumed=", text);
        // The folder/item hierarchy should surface as nested lists.
        Assert.Contains("-> Fold", text);
    }

    [Fact]
    public void ToText_CanOmitPreview()
    {
        var withPreview = RifxDump.ToText(ReadFixture("Item-01.aep"));
        var without = RifxDump.ToText(ReadFixture("Item-01.aep"),
            new RifxDump.Options { IncludePreview = false });

        Assert.Contains("'", withPreview);   // ascii preview present
        Assert.DoesNotContain("| ", without); // no preview column
    }

    [Fact]
    public void ToJson_ProducesParseableTreeWithOffsets()
    {
        var json = RifxDump.ToJson(ReadFixture("Item-01.aep"));
        using var doc = JsonDocument.Parse(json);
        var rootEl = doc.RootElement;

        Assert.Equal("list", rootEl.GetProperty("kind").GetString());
        Assert.Equal("Egg!", rootEl.GetProperty("identifier").GetString());
        Assert.Equal(8, rootEl.GetProperty("offset").GetInt64());

        var firstBlock = rootEl.GetProperty("blocks")[0];
        Assert.Equal("block", firstBlock.GetProperty("kind").GetString());
        Assert.True(firstBlock.GetProperty("offset").GetInt64() >= 12);
        Assert.False(firstBlock.GetProperty("anomalous").GetBoolean());
    }

    [Fact]
    public void TruncatedFile_ProducesAnomalousBlock()
    {
        // Body = identifier(4) + block header(8) + 180 payload bytes = 192. The block
        // declares a size far beyond the 180 bytes that remain, so the reader captures
        // the remainder as ANON. Declared size is even to avoid the trailing pad read.
        const int payload = 180;
        using var ms = new MemoryStream();
        ms.Write(Encoding.ASCII.GetBytes("RIFX"));
        WriteBe(ms, (uint)(4 + 8 + payload));   // file size (body length) = 192
        ms.Write(Encoding.ASCII.GetBytes("Egg!")); // list identifier
        ms.Write(Encoding.ASCII.GetBytes("blok"));  // block type
        WriteBe(ms, 9998);                      // declared size far beyond remaining
        ms.Write(new byte[payload]);            // the actual remaining bytes
        ms.Position = 0;

        var root = RifxReader.FromStream(ms);
        var anon = Assert.Single(root.Blocks);
        Assert.Equal("ANON", anon.Type);
        Assert.True(anon.IsAnomalous);

        var text = RifxDump.ToText(root);
        Assert.Contains("ANOMALOUS", text);
    }

    private static void WriteBe(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        stream.Write(buf);
    }
}
