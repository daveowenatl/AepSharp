namespace AepSharp.Tests;

/// <summary>
/// Essential Graphics tables. The checked-in fixtures publish nothing, so these run against a
/// project named by <c>AEPSHARP_ESSENTIAL_AEP</c> (a template with Essential Graphics controls;
/// the values asserted are the EOM recap template's) and skip without it.
/// </summary>
public class EssentialPropertyTests
{
    private static AepProject? Open()
    {
        var path = Environment.GetEnvironmentVariable("AEPSHARP_ESSENTIAL_AEP");
        return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : AepProject.Open(path);
    }

    private static AepItem Comp(AepProject project, string name) =>
        project.Items.Values.Single(i => i.ItemType == ItemType.Composition && i.Name == name);

    [SkippableFact]
    public void CompositionsWithoutPanelPublishNothing()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        Assert.Empty(Comp(project!, "Final").EssentialProperties);
        var fixture = AepProject.Open("data/Item-01.aep");
        Assert.All(fixture.Items.Values, i => Assert.Empty(i.EssentialProperties));
    }

    [SkippableFact]
    public void ColumnChartPublishesTwentyFourControlsInPanelOrder()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var props = Comp(project!, "Animated Column Chart").EssentialProperties;
        Assert.Equal(24, props.Count);
        Assert.Equal("Compact Numbers", props[0].Name);
        Assert.Equal("Column 1 Label", props[^1].Name);
        Assert.Contains(props, p => p.Name == "Unit Type" && p.Kind == EssentialPropertyKind.Dropdown);
    }

    [SkippableFact]
    public void SliderReadsValueRangeAndEffectPath()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var slider = Comp(project!, "Animated Column Chart").EssentialProperties.Single(p => p.Name == "Column 1 Value");
        Assert.Equal(EssentialPropertyKind.Slider, slider.Kind);
        Assert.Equal(496u, slider.LayerId);
        Assert.Equal([76.0], slider.Value);
        Assert.Equal([76.0], slider.Default);
        Assert.Equal(0.0, slider.SliderMin);
        Assert.Equal(100.0, slider.SliderMax);
        Assert.Equal(["ADBE Effect Parade", "ADBE Slider Control", "ADBE Slider Control-0001"], slider.Path.Select(s => s.MatchName));
        Assert.Equal([null, 0u, 1u], slider.Path.Select(s => s.Index));
        Assert.Matches("^[0-9a-f-]{36}$", slider.Guid);
    }

    [SkippableFact]
    public void TextControlPointsAtItsLayerAndFont()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var title = Comp(project!, "Animated Column Chart").EssentialProperties.Single(p => p.Name == "Chart Title");
        Assert.Equal(EssentialPropertyKind.Text, title.Kind);
        Assert.Equal(106u, title.LayerId);
        Assert.Null(title.Value);
        Assert.Equal(["ADBE Text Properties", "ADBE Text Document"], title.Path.Select(s => s.MatchName));
        Assert.NotNull(title.FontName);
    }

    [SkippableFact]
    public void CheckboxAndDropdownDecodeAsNumbers()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var props = Comp(project!, "Animated Column Chart").EssentialProperties;
        var compact = props.Single(p => p.Name == "Compact Numbers");
        Assert.Equal(EssentialPropertyKind.Checkbox, compact.Kind);
        Assert.NotNull(compact.Value);
        Assert.Single(compact.Value!);
        var unit = props.Single(p => p.Name == "Unit Type");
        Assert.Equal(EssentialPropertyKind.Dropdown, unit.Kind);
        Assert.NotNull(unit.Value);
        Assert.StartsWith("Pseudo/", unit.Path[1].MatchName);
    }

    [SkippableFact]
    public void MetricPublishesAColor()
    {
        var project = Open();
        Skip.If(project is null, "AEPSHARP_ESSENTIAL_AEP not set");
        var color = Comp(project!, "Animated Metric").EssentialProperties.Single(p => p.Kind == EssentialPropertyKind.Color);
        Assert.NotNull(color.Value);
        Assert.Equal(4, color.Value!.Count);
    }
}
