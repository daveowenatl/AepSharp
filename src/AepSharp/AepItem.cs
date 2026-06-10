using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using AepSharp.Rifx;

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

    /// <summary>
    /// For file-backed footage (image, audio/video, vector, Photoshop), the source
    /// file path recorded in the item's alias. Null for solids, placeholders, and
    /// non-footage items, or when the alias can't be read.
    /// </summary>
    public string? SourcePath { get; internal set; }

    internal static AepItem Parse(RifxList itemHead, AepProject project, bool isRoot = false)
    {
        var item = new AepItem();

        if (isRoot)
        {
            item.Id = 0;
            item.Name = "root";
            item.ItemType = ItemType.Folder;
        }
        else
        {
            // Parse name
            var nameBlock = itemHead.FindByType("Utf8");
            if (nameBlock != null)
                item.Name = nameBlock.ToAsciiString();

            // Parse idta — type at offset 0 (uint16), ID at offset 16 (uint32)
            var idtaBlock = itemHead.FindByType("idta");
            if (idtaBlock == null)
                throw new InvalidDataException("Missing idta block in item");
            var idtaData = idtaBlock.GetBytes();
            var typeValue = BinaryPrimitives.ReadUInt16BigEndian(idtaData.AsSpan(0));
            item.Id = BinaryPrimitives.ReadUInt32BigEndian(idtaData.AsSpan(16));
            item.ItemType = typeValue switch
            {
                0x01 => ItemType.Folder,
                0x04 => ItemType.Composition,
                0x07 => ItemType.Footage,
                _ => throw new InvalidDataException($"Unknown item type: 0x{typeValue:X4}")
            };
        }

        switch (item.ItemType)
        {
            case ItemType.Folder:
                {
                    var childLists = new List<RifxList>();
                    childLists.AddRange(itemHead.SublistFilter("Item"));
                    var sfdr = itemHead.SublistMerge("Sfdr");
                    childLists.AddRange(sfdr.SublistFilter("Item"));
                    foreach (var childList in childLists)
                    {
                        var child = Parse(childList, project);
                        item.FolderContents.Add(child);
                    }
                    break;
                }
            case ItemType.Footage:
                {
                    var pinList = itemHead.SublistFind("Pin ");
                    if (pinList == null)
                        throw new InvalidDataException("Missing Pin list in footage item");

                    var sspcBlock = pinList.FindByType("sspc");
                    if (sspcBlock == null)
                        throw new InvalidDataException("Missing sspc block in footage item");
                    var sspc = sspcBlock.GetBytes();
                    item.Width = (ushort)BinaryPrimitives.ReadUInt32BigEndian(sspc.AsSpan(30));
                    item.Height = (ushort)BinaryPrimitives.ReadUInt32BigEndian(sspc.AsSpan(34));
                    var secDividend = BinaryPrimitives.ReadUInt32BigEndian(sspc.AsSpan(38));
                    var secDivisor = BinaryPrimitives.ReadUInt32BigEndian(sspc.AsSpan(42));
                    item.DurationSeconds = (double)secDividend / secDivisor;
                    var fpsWhole = BinaryPrimitives.ReadUInt32BigEndian(sspc.AsSpan(56));
                    var fpsFrac = BinaryPrimitives.ReadUInt16BigEndian(sspc.AsSpan(60));
                    item.Framerate = fpsWhole + ((double)fpsFrac / (1 << 16));

                    var optiBlock = pinList.FindByType("opti");
                    if (optiBlock != null)
                    {
                        var optiData = optiBlock.GetBytes();
                        item.FootageType = (FootageType)BinaryPrimitives.ReadUInt16BigEndian(optiData.AsSpan(4));
                        switch (item.FootageType)
                        {
                            case FootageType.Solid:
                                {
                                    var end = Math.Min(255, optiData.Length);
                                    var nameBytes = optiData[26..end];
                                    item.Name = ExtractNullPaddedString(nameBytes);
                                    break;
                                }
                            case FootageType.Placeholder:
                                {
                                    var nameBytes = optiData[10..];
                                    item.Name = ExtractNullPaddedString(nameBytes);
                                    break;
                                }
                        }
                    }

                    // File-backed footage (image / AV / vector / Photoshop): the
                    // source path lives in the alias block, not opti. Solids and
                    // placeholders are synthetic and have no source file.
                    item.SourcePath = ExtractSourcePath(pinList);
                    if (string.IsNullOrEmpty(item.Name) && item.SourcePath is { } sourcePath)
                        item.Name = FileNameFromPath(sourcePath);
                    break;
                }
            case ItemType.Composition:
                {
                    var cdtaBlock = itemHead.FindByType("cdta");
                    if (cdtaBlock == null)
                        throw new InvalidDataException("Missing cdta block in composition item");
                    var cdta = cdtaBlock.GetBytes();
                    var fpsDivisor = BinaryPrimitives.ReadUInt32BigEndian(cdta.AsSpan(4));
                    var fpsDividend = BinaryPrimitives.ReadUInt32BigEndian(cdta.AsSpan(8));
                    item.Framerate = (double)fpsDividend / fpsDivisor;
                    var secDividend = BinaryPrimitives.ReadUInt32BigEndian(cdta.AsSpan(44));
                    var secDivisor = BinaryPrimitives.ReadUInt32BigEndian(cdta.AsSpan(48));
                    item.DurationSeconds = (double)secDividend / secDivisor;
                    item.BackgroundColor = new[] { cdta[52], cdta[53], cdta[54] };
                    item.Width = BinaryPrimitives.ReadUInt16BigEndian(cdta.AsSpan(140));
                    item.Height = BinaryPrimitives.ReadUInt16BigEndian(cdta.AsSpan(142));

                    // Parse layers
                    var layerIndex = 0;
                    foreach (var layerList in itemHead.SublistFilter("Layr"))
                    {
                        layerIndex++;
                        var layer = AepLayer.Parse(layerList, project);
                        layer.Index = (uint)layerIndex;
                        item.CompositionLayers.Add(layer);
                    }
                    break;
                }
        }

        project.Items[item.Id] = item;
        return item;
    }

    /// <summary>
    /// Reads the footage source path from the Pin list's alias. Modern After
    /// Effects stores the alias as a JSON object with a "fullpath" field; older
    /// versions use a binary Mac alias record, which we don't decode (returns null).
    /// </summary>
    private static string? ExtractSourcePath(RifxList pinList)
    {
        var alasBlock = pinList.SublistFind("Als2")?.FindByType("alas");
        return alasBlock == null ? null : ParseAliasPath(alasBlock.GetBytes());
    }

    /// <summary>
    /// Reads the "fullpath" from a modern (JSON) After Effects alias record.
    /// Returns null for the older binary Mac alias format, missing/empty fullpath,
    /// or malformed JSON.
    /// </summary>
    internal static string? ParseAliasPath(byte[] aliasBytes)
    {
        var text = Encoding.UTF8.GetString(aliasBytes);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null; // not the JSON alias format

        try
        {
            using var doc = JsonDocument.Parse(text.Substring(start, end - start + 1));
            if (doc.RootElement.TryGetProperty("fullpath", out var fullPath)
                && fullPath.ValueKind == JsonValueKind.String)
            {
                var path = fullPath.GetString();
                return string.IsNullOrEmpty(path) ? null : path;
            }
        }
        catch (JsonException)
        {
            // malformed alias JSON — treat as no source path
        }
        return null;
    }

    private static string FileNameFromPath(string path)
    {
        var slash = path.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static string ExtractNullPaddedString(byte[] data)
    {
        var sb = new StringBuilder();
        int lastNonNull = -1;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != 0)
                lastNonNull = i;
        }
        if (lastNonNull < 0)
            return "";
        for (int i = 0; i <= lastNonNull; i++)
        {
            sb.Append(data[i] == 0 ? ' ' : (char)data[i]);
        }
        return sb.ToString();
    }
}
