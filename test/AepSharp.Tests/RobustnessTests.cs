using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

public class RobustnessTests
{
    [Fact]
    public void EmptyStream_ThrowsEndOfStreamException()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        Assert.Throws<EndOfStreamException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void TruncatedAfterMagic_ThrowsEndOfStreamException()
    {
        // Valid RIFX magic but no file size
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("RIFX"));
        Assert.Throws<EndOfStreamException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void TruncatedAfterFileSize_ThrowsEndOfStreamException()
    {
        // RIFX + file size but no content
        var bytes = new byte[8];
        Encoding.ASCII.GetBytes("RIFX").CopyTo(bytes, 0);
        bytes[4] = 0; bytes[5] = 0; bytes[6] = 0; bytes[7] = 100; // size = 100
        using var stream = new MemoryStream(bytes);
        Assert.Throws<EndOfStreamException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void ValidRifxButNotAep_ThrowsOnMissingNhed()
    {
        // Minimal valid RIFX with an "Egg!" identifier but no nhed block
        var bytes = BuildMinimalRifx("Egg!", Array.Empty<(string type, byte[] data)>());
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => AepProject.FromStream(stream));
    }

    [Fact]
    public void HasNhedButNoFold_ThrowsOnMissingFold()
    {
        // RIFX with nhed block but no Fold list
        var nhedData = new byte[16]; // 16 bytes, BPC at offset 15
        var blocks = new[] { ("nhed", nhedData) };
        var bytes = BuildMinimalRifx("Egg!", blocks);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => AepProject.FromStream(stream));
    }

    [Fact]
    public void TruncatedMidBlock_ThrowsEndOfStreamException()
    {
        // RIFX header says there's a block, but stream ends mid-block
        using var ms = new MemoryStream();
        // RIFX magic
        ms.Write(Encoding.ASCII.GetBytes("RIFX"));
        // File size (large enough to expect content)
        WriteBigEndianUInt32(ms, 100);
        // List identifier
        ms.Write(Encoding.ASCII.GetBytes("Egg!"));
        // Block type
        ms.Write(Encoding.ASCII.GetBytes("nhed"));
        // Block size (claims 16 bytes)
        WriteBigEndianUInt32(ms, 16);
        // Only write 4 bytes of data (truncated)
        ms.Write(new byte[4]);
        ms.Position = 0;

        Assert.Throws<EndOfStreamException>(() => RifxReader.FromStream(ms));
    }

    [Fact]
    public void NonRifxFile_Png_ThrowsInvalidDataException()
    {
        // PNG magic bytes
        var pngHeader = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var stream = new MemoryStream(pngHeader);
        Assert.Throws<InvalidDataException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void LittleEndianRiff_ThrowsInvalidDataException()
    {
        // RIFF (little-endian) instead of RIFX (big-endian)
        var bytes = new byte[12];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        using var stream = new MemoryStream(bytes);
        Assert.Throws<InvalidDataException>(() => RifxReader.FromStream(stream));
    }

    [Fact]
    public void FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() => AepProject.Open("nonexistent.aep"));
    }

    /// <summary>
    /// Build a minimal RIFX byte array with an identifier and flat (non-LIST) blocks.
    /// </summary>
    private static byte[] BuildMinimalRifx(string identifier, (string type, byte[] data)[] blocks)
    {
        using var ms = new MemoryStream();

        // Calculate total content size: 4 (identifier) + sum of blocks
        uint contentSize = 4;
        foreach (var (type, data) in blocks)
        {
            contentSize += 8 + (uint)data.Length; // type(4) + size(4) + data
            if (data.Length % 2 != 0) contentSize++; // padding
        }

        // RIFX header
        ms.Write(Encoding.ASCII.GetBytes("RIFX"));
        WriteBigEndianUInt32(ms, contentSize);

        // List identifier
        ms.Write(Encoding.ASCII.GetBytes(identifier));

        // Blocks
        foreach (var (type, data) in blocks)
        {
            ms.Write(Encoding.ASCII.GetBytes(type));
            WriteBigEndianUInt32(ms, (uint)data.Length);
            ms.Write(data);
            if (data.Length % 2 != 0) ms.WriteByte(0); // padding
        }

        return ms.ToArray();
    }

    private static void WriteBigEndianUInt32(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(buf, value);
        stream.Write(buf);
    }
}
