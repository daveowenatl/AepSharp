using AepSharp.Rifx;

namespace AepSharp;

public class AepProject
{
    public string ExpressionEngine { get; internal set; } = "";
    public BitsPerChannel Depth { get; internal set; }
    public AepItem RootFolder { get; internal set; } = null!;
    public Dictionary<uint, AepItem> Items { get; } = new();

    public static AepProject Open(string path)
    {
        using var stream = File.OpenRead(path);
        return FromStream(stream);
    }

    public static AepProject FromStream(Stream stream)
    {
        var root = RifxReader.FromStream(stream);
        return Parse(root);
    }

    internal static AepProject Parse(RifxList root)
    {
        var project = new AepProject();

        // Parse expression engine
        var exEnList = root.SublistFind("ExEn");
        if (exEnList != null)
        {
            project.ExpressionEngine = exEnList.Blocks[0].ToAsciiString();
        }

        // Parse project header (nhed block) — BPC at offset 15
        var nhedBlock = root.Find(b => b.Type == "nhed");
        if (nhedBlock == null)
            throw new InvalidDataException("Missing nhed block in project");
        var nhedData = nhedBlock.GetBytes();
        project.Depth = (BitsPerChannel)nhedData[15];

        // Parse root folder
        var rootFolderList = root.SublistFind("Fold");
        if (rootFolderList == null)
            throw new InvalidDataException("Missing root Fold list in project");
        project.RootFolder = AepItem.Parse(rootFolderList, project, isRoot: true);

        // Layers without explicit names inherit from their source item
        foreach (var item in project.Items.Values)
        {
            if (item.ItemType == ItemType.Composition)
            {
                foreach (var layer in item.CompositionLayers)
                {
                    if (string.IsNullOrEmpty(layer.Name) && project.Items.TryGetValue(layer.SourceId, out var source))
                        layer.Name = source.Name;
                }
            }
        }

        return project;
    }
}
