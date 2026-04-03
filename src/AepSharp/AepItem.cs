namespace AepSharp;

public class AepItem
{
    public string Name { get; internal set; } = "";
    public uint Id { get; internal set; }
    public ItemType ItemType { get; internal set; }
    public List<AepItem> FolderContents { get; internal set; } = new();
    public ushort Width { get; internal set; }
    public ushort Height { get; internal set; }
    public double Framerate { get; internal set; }
    public double DurationSeconds { get; internal set; }
    public FootageType FootageType { get; internal set; }
    public byte[] BackgroundColor { get; internal set; } = new byte[3];
    public List<AepLayer> CompositionLayers { get; internal set; } = new();
}
