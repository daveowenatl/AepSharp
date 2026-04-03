namespace AepSharp.Tests;

public class LayerTests
{
    private readonly AepProject _project;
    private readonly AepItem _comp01;

    public LayerTests()
    {
        _project = AepProject.Open("data/Layer-01.aep");
        _comp01 = _project.RootFolder.FolderContents[0];
    }

    [Fact]
    public void CollapseTransform_Enabled() =>
        Assert.True(_comp01.CompositionLayers[0].CollapseTransformEnabled);

    [Fact]
    public void Effects_Enabled() =>
        Assert.True(_comp01.CompositionLayers[1].EffectsEnabled);

    [Fact]
    public void MotionBlur_Enabled() =>
        Assert.True(_comp01.CompositionLayers[2].MotionBlurEnabled);

    [Fact]
    public void Shy_Enabled() =>
        Assert.True(_comp01.CompositionLayers[4].ShyEnabled);

    [Fact]
    public void AdjustmentLayer_Enabled() =>
        Assert.True(_comp01.CompositionLayers[5].AdjustmentLayerEnabled);

    [Fact]
    public void ThreeD_Enabled() =>
        Assert.True(_comp01.CompositionLayers[6].ThreeDEnabled);

    [Fact]
    public void Solo_Enabled() =>
        Assert.True(_comp01.CompositionLayers[7].SoloEnabled);

    [Fact]
    public void Guide_Enabled() =>
        Assert.True(_comp01.CompositionLayers[8].GuideEnabled);

    [Fact]
    public void FrameBlendMode_PixelMotion() =>
        Assert.Equal(LayerFrameBlendMode.PixelMotion, _comp01.CompositionLayers[9].FrameBlendMode);

    [Fact]
    public void FrameBlendMode_FrameMix() =>
        Assert.Equal(LayerFrameBlendMode.FrameMix, _comp01.CompositionLayers[10].FrameBlendMode);

    [Fact]
    public void Quality_Wireframe() =>
        Assert.Equal(LayerQuality.Wireframe, _comp01.CompositionLayers[11].Quality);

    [Fact]
    public void Quality_Draft() =>
        Assert.Equal(LayerQuality.Draft, _comp01.CompositionLayers[12].Quality);

    [Fact]
    public void Quality_Best() =>
        Assert.Equal(LayerQuality.Best, _comp01.CompositionLayers[13].Quality);

    [Fact]
    public void SamplingMode_Bilinear() =>
        Assert.Equal(LayerSamplingMode.Bilinear, _comp01.CompositionLayers[14].SamplingMode);

    [Fact]
    public void SamplingMode_Bicubic() =>
        Assert.Equal(LayerSamplingMode.Bicubic, _comp01.CompositionLayers[15].SamplingMode);

    [Fact]
    public void VideoAndAudio_Enabled()
    {
        Assert.True(_comp01.CompositionLayers[16].VideoEnabled);
        Assert.True(_comp01.CompositionLayers[16].AudioEnabled);
    }
}
