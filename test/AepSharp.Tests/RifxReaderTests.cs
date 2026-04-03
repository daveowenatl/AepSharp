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
}
