namespace AepSharp.Tests;

public class PropertyTests
{
    private readonly AepProject _project;
    private readonly AepItem _comp01;

    public PropertyTests()
    {
        _project = AepProject.Open("data/Property-01.aep");
        _comp01 = _project.RootFolder.FolderContents[0];
    }

    [Fact]
    public void TextLayer_HasNoEffects_HasText()
    {
        var textLayer = _comp01.CompositionLayers[1];
        Assert.Empty(textLayer.Effects);
        Assert.NotNull(textLayer.Text);
    }

    [Fact]
    public void ExpressionControlsLayer_Has7Effects_NoText()
    {
        var layer = _comp01.CompositionLayers[0];
        Assert.Equal(7, layer.Effects.Count);
        Assert.Null(layer.Text);
    }

    [Fact]
    public void CheckboxEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[0];
        Assert.Equal(1u, effect.Index);
        Assert.Equal("ADBE Checkbox Control", effect.MatchName);
        Assert.Equal("Checkbox Control", effect.Name);
        Assert.Equal(PropertyType.Group, effect.PropertyType);
        Assert.Single(effect.Properties);
        Assert.Equal(1u, effect.Properties[0].Index);
        Assert.Equal("ADBE Checkbox Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Checkbox", effect.Properties[0].Name);
        Assert.Equal(PropertyType.Boolean, effect.Properties[0].PropertyType);
        Assert.Empty(effect.Properties[0].Properties);
        Assert.Empty(effect.Properties[0].SelectOptions);
    }

    [Fact]
    public void SliderEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[1];
        Assert.Equal(2u, effect.Index);
        Assert.Equal("ADBE Slider Control", effect.MatchName);
        Assert.Equal("Slider Control", effect.Name);
        Assert.Equal(PropertyType.Group, effect.PropertyType);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Slider Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Slider", effect.Properties[0].Name);
        Assert.Equal(PropertyType.OneD, effect.Properties[0].PropertyType);
    }

    [Fact]
    public void PointEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[2];
        Assert.Equal(3u, effect.Index);
        Assert.Equal("ADBE Point Control", effect.MatchName);
        Assert.Equal("Point Control", effect.Name);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Point Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Point", effect.Properties[0].Name);
        Assert.Equal(PropertyType.TwoD, effect.Properties[0].PropertyType);
    }

    [Fact]
    public void ThreeDPointEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[3];
        Assert.Equal(4u, effect.Index);
        Assert.Equal("ADBE Point3D Control", effect.MatchName);
        Assert.Equal("3D Point Control", effect.Name);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Point3D Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("3D Point", effect.Properties[0].Name);
        Assert.Equal(PropertyType.ThreeD, effect.Properties[0].PropertyType);
    }

    [Fact]
    public void ColorEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[4];
        Assert.Equal(5u, effect.Index);
        Assert.Equal("ADBE Color Control", effect.MatchName);
        Assert.Equal("Color Control", effect.Name);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Color Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Color", effect.Properties[0].Name);
        Assert.Equal(PropertyType.Color, effect.Properties[0].PropertyType);
    }

    [Fact]
    public void AngleEffect_ParsedCorrectly_WithCustomLabel()
    {
        var effect = _comp01.CompositionLayers[0].Effects[5];
        Assert.Equal(6u, effect.Index);
        Assert.Equal("ADBE Angle Control", effect.MatchName);
        Assert.Equal("Angle Control", effect.Name);
        Assert.Equal("Custom Angle Control Label", effect.Label);
        Assert.Equal(PropertyType.Group, effect.PropertyType);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Angle Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Angle", effect.Properties[0].Name);
        Assert.Equal(PropertyType.Angle, effect.Properties[0].PropertyType);
    }

    [Fact]
    public void LayerSelectEffect_ParsedCorrectly()
    {
        var effect = _comp01.CompositionLayers[0].Effects[6];
        Assert.Equal(7u, effect.Index);
        Assert.Equal("ADBE Layer Control", effect.MatchName);
        Assert.Equal("Layer Control", effect.Name);
        Assert.Equal(PropertyType.Group, effect.PropertyType);
        Assert.Single(effect.Properties);
        Assert.Equal("ADBE Layer Control-0001", effect.Properties[0].MatchName);
        Assert.Equal("Layer", effect.Properties[0].Name);
        Assert.Equal(PropertyType.LayerSelect, effect.Properties[0].PropertyType);
        Assert.Empty(effect.Properties[0].Properties);
        Assert.Empty(effect.Properties[0].SelectOptions);
    }
}
