using System.Text;
using AepSharp.EngineModel;

namespace AepSharp.Tests;

/// <summary>
/// Tests for the structural text extractor: it walks the parsed EngineData run
/// array (document /1, runs /1, each run /0/0 = text) and returns every run's
/// text in order. This replaces the heuristic "last non-noise string" and fixes
/// multi-run truncation.
/// </summary>
public class EngineTextExtractorTests
{
    private static byte[] Str(string s)
    {
        var bytes = new List<byte> { (byte)'(', 0xFE, 0xFF };
        bytes.AddRange(Encoding.BigEndianUnicode.GetBytes(s));
        bytes.Add((byte)')');
        return bytes.ToArray();
    }

    private static EngineTextDocument? Extract(params object[] parts)
    {
        var blob = new List<byte>();
        foreach (var p in parts)
        {
            if (p is string s) blob.AddRange(Encoding.ASCII.GetBytes(s));
            else if (p is byte[] b) blob.AddRange(b);
        }
        return EngineTextExtractor.Extract(EngineDataParser.Parse(blob.ToArray()));
    }

    // A run is: << /0 << /0 (text) >> >>
    private static object[] Run(string text) =>
        new object[] { " << /0 << /0 ", Str(text), " >> >> " };

    private static object[] Doc(params object[][] runs)
    {
        var parts = new List<object> { "/1 << /1 [ " };
        foreach (var r in runs) parts.AddRange(r);
        parts.Add(" ] >>");
        return parts.ToArray();
    }

    // A font-set entry: << /0 << /0 << /0 (name) >> >> >>
    private static object[] Font(string name) =>
        new object[] { " << /0 << /0 << /0 ", Str(name), " >> >> >> " };

    // The resource dict: /0 << /1 << /0 [ <font entries> ] >> >>
    private static object[] Resource(params object[][] fonts)
    {
        var parts = new List<object> { "/0 << /1 << /0 [ " };
        foreach (var f in fonts) parts.AddRange(f);
        parts.Add(" ] >> >> ");
        return parts.ToArray();
    }

    private static object[] Concat(params object[][] groups)
    {
        var parts = new List<object>();
        foreach (var g in groups) parts.AddRange(g);
        return parts.ToArray();
    }

    [Fact]
    public void ExtractsTheFontSet()
    {
        var doc = Extract(Concat(
            Resource(Font("Heebo-ExtraBold"), Font("Myriad-Roman"), Font("AdobeInvisFont")),
            Doc(Run("Hi"))));

        Assert.Equal(new[] { "Heebo-ExtraBold", "Myriad-Roman", "AdobeInvisFont" }, doc!.Fonts);
        Assert.Equal("Hi", doc.Text);
    }

    [Fact]
    public void FontsAreEmptyWhenNoResourceDict()
    {
        var doc = Extract(Doc(Run("Hi")));
        Assert.Empty(doc!.Fonts);
    }

    [Fact]
    public void ExtractsASingleRun()
    {
        var doc = Extract(Doc(Run("2026 RAM\r")));
        Assert.NotNull(doc);
        Assert.Equal("2026 RAM", doc!.Text);
        Assert.Equal(new[] { "2026 RAM\r" }, doc.Runs);
    }

    [Fact]
    public void ConcatenatesMultipleRunsInOrder()
    {
        // The multi-run case the heuristic truncates to "For".
        var doc = Extract(Doc(Run("Lease "), Run("For")));
        Assert.Equal("Lease For", doc!.Text);
        Assert.Equal(new[] { "Lease ", "For" }, doc.Runs);
    }

    [Fact]
    public void KeepsCjkRunText()
    {
        // The heuristic drops all CJK as kinsoku noise; a structural read keeps it.
        var doc = Extract(Doc(Run("セール")));
        Assert.Equal("セール", doc!.Text);
    }

    [Fact]
    public void EmptyRunYieldsEmptyText()
    {
        var doc = Extract(Doc(Run("")));
        Assert.NotNull(doc);
        Assert.Equal("", doc!.Text);
    }

    [Fact]
    public void ReturnsNullWhenNoRunArrayPresent()
    {
        // A blob with no document/run structure (just a header) isn't a text doc.
        var doc = Extract("/98 << /0 13 >>");
        Assert.Null(doc);
    }
}
