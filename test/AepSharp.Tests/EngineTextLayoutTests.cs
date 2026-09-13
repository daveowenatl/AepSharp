using AepSharp.EngineModel;

namespace AepSharp.Tests;

/// <summary>
/// Paragraph justification, box text geometry, tracking and leading from the EngineData
/// model. Key paths per py-aep: the first paragraph style is at run /0/5/0[0]/0/0/5
/// (justification /0, auto-leading factor /7); the text frame is at resource
/// /0/8/0[0]/0, whose /1/0 outline marks box text; character style /4 auto leading,
/// /5 leading, /8 tracking.
/// </summary>
public class EngineTextLayoutTests
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

    private static EngineNumber N(double d) => new() { Value = d };

    private static EngineValue Document(EngineDict? paragraphStyle, EngineDict? charStyle, EngineDict? frame)
    {
        var content = D(("0", new EngineString { Value = "Hello" }));
        if (paragraphStyle is not null)
            content.Entries.Add(new("5", D(("0", Arr(D(("0", D(("0", D(("5", paragraphStyle))))), ("1", N(5))))))));
        if (charStyle is not null)
            content.Entries.Add(new("6", D(("0", Arr(D(("0", D(("0", D(("6", charStyle))))), ("1", N(5))))))));

        var top = D(("1", D(("1", Arr(D(("0", content)))))));
        if (frame is not null)
            top.Entries.Insert(0, new("0", D(("8", D(("0", Arr(D(("0", frame)))))))));
        return top;
    }

    // Box outline as After Effects writes it (py-aep's _box_coords): 16 vertices tracing
    // the corners with repeats — two at top-left, four at top-right, four at bottom-right,
    // four at bottom-left, two at top-left — so vertex 6 is the bottom-right corner.
    private static EngineArray Outline(double left, double top, double width, double height)
    {
        double r = left + width, b = top + height;
        var vertices = new (double x, double y)[]
        {
            (left, top), (left, top), (r, top), (r, top), (r, top), (r, top), (r, b), (r, b),
            (r, b), (r, b), (left, b), (left, b), (left, b), (left, b), (left, top), (left, top),
        };
        return Arr(vertices.SelectMany(v => new EngineValue[] { N(v.x), N(v.y) }).ToArray());
    }

    [Theory]
    [InlineData(0, TextJustification.Left)]
    [InlineData(1, TextJustification.Right)]
    [InlineData(2, TextJustification.Center)]
    [InlineData(3, TextJustification.FullJustifyLastLineLeft)]
    [InlineData(6, TextJustification.FullJustifyLastLineFull)]
    public void DecodesJustificationCodes(int code, TextJustification expected)
    {
        var doc = EngineTextExtractor.Extract(Document(D(("0", N(code))), null, null))!;

        Assert.Equal(code, doc.Justification);
        Assert.Equal(expected, (TextJustification)doc.Justification!.Value);
    }

    [Fact]
    public void MissingParagraphStyleHasNoJustification()
    {
        Assert.Null(EngineTextExtractor.Extract(Document(null, null, null))!.Justification);
    }

    [Fact]
    public void BoxTextExposesSizeAndTopLeft()
    {
        // Production values: a centred 2426.6 x 55 box whose top-left is (-1390.9, -75.5).
        var frame = D(("1", D(("0", Outline(-1390.90906, -75.5, 2426.63623, 55)))), ("2", D()));
        var doc = EngineTextExtractor.Extract(Document(null, null, frame))!;

        Assert.True(doc.IsBoxText);
        Assert.Equal(2426.63623, doc.BoxSize![0], 5);
        Assert.Equal(55.0, doc.BoxSize[1], 5);
        Assert.Equal(new[] { -1390.90906, -75.5 }, doc.BoxPosition);
    }

    [Fact]
    public void PointTextHasNoBox()
    {
        var doc = EngineTextExtractor.Extract(Document(null, null, D(("2", D()))))!;

        Assert.False(doc.IsBoxText);
        Assert.Null(doc.BoxSize);
        Assert.Null(doc.BoxPosition);
    }

    [Fact]
    public void ShortOutlineIsBoxTextWithoutGeometry()
    {
        var frame = D(("1", D(("0", Arr(N(0), N(0), N(10))))));
        var doc = EngineTextExtractor.Extract(Document(null, null, frame))!;

        Assert.True(doc.IsBoxText);
        Assert.Null(doc.BoxSize);
    }

    [Fact]
    public void AutoLeadingUsesTheParagraphFactor()
    {
        var doc = EngineTextExtractor.Extract(Document(
            D(("0", N(0)), ("7", N(1.5))),
            D(("1", N(40)), ("5", N(0.01)), ("8", N(-25))),
            null))!;

        var run = Assert.Single(doc.StyledRuns);
        Assert.Equal(60.0, run.Leading);
        Assert.Equal(-25.0, run.Tracking);
    }

    [Fact]
    public void ExplicitLeadingWhenAutoLeadingIsOff()
    {
        var doc = EngineTextExtractor.Extract(Document(
            D(("7", N(1.2))),
            D(("1", N(40)), ("4", new EngineBoolean { Value = false }), ("5", N(52))),
            null))!;

        Assert.Equal(52.0, doc.StyledRuns[0].Leading);
        Assert.Null(doc.StyledRuns[0].Tracking);
    }

    [Fact]
    public void FixtureTextLayerIsCentredPointText()
    {
        var project = AepProject.Open("data/Property-01.aep");
        var text = project.RootFolder.FolderContents[0].CompositionLayers[1];

        Assert.Equal(TextJustification.Center, text.TextJustification);
        Assert.False(text.IsBoxText);
        Assert.Null(text.TextBoxSize);
        Assert.Equal(0.0, text.TextRuns[0].Tracking);
        Assert.Equal(222.0, text.TextRuns[0].Leading);
    }
}
