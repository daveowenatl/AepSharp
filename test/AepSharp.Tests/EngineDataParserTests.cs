using System.Text;
using AepSharp.EngineModel;

namespace AepSharp.Tests;

/// <summary>
/// Tests for the structural EngineData (PostScript/COS) parser. These build the
/// dict syntax up from primitives. Strings are encoded as real EngineData expects:
/// "( FE FF &lt;UTF-16BE&gt; )".
/// </summary>
public class EngineDataParserTests
{
    private static EngineValue Parse(string ascii) =>
        EngineDataParser.Parse(Encoding.ASCII.GetBytes(ascii));

    /// <summary>A real EngineData string segment: "( FE FF &lt;UTF-16BE&gt; )".</summary>
    private static byte[] Str(string s)
    {
        var bytes = new List<byte> { (byte)'(', 0xFE, 0xFF };
        bytes.AddRange(Encoding.BigEndianUnicode.GetBytes(s));
        bytes.Add((byte)')');
        return bytes.ToArray();
    }

    private static EngineValue ParseBytes(params object[] parts)
    {
        var blob = new List<byte>();
        foreach (var p in parts)
        {
            if (p is string s) blob.AddRange(Encoding.ASCII.GetBytes(s));
            else if (p is byte[] b) blob.AddRange(b);
        }
        return EngineDataParser.Parse(blob.ToArray());
    }

    [Fact]
    public void ParsesADictWithOneNumberEntry()
    {
        var root = Parse("<< /0 13 >>");

        var dict = Assert.IsType<EngineDict>(root);
        var value = Assert.IsType<EngineNumber>(dict.Get("0"));
        Assert.Equal(13.0, value.Value);
    }

    [Fact]
    public void ParsesMultipleEntriesInOrder()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /0 1 /1 2 /2 3 >>"));

        Assert.Equal(3, dict.Entries.Count);
        Assert.Equal("0", dict.Entries[0].Key);
        Assert.Equal("2", dict.Entries[2].Key);
    }

    [Fact]
    public void ParsesFloatsIncludingLeadingDot()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /0 36.0 /1 .5 /2 -2.25 >>"));

        Assert.Equal(36.0, ((EngineNumber)dict.Get("0")!).Value);
        Assert.Equal(0.5, ((EngineNumber)dict.Get("1")!).Value);
        Assert.Equal(-2.25, ((EngineNumber)dict.Get("2")!).Value);
    }

    [Fact]
    public void ParsesBooleans()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /0 true /1 false >>"));

        Assert.True(((EngineBoolean)dict.Get("0")!).Value);
        Assert.False(((EngineBoolean)dict.Get("1")!).Value);
    }

    [Fact]
    public void ParsesNameValuesAndNil()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /99 /CoolTypeFont /27 /nil >>"));

        Assert.Equal("CoolTypeFont", ((EngineName)dict.Get("99")!).Value);
        Assert.True(((EngineName)dict.Get("27")!).IsNil);
    }

    [Fact]
    public void ParsesArraysOfNumbers()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /18 [ 0.0 0.0 0.0 ] >>"));

        var arr = Assert.IsType<EngineArray>(dict.Get("18"));
        Assert.Equal(3, arr.Items.Count);
        Assert.Equal(0.0, ((EngineNumber)arr.Items[0]).Value);
    }

    [Fact]
    public void ParsesNestedDicts()
    {
        var dict = Assert.IsType<EngineDict>(Parse("<< /0 << /14 36.0 >> >>"));

        var inner = Assert.IsType<EngineDict>(dict.Get("0"));
        Assert.Equal(36.0, ((EngineNumber)inner.Get("14")!).Value);
    }

    [Fact]
    public void ParsesUtf16BeStrings()
    {
        var dict = Assert.IsType<EngineDict>(ParseBytes("<< /0 ", Str("2026 RAM"), " >>"));

        Assert.Equal("2026 RAM", ((EngineString)dict.Get("0")!).Value);
    }

    [Fact]
    public void HonorsBackslashEscapedCloseParenInStrings()
    {
        // "( FE FF 00 41 00 5C 29 00 42 )" => UTF-16BE "A)B"
        byte[] seg = { (byte)'(', 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x5C, 0x29, 0x00, 0x42, (byte)')' };
        var dict = Assert.IsType<EngineDict>(ParseBytes("<< /0 ", seg, " >>"));

        Assert.Equal("A)B", ((EngineString)dict.Get("0")!).Value);
    }

    [Fact]
    public void ParsesAnImplicitTopLevelDictBody()
    {
        // Real EngineData blobs are a bare sequence of key/value pairs, not wrapped
        // in an outer << >>: "/98 << /0 13 >> /0 << …document… >>".
        var dict = Assert.IsType<EngineDict>(Parse("/98 << /0 13 >> /0 << /14 36.0 >>"));

        Assert.Equal(2, dict.Entries.Count);
        Assert.Equal("98", dict.Entries[0].Key);
        var doc = Assert.IsType<EngineDict>(dict.Get("0"));
        Assert.Equal(36.0, ((EngineNumber)doc.Get("14")!).Value);
    }

    [Theory]
    [InlineData("<< /0 1.2.3 >>")]
    [InlineData("<< /0 - >>")]
    [InlineData("<< /0 . >>")]
    [InlineData("<< /0 5-3 >>")]
    [InlineData("<< /0 +- >>")]
    public void MalformedNumbersThrowInvalidDataException(string input)
    {
        // The char filter accepts [+-.0-9] sequences double.Parse rejects. Those must
        // surface as InvalidDataException (the contract callers catch), never
        // FormatException — one bad blob must not crash AepProject.Open.
        Assert.Throws<InvalidDataException>(() => Parse(input));
    }

    [Fact]
    public void ParsesAnArrayOfRunDicts()
    {
        // Mimics the run array: /1 [ << /0 << /0 (text) >> >> ]
        var root = ParseBytes("<< /1 [ << /0 << /0 ", Str("Hello"), " >> >> ] >>");
        var dict = Assert.IsType<EngineDict>(root);
        var runs = Assert.IsType<EngineArray>(dict.Get("1"));
        var run = Assert.IsType<EngineDict>(runs.Items[0]);
        var inner = Assert.IsType<EngineDict>(run.Get("0"));
        Assert.Equal("Hello", ((EngineString)inner.Get("0")!).Value);
    }
}
