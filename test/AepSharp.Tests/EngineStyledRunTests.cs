using AepSharp.EngineModel;

namespace AepSharp.Tests;

/// <summary>
/// Tests for per-character StyleRun extraction. Builds the EngineData model tree
/// directly (the byte parser is tested separately) mirroring the real structure
/// confirmed against the TESTER ground truth: document /1 -> run /1[0] -> /0 has
/// /0 (text) and /6 (StyleRun). Each StyleRun span: /0/0/6 holds the style dict
/// (/0 font index, /1 size, /53/0/1 fill ARGB, /54/0/1 stroke ARGB), and /1 is the
/// span length.
/// </summary>
public class EngineStyledRunTests
{
    private static EngineDict D(params (string k, EngineValue v)[] e)
    {
        var d = new EngineDict();
        foreach (var (k, v) in e) d.Entries.Add(new(k, v));
        return d;
    }

    private static EngineArray Arr(params EngineValue[] items)
    {
        var a = new EngineArray();
        a.Items.AddRange(items);
        return a;
    }

    private static EngineString S(string s) => new() { Value = s };
    private static EngineNumber N(double d) => new() { Value = d };

    private static EngineValue Color(params double[] argb) => D(("0", D(("1", Arr(Array.ConvertAll(argb, N))))));

    private static EngineValue Span(double len, int fontIdx, double size, double[] fill, double[] stroke)
    {
        var style = D(
            ("0", N(fontIdx)),
            ("1", N(size)),
            ("53", Color(fill)),
            ("54", Color(stroke)));
        return D(
            ("0", D(("0", D(("6", style))))),
            ("1", N(len)));
    }

    private static EngineValue Document(string text, params EngineValue[] spans)
    {
        var runEntry = D(("0", D(
            ("0", S(text)),
            ("6", D(("0", Arr(spans)))))));
        return D(("1", D(("1", Arr(runEntry)))));
    }

    [Fact]
    public void SplitsTextIntoStyledRunsBySpanLength()
    {
        var doc = EngineTextExtractor.Extract(Document(
            "ABCD",
            Span(2, 0, 100, new double[] { 1, 1, 0, 0 }, new double[] { 1, 0, 0, 0 }),
            Span(2, 1, 50, new double[] { 1, 0, 0, 1 }, new double[] { 1, 1, 1, 1 })));

        Assert.NotNull(doc);
        Assert.Equal(2, doc!.StyledRuns.Count);

        var a = doc.StyledRuns[0];
        Assert.Equal("AB", a.Text);
        Assert.Equal(0, a.FontIndex);
        Assert.Equal(100, a.FontSize);
        Assert.Equal(new double[] { 1, 0, 0 }, a.Fill);   // ARGB -> RGB
        Assert.Equal(new double[] { 0, 0, 0 }, a.Stroke);

        var b = doc.StyledRuns[1];
        Assert.Equal("CD", b.Text);
        Assert.Equal(1, b.FontIndex);
        Assert.Equal(50, b.FontSize);
        Assert.Equal(new double[] { 0, 0, 1 }, b.Fill);   // blue
        Assert.Equal(new double[] { 1, 1, 1 }, b.Stroke); // white
    }

    [Fact]
    public void HostileSpanLengthsDoNotCorruptSlicing()
    {
        // A corrupt blob can carry a negative or absurdly large span length. The
        // cursor must never move backwards (which would duplicate text into later
        // runs) and large lengths must clamp to the remaining text.
        var doc = EngineTextExtractor.Extract(Document(
            "ABCD",
            Span(-5, 0, 10, new double[] { 1, 0, 0, 0 }, new double[] { 1, 0, 0, 0 }),
            Span(2, 0, 10, new double[] { 1, 0, 0, 0 }, new double[] { 1, 0, 0, 0 }),
            Span(1e10, 0, 10, new double[] { 1, 0, 0, 0 }, new double[] { 1, 0, 0, 0 })));

        Assert.Equal(3, doc!.StyledRuns.Count);
        Assert.Equal("", doc.StyledRuns[0].Text);     // negative length -> empty, no rewind
        Assert.Equal("AB", doc.StyledRuns[1].Text);   // starts at 0, not at -5
        Assert.Equal("CD", doc.StyledRuns[2].Text);   // huge length clamps to remainder
    }

    [Fact]
    public void TrimsTrailingCarriageReturnFromLastRun()
    {
        var doc = EngineTextExtractor.Extract(Document(
            "Hi\r",
            Span(3, 0, 20, new double[] { 1, 0, 0, 0 }, new double[] { 1, 0, 0, 0 })));

        var run = Assert.Single(doc!.StyledRuns);
        Assert.Equal("Hi", run.Text);
    }
}
