namespace AepSharp.Tests;

public class ProjectTests
{
    [Theory]
    [InlineData("data/ExEn-js.aep", "javascript-1.0")]
    [InlineData("data/ExEn-es.aep", "extendscript")]
    public void ExpressionEngine_ParsedCorrectly(string path, string expected)
    {
        var project = AepProject.Open(path);
        Assert.Equal(expected, project.ExpressionEngine);
    }

    [Theory]
    [InlineData("data/BPC-8.aep", BitsPerChannel.Bpc8)]
    [InlineData("data/BPC-16.aep", BitsPerChannel.Bpc16)]
    [InlineData("data/BPC-32.aep", BitsPerChannel.Bpc32)]
    public void BitDepth_ParsedCorrectly(string path, BitsPerChannel expected)
    {
        var project = AepProject.Open(path);
        Assert.Equal(expected, project.Depth);
    }
}
