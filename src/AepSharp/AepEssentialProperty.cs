using System.Buffers.Binary;
using System.Text.Json;
using AepSharp.Rifx;

namespace AepSharp;

/// <summary>The control kind of an Essential Graphics property, as the <c>CTyp</c> block codes it.</summary>
public enum EssentialPropertyKind
{
    Unknown = 0,
    Checkbox = 1,
    Slider = 2,
    Color = 4,
    Text = 6,
    Dropdown = 13,
}

/// <summary>One step of the path from a layer to the property an Essential Graphics control drives.</summary>
/// <param name="MatchName">The property or group match name (e.g. <c>ADBE Effect Parade</c>, <c>ADBE Slider Control</c>, <c>ADBE Slider Control-0001</c>).</param>
/// <param name="Index">The zero-based position among the parent's children of that kind, or null when the file stores no index (<c>0xFFFFFFFF</c>).</param>
public sealed record EssentialPropertyPathStep(string MatchName, uint? Index);

/// <summary>
/// A property a composition publishes to its Essential Graphics panel: what the After Effects
/// scripting API calls a master property (<c>layer.essentialProperty</c>), and what a template
/// author exposes so a precomp instance can override it. Read from the composition item's
/// <c>CIF3</c> list (older files: <c>CIF2</c>, <c>CIFO</c>), one <c>CCtl</c> list per control.
/// </summary>
public class AepEssentialProperty
{
    /// <summary>The name shown in the panel and used by <c>essentialProperty.property(name)</c>.</summary>
    public string Name { get; internal set; } = "";

    /// <summary>The control's stable identifier.</summary>
    public string Guid { get; internal set; } = "";

    public EssentialPropertyKind Kind { get; internal set; }

    /// <summary>The raw <c>CTyp</c> code, kept for kinds this library doesn't name.</summary>
    public uint KindCode { get; internal set; }

    /// <summary>The <see cref="AepLayer.Id"/> of the layer inside the composition that holds the driven property.</summary>
    public uint LayerId { get; internal set; }

    /// <summary>
    /// The path from that layer to the driven property. A slider or checkbox reads
    /// <c>ADBE Effect Parade</c> → the effect (by position among the layer's effects) → its
    /// parameter; a text control reads <c>ADBE Text Properties</c> → <c>ADBE Text Document</c>.
    /// </summary>
    public IReadOnlyList<EssentialPropertyPathStep> Path { get; internal set; } = Array.Empty<EssentialPropertyPathStep>();

    /// <summary>For a text control, the font the panel edits (<c>fontEditValue</c>); null otherwise.</summary>
    public string? FontName { get; internal set; }

    /// <summary>
    /// The control's current value as the file stores it: one double for a slider, 0/1 for a
    /// checkbox, a menu index for a dropdown, RGBA for a color. Null for text controls (their
    /// value is the text layer's Source Text) and for kinds whose encoding isn't decoded.
    /// </summary>
    public IReadOnlyList<double>? Value { get; internal set; }

    /// <summary>The panel's default for the control, encoded like <see cref="Value"/>.</summary>
    public IReadOnlyList<double>? Default { get; internal set; }

    /// <summary>
    /// The slider range the panel displays (<c>Smin</c>/<c>Smax</c>). This is the panel's UI
    /// range only: After Effects doesn't clamp the property to it, and a template's own default
    /// can sit outside it. Null for non-slider kinds.
    /// </summary>
    public double? SliderMin { get; internal set; }

    public double? SliderMax { get; internal set; }

    internal static List<AepEssentialProperty> ParseAll(RifxList cifList)
    {
        var result = new List<AepEssentialProperty>();
        foreach (var control in cifList.SublistFilter("CCtl"))
        {
            var property = Parse(control);
            if (property is not null)
                result.Add(property);
        }
        return result;
    }

    private static AepEssentialProperty? Parse(RifxList control)
    {
        var property = new AepEssentialProperty();

        // Name: the first string of the CpS2 (name + locale) list; fall back to the caption.
        var name = control.SublistFind("CpS2")?.FindByType("Utf8")?.ToAsciiString()
            ?? control.SublistFind("CapS")?.FindByType("Utf8")?.ToAsciiString();
        if (string.IsNullOrEmpty(name))
            return null;
        property.Name = name;

        foreach (var block in control.Blocks)
        {
            switch (block.Type)
            {
                case "Utf8":
                    {
                        var text = block.ToAsciiString();
                        if (text.Length == 36 && text[8] == '-')
                            property.Guid = text;
                        break;
                    }
                case "CTyp":
                    property.KindCode = block.ToUInt32();
                    property.Kind = Enum.IsDefined(typeof(EssentialPropertyKind), (int)property.KindCode)
                        ? (EssentialPropertyKind)property.KindCode
                        : EssentialPropertyKind.Unknown;
                    break;
                case "CVal":
                    property.Value = DecodeValue(block.GetBytes());
                    break;
                case "CDef":
                    property.Default = DecodeValue(block.GetBytes());
                    break;
                case "Smin":
                    property.SliderMin = ReadDouble(block.GetBytes());
                    break;
                case "Smax":
                    property.SliderMax = ReadDouble(block.GetBytes());
                    break;
            }
        }

        // The driven property: CPrp holds the source layer id and a JSON path; a text
        // control carries a second JSON with the font the panel edits.
        if (control.SublistFind("CPrp") is { } target)
        {
            if (target.FindByType("CLId") is { } layerId)
                property.LayerId = layerId.ToUInt32();
            foreach (var utf8 in target.Blocks.Where(b => b.Type == "Utf8"))
                ReadJson(property, utf8.ToAsciiString());
        }
        foreach (var utf8 in control.Blocks.Where(b => b.Type == "Utf8"))
            if (utf8.ToAsciiString() is { Length: > 0 } text && text[0] == '{')
                ReadJson(property, text);

        return property;
    }

    private static void ReadJson(AepEssentialProperty property, string text)
    {
        if (text.Length == 0 || text[0] != '{')
            return;
        try
        {
            using var json = JsonDocument.Parse(text);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return;
            if (root.TryGetProperty("fontEditValue", out var font) && font.ValueKind == JsonValueKind.String)
            {
                property.FontName = font.GetString();
                return;
            }
            var steps = new List<(int Order, EssentialPropertyPathStep Step)>();
            foreach (var entry in root.EnumerateObject())
            {
                if (!int.TryParse(entry.Name, out var order) || entry.Value.ValueKind != JsonValueKind.Object)
                    continue;
                var matchName = entry.Value.TryGetProperty("matchName", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                uint? index = entry.Value.TryGetProperty("index", out var i) && i.TryGetUInt32(out var value) && value != uint.MaxValue ? value : null;
                steps.Add((order, new EssentialPropertyPathStep(matchName, index)));
            }
            if (steps.Count > 0)
                property.Path = steps.OrderBy(s => s.Order).Select(s => s.Step).ToList();
        }
        catch (JsonException)
        {
            // Not a path or font record; leave the property as parsed so far.
        }
    }

    // CVal/CDef encode by size: a double for sliders, a u32 for checkboxes and menus, a byte for
    // some checkbox writes, four doubles or four floats for colors.
    private static IReadOnlyList<double>? DecodeValue(byte[] bytes) => bytes.Length switch
    {
        8 => [BinaryPrimitives.ReadDoubleBigEndian(bytes)],
        4 => [BinaryPrimitives.ReadUInt32BigEndian(bytes)],
        1 => [bytes[0]],
        32 => [.. Enumerable.Range(0, 4).Select(i => BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(i * 8)))],
        16 => [.. Enumerable.Range(0, 4).Select(i => (double)BinaryPrimitives.ReadSingleBigEndian(bytes.AsSpan(i * 4)))],
        _ => null,
    };

    private static double? ReadDouble(byte[] bytes) =>
        bytes.Length >= 8 ? BinaryPrimitives.ReadDoubleBigEndian(bytes) : null;
}
