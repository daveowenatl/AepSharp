using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Path properties (mask shapes, shape layer paths) and masks. The synthetic cases build the
/// om-s / omks / shap lists byte for byte; the project cases need <c>AEPSHARP_ESSENTIAL_AEP</c>
/// (the EOM recap template) and skip without it.
/// </summary>
public class PathAndMaskTests
{
    // ---- builders -------------------------------------------------------------

    private static RifxBlock Utf8(string type, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return new RifxBlock { Type = type, Size = (uint)bytes.Length, Data = bytes };
    }

    private static RifxList Shap(bool closed, (double, double, double, double) bounds, params (double X, double Y)[] points)
    {
        var shph = new byte[24];
        shph[0] = 0xb3; shph[1] = 0xde; shph[2] = 0x02;
        shph[3] = (byte)(closed ? 0x01 : 0x09);
        BinaryPrimitives.WriteSingleBigEndian(shph.AsSpan(4), (float)bounds.Item1);
        BinaryPrimitives.WriteSingleBigEndian(shph.AsSpan(8), (float)bounds.Item2);
        BinaryPrimitives.WriteSingleBigEndian(shph.AsSpan(12), (float)bounds.Item3);
        BinaryPrimitives.WriteSingleBigEndian(shph.AsSpan(16), (float)bounds.Item4);
        var lhd3 = new byte[52];
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(10), (ushort)points.Length);
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(18), 8);
        var ldat = new byte[points.Length * 8];
        for (var i = 0; i < points.Length; i++)
        {
            BinaryPrimitives.WriteSingleBigEndian(ldat.AsSpan(i * 8), (float)points[i].X);
            BinaryPrimitives.WriteSingleBigEndian(ldat.AsSpan(i * 8 + 4), (float)points[i].Y);
        }
        var list = new RifxList { Identifier = "list" };
        list.Blocks.Add(new RifxBlock { Type = "lhd3", Size = 52, Data = lhd3 });
        list.Blocks.Add(new RifxBlock { Type = "ldat", Size = (uint)ldat.Length, Data = ldat });
        var shap = new RifxList { Identifier = "shap" };
        shap.Blocks.Add(new RifxBlock { Type = "shph", Size = 24, Data = shph });
        shap.Blocks.Add(new RifxBlock { Type = "LIST", Data = list });
        return shap;
    }

    private static RifxList PathProperty(RifxList? shap, string? expression)
    {
        var tdb4 = new byte[124];
        BinaryPrimitives.WriteUInt16BigEndian(tdb4, 0xDB99);
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), 1);
        var tdbs = new RifxList { Identifier = "tdbs" };
        tdbs.Blocks.Add(new RifxBlock { Type = "tdb4", Size = 124, Data = tdb4 });
        if (expression is not null)
            tdbs.Blocks.Add(Utf8("Utf8", expression));
        var oms = new RifxList { Identifier = "om-s" };
        oms.Blocks.Add(new RifxBlock { Type = "LIST", Data = tdbs });
        if (shap is not null)
        {
            var omks = new RifxList { Identifier = "omks" };
            omks.Blocks.Add(new RifxBlock { Type = "LIST", Data = shap });
            oms.Blocks.Add(new RifxBlock { Type = "LIST", Data = omks });
        }
        return oms;
    }

    private static AepProject? OpenRecap()
    {
        var path = Environment.GetEnvironmentVariable("AEPSHARP_ESSENTIAL_AEP");
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : AepProject.Open(path);
    }

    // ---- synthetic ------------------------------------------------------------

    [Fact]
    public void ClosedTriangleDecodesVerticesAndTangents()
    {
        var shap = Shap(closed: true, (10, 20, 110, 220),
            (0, 0), (0.1, 0.2), (0.3, 0.4),
            (1, 0), (1, 0), (1, 0),
            (0.5, 1), (0.4, 0.9), (0.6, 0.9));
        var property = AepProperty.ParseFromList(PathProperty(shap, null), "ADBE Mask Shape");

        var path = property.Path;
        Assert.NotNull(path);
        Assert.True(path!.Closed);
        Assert.Equal(new AepPathBounds(10, 20, 110, 220), path.Bounds);
        Assert.Equal([new AepPathPoint(0.1, 0.2), new AepPathPoint(1, 0), new AepPathPoint(0.4, 0.9)], path.Vertices.Select(p => new AepPathPoint(Math.Round(p.X, 4), Math.Round(p.Y, 4))));
        Assert.Equal([new AepPathPoint(0, 0), new AepPathPoint(1, 0), new AepPathPoint(0.5, 1)], path.InTangents.Select(p => new AepPathPoint(Math.Round(p.X, 4), Math.Round(p.Y, 4))));
        Assert.Equal([new AepPathPoint(0.3, 0.4), new AepPathPoint(1, 0), new AepPathPoint(0.6, 0.9)], path.OutTangents.Select(p => new AepPathPoint(Math.Round(p.X, 4), Math.Round(p.Y, 4))));
        Assert.Null(property.Expression);
    }

    [Fact]
    public void OpenPathWithNoVerticesKeepsItsExpression()
    {
        var shap = Shap(closed: false, (0, 0, 1, 1));
        var property = AepProperty.ParseFromList(PathProperty(shap, "createPath(pts, [], [], false)"), "ADBE Vector Shape");

        Assert.NotNull(property.Path);
        Assert.False(property.Path!.Closed);
        Assert.Empty(property.Path.Vertices);
        Assert.Equal("createPath(pts, [], [], false)", property.Expression);
        Assert.True(property.ExpressionEnabled);
    }

    [Fact]
    public void PathPropertyWithoutShapeHasNoPath()
    {
        var property = AepProperty.ParseFromList(PathProperty(null, null), "ADBE Vector Shape");
        Assert.Null(property.Path);
    }

    [Theory]
    [InlineData("Draw On", "Draw On")]
    [InlineData("-_0_/-", "")]
    [InlineData("", "")]
    public void AGroupsUserNameIsItsLabel(string stored, string label)
    {
        var group = new RifxList { Identifier = "tdgp" };
        group.Blocks.Add(Utf8("tdsn", stored));
        var property = AepProperty.ParseFromList(group, "ADBE Vector Filter - Trim");

        Assert.Equal(label, property.Label);
        Assert.Equal("ADBE Vector Filter - Trim", property.Name);
    }

    // ---- the recap template ---------------------------------------------------

    [SkippableFact]
    public void SeriesLineGroupAndTrimCarryTheirNames()
    {
        var project = OpenRecap();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var line = project!.Items.Values.Single(i => i.Name == "Animated Line Chart").CompositionLayers.Single(l => l.Name == "Series 1 Line");

        // The markers' expressions read thisLayer.content('Line').content('Draw On').end.
        var group = line.Contents!.Properties.First(p => p.MatchName == "ADBE Vector Group");
        Assert.Equal("Line", group.Label);
        var trim = Descendants(group).Single(p => p.MatchName == "ADBE Vector Filter - Trim");
        Assert.Equal("Draw On", trim.Label);
    }

    [SkippableFact]
    public void SeriesLinePathIsExpressionDrivenWithNoStaticVertices()
    {
        var project = OpenRecap();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var line = project!.Items.Values.Single(i => i.Name == "Animated Line Chart").CompositionLayers.Single(l => l.Name == "Series 1 Line");
        var shape = Descendants(line.Contents!).Single(p => p.MatchName == "ADBE Vector Shape");

        Assert.NotNull(shape.Path);
        Assert.Empty(shape.Path!.Vertices);
        Assert.False(shape.Path.Closed);
        Assert.False(shape.IsAnimated);
        Assert.True(shape.ExpressionEnabled);
        Assert.Contains("Series 1 Data Values", shape.Expression);
    }

    [SkippableFact]
    public void AmbientSolidHasOneFeatheredAddMask()
    {
        var project = OpenRecap();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var final = project!.Items.Values.Single(i => i.Name == "Final");
        var solid = final.CompositionLayers.Single(l => l.Name == "Ambient Teal");

        Assert.Equal(1, solid.MaskCount);
        var mask = Assert.Single(solid.Masks);
        Assert.Equal(AepMaskMode.Add, mask.Mode);
        Assert.Equal([410.0, 410.0], mask.Feather);
        Assert.Equal(1.0, mask.Opacity);
        Assert.NotNull(mask.Path);
        Assert.True(mask.Path!.Closed);
        Assert.Equal(4, mask.Path.Vertices.Count);
        Assert.Null(mask.Shape?.Expression);
    }

    private static IEnumerable<AepProperty> Descendants(AepProperty root)
    {
        foreach (var child in root.Properties)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
        }
    }
}
