namespace AepSharp.Tests;

public class ItemTests
{
    private readonly AepProject _project;

    public ItemTests()
    {
        _project = AepProject.Open("data/Item-01.aep");
    }

    [Fact]
    public void RootFolder_HasIdZero()
    {
        Assert.Equal(0u, _project.RootFolder.Id);
    }

    [Fact]
    public void Folder01_ParsedCorrectly()
    {
        var folder01 = _project.RootFolder.FolderContents[0];
        Assert.Equal("Folder 01", folder01.Name);
        Assert.Equal(46u, folder01.Id);
        Assert.Equal(ItemType.Folder, folder01.ItemType);
    }

    [Fact]
    public void Folder02_ParsedCorrectly()
    {
        var folder02 = _project.RootFolder.FolderContents[1];
        Assert.Equal("Folder 02", folder02.Name);
        Assert.Equal(47u, folder02.Id);
        Assert.Equal(ItemType.Folder, folder02.ItemType);
    }

    [Fact]
    public void Comp01_ParsedCorrectly()
    {
        var comp01 = _project.RootFolder.FolderContents[0].FolderContents[0];
        Assert.Equal("Comp 01", comp01.Name);
        Assert.Equal(48u, comp01.Id);
        Assert.Equal(ItemType.Composition, comp01.ItemType);
        Assert.Equal((ushort)351, comp01.Width);
        Assert.Equal((ushort)856, comp01.Height);
        Assert.Equal(21.0, comp01.Framerate);
        Assert.Equal(31.0, comp01.DurationSeconds);
        Assert.Equal(new byte[] { 15, 75, 82 }, comp01.BackgroundColor);
    }

    [Fact]
    public void Comp02_ParsedCorrectly()
    {
        var comp02 = _project.RootFolder.FolderContents[1].FolderContents[0];
        Assert.Equal("Comp 02", comp02.Name);
        Assert.Equal(59u, comp02.Id);
        Assert.Equal(ItemType.Composition, comp02.ItemType);
        Assert.Equal((ushort)452, comp02.Width);
        Assert.Equal((ushort)639, comp02.Height);
        Assert.Equal(29.97, comp02.Framerate);
        Assert.Equal(71.338004671338, comp02.DurationSeconds, 10);
        Assert.Equal(new byte[] { 145, 206, 85 }, comp02.BackgroundColor);
    }

    [Fact]
    public void FootageFolder_ParsedCorrectly()
    {
        var footageFolder = _project.RootFolder.FolderContents[2];
        Assert.Equal("Footage", footageFolder.Name);
        Assert.Equal(70u, footageFolder.Id);
    }

    [Fact]
    public void PlaceholderFootage_ParsedCorrectly()
    {
        var placeholder = _project.RootFolder.FolderContents[2].FolderContents[2];
        Assert.Equal("Missing Footage", placeholder.Name);
        Assert.Equal(71u, placeholder.Id);
        Assert.Equal(ItemType.Footage, placeholder.ItemType);
        Assert.Equal(127.0, placeholder.DurationSeconds);
        Assert.Equal(123.45669555664062, placeholder.Framerate, 10);
        Assert.Equal((ushort)1234, placeholder.Width);
        Assert.Equal((ushort)5678, placeholder.Height);
    }

    [Fact]
    public void RedSolid_ParsedCorrectly()
    {
        var redSolid = _project.RootFolder.FolderContents[2].FolderContents[3];
        Assert.Equal(FootageType.Solid, redSolid.FootageType);
        Assert.Equal("Red Solid 1", redSolid.Name);
    }
}
