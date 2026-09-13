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

    /// <summary>
    /// Full path of a file-backed footage item's source as After Effects last saw it
    /// (e.g. <c>/Users/x/(Footage)/CTA.png</c>, or a Windows path for projects saved on
    /// Windows); for an image sequence, the containing folder. Read from the <c>alas</c>
    /// JSON record in the item's <c>Pin</c> / <c>Als2</c> list. Null for solids,
    /// placeholders, compositions and folders, and when the record is missing or
    /// not JSON (older binary aliases).
    /// </summary>
    public string? SourcePath { get; internal set; }
    public byte[] BackgroundColor { get; internal set; } = new byte[3];
    public List<AepLayer> CompositionLayers { get; internal set; } = new();

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

                    item.SourcePath = ReadSourcePath(pinList);

                    var optiBlock = pinList.FindByType("opti");
                    if (optiBlock != null)
                    {
                        var optiData = optiBlock.GetBytes();
                        item.FootageType = (FootageType)BinaryPrimitives.ReadUInt16BigEndian(optiData.AsSpan(4));
                        switch (item.FootageType)
                        {
                            case FootageType.Solid:
                                {
                                    // NUL-terminated: bytes after the terminator can be stale
                                    // leftovers of an earlier, longer name.
                                    var end = Math.Min(26 + 256, optiData.Length);
                                    item.Name = end > 26 ? NullTerminatedUtf8(optiData.AsSpan(26, end - 26)) : "";
                                    break;
                                }
                            case FootageType.Placeholder:
                                {
                                    item.Name = optiData.Length > 10 ? NullTerminatedUtf8(optiData.AsSpan(10)) : "";
                                    break;
                                }
                        }
                    }

                    // After Effects leaves the item name empty for file footage that was
                    // never renamed and shows the source file name instead — prefixed with
                    // the source layer for a single layer of a layered Illustrator/PDF file.
                    if (string.IsNullOrEmpty(item.Name) && item.SourcePath is { Length: > 0 } path)
                    {
                        var sourceLayer = optiBlock is not null && item.FootageType == FootageType.Vector
                            ? VectorLayerName(optiBlock.GetBytes())
                            : "";
                        item.Name = sourceLayer.Length > 0 ? $"{sourceLayer}/{FileNameOf(path)}" : FileNameOf(path);
                    }
                    break;
                }
            case ItemType.Composition:
                {
                    var cdtaBlock = itemHead.FindByType("cdta");
                    if (cdtaBlock == null)
                        throw new InvalidDataException("Missing cdta block in composition item");
                    var cdta = cdtaBlock.GetBytes();
                    var fpsDivisor = BinaryPrimitives.ReadUInt32BigEndian(cdta.AsSpan(4));
                    // Internal timebase (frame rate × 256 × time scale): also the units-per-second
                    // for keyframe and layer times in this composition.
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
                        var layer = AepLayer.Parse(layerList, project, fpsDividend);
                        layer.Index = (uint)layerIndex;
                        item.CompositionLayers.Add(layer);
                    }
                    break;
                }
        }

        project.Items[item.Id] = item;
        return item;
    }

    // The alas payload is a UTF-8 JSON record in current After Effects versions, e.g.
    // {"ascendcount_base":2,"ascendcount_target":3,"fullpath":"/Users/x/CTA.png",
    //  "platform":2,"target_is_folder":false}. Layout per py-aep (MIT).
    private static string? ReadSourcePath(RifxList pinList)
    {
        var alas = pinList.SublistFind("Als2")?.FindByType("alas");
        if (alas?.Data is not byte[] bytes || bytes.Length == 0)
            return null;

        var span = bytes.AsSpan();
        var end = span.IndexOf((byte)0);
        if (end >= 0)
            span = span[..end];
        if (span.IsEmpty || span[0] != (byte)'{')
            return null;

        try
        {
            using var json = JsonDocument.Parse(span.ToArray());
            return json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("fullpath", out var fullPath)
                && fullPath.ValueKind == JsonValueKind.String
                ? fullPath.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Vector ("TEXT") opti: the selected source layer name is a NUL-padded UTF-8 field of
    // 256 bytes at offset 0x44; empty for whole-document footage. Per py-aep's TextOptiChunk.
    private static string VectorLayerName(byte[] opti) =>
        opti.Length >= 0x44 + 256 ? NullTerminatedUtf8(opti.AsSpan(0x44, 256)) : "";

    private static string NullTerminatedUtf8(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.UTF8.GetString(end < 0 ? bytes : bytes[..end]);
    }

    // Splits on both separators: a project saved on Windows keeps backslash paths.
    private static string FileNameOf(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        var slash = trimmed.LastIndexOfAny(['/', '\\']);
        return slash < 0 ? trimmed : trimmed[(slash + 1)..];
    }
}
