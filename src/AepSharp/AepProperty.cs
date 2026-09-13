using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp;

public class AepProperty
{
    public string MatchName { get; internal set; } = "";
    public string Name { get; internal set; } = "";
    public string Label { get; internal set; } = "";
    public uint Index { get; internal set; }
    public PropertyType PropertyType { get; internal set; } = PropertyType.Custom;
    public List<AepProperty> Properties { get; internal set; } = new();
    public List<string> SelectOptions { get; internal set; } = new();

    /// <summary>
    /// The property's static value as raw components (e.g. [x, y, z] for Position,
    /// one element for scalars like Opacity). Values are exposed exactly as the
    /// file stores them: percent-typed properties are fractions (100% = 1.0).
    /// Null for groups, for properties without a static value, and for animated
    /// properties (use <see cref="Keyframes"/> and <see cref="ValueAtTime"/>).
    /// </summary>
    public IReadOnlyList<double>? Value { get; internal set; }

    /// <summary>
    /// Number of keyframes on the property, read from the keyframe list header
    /// (lhd3). Zero for static properties and groups. A property can carry a
    /// single keyframe, which After Effects still treats as animated.
    /// </summary>
    public int KeyframeCount { get; internal set; }

    /// <summary>True when the property has keyframes (its value varies over time).</summary>
    public bool IsAnimated => KeyframeCount > 0;

    /// <summary>Number of value components (e.g. 3 for Position, 1 for Opacity). Zero for groups.</summary>
    public int Dimensions { get; internal set; }

    /// <summary>
    /// True for spatial properties (Position, Anchor Point, effect points), whose
    /// keyframes carry spatial tangents and interpolate along a path.
    /// </summary>
    public bool IsSpatial { get; internal set; }

    /// <summary>
    /// Decoded keyframes in time order. Empty for static properties. Keyframes whose
    /// kind has no numeric value (markers, colours, shapes) have a null
    /// <see cref="AepKeyframe.Value"/>.
    /// </summary>
    public IReadOnlyList<AepKeyframe> Keyframes { get; internal set; } = Array.Empty<AepKeyframe>();

    /// <summary>
    /// The property's expression source, or null if it has none. An enabled
    /// expression overrides the keyframed or static value at render time; this
    /// library does not evaluate expressions.
    /// </summary>
    public string? Expression { get; internal set; }

    /// <summary>True when <see cref="Expression"/> is present and not disabled.</summary>
    public bool ExpressionEnabled { get; internal set; }

    /// <summary>
    /// False when the property or group is switched off (e.g. a shape layer's Fill with its
    /// eye toggled off). Read from bit 0 of byte 3 of the tdsb block, per py-aep's TdsbChunk.
    /// </summary>
    public bool Enabled { get; internal set; } = true;

    /// <summary>
    /// True when a separable property (a layer's Position) is split into X/Y/Z Position
    /// followers ("ADBE Position_0/_1/_2"), which then hold the values. Bit 1 of byte 3 of
    /// the tdsb block, per py-aep's TdsbChunk.
    /// </summary>
    public bool DimensionsSeparated { get; internal set; }

    /// <summary>
    /// For a layer-select effect parameter (e.g. Layer Control): the <see cref="AepLayer.Id"/>
    /// of the referenced layer in the same composition, or null when none is chosen or the
    /// property is not a layer reference. Read from the value's <c>tdpi</c> block.
    /// </summary>
    public uint? LayerReferenceId { get; internal set; }

    /// <summary>
    /// The property's pre-expression value at <paramref name="layerTime"/> seconds of
    /// layer time, interpolated from its keyframes (hold, linear, Bezier with temporal
    /// ease, spatial paths, auto-Bezier). Returns the static <see cref="Value"/> when
    /// the property is not animated, or null when neither is available.
    /// </summary>
    public IReadOnlyList<double>? ValueAtTime(double layerTime) =>
        Keyframes.Count == 0 ? Value : KeyframeInterpolator.Interpolate(layerTime, Keyframes, IsSpatial);

    /// <param name="effectDefinitions">
    /// Project-level effect parameter definitions (the <c>EfdG</c> list), used when an
    /// effect instance omits its own <c>parT</c>. Null outside a project parse.
    /// </param>
    internal static AepProperty ParseFromList(RifxList propHead, string matchName, EffectDefinitions? effectDefinitions = null)
    {
        var prop = new AepProperty
        {
            MatchName = matchName,
            Name = matchName == "ADBE Effect Parade" ? "Effects" : matchName
        };

        if (propHead.FindByType("tdsb")?.GetBytes() is { Length: >= 4 } flags)
        {
            prop.Enabled = (flags[3] & 1) != 0;
            // The bit is only meaningful on a separation leader; py-aep reads it for Position.
            prop.DimensionsSeparated = matchName == "ADBE Position" && (flags[3] & 2) != 0;
        }

        // Parse sub-properties from tdgp groups. Walk the (match name, list) pairs in
        // order rather than through a name-keyed map: an effect parade can hold several
        // instances of the same effect (e.g. three "ADBE Slider Control"s), and each must
        // keep its own list.
        var (subNames, subContents) = PairMatchNames(propHead);
        var subIndex = 0;
        for (int i = 0; i < subNames.Count && i < subContents.Count; i++)
        {
            if (subContents[i].Count == 0 || subContents[i][0] is not RifxList subData)
                continue;
            var subProp = ParseFromList(subData, subNames[i], effectDefinitions);
            subProp.Index = (uint)++subIndex;
            prop.Properties.Add(subProp);
        }

        // A tdbs list is a property's value container: tdb4 describes the value
        // (component count at u16 offset 2), cdat holds the static value as
        // big-endian doubles — the first <components> of them; the rest are
        // reserved slots. Animated properties carry keyframes instead of cdat.
        if (propHead.Identifier == "tdbs")
        {
            prop.Value = DecodeStaticValue(propHead);
            prop.KeyframeCount = DecodeKeyframeCount(propHead);
            DecodeValueMetadata(prop, propHead);
            prop.Keyframes = KeyframeDecoder.Decode(propHead, prop.Dimensions, prop.IsSpatial);
        }

        // Source Text: a btds list wraps the text document's tdbs (metadata and any
        // expression) alongside the btdk EngineData.
        if (propHead.Identifier == "btds" && propHead.SublistFind("tdbs") is { } documentTdbs)
            DecodeValueMetadata(prop, documentTdbs);

        // Handle effect sub-properties (sspc identifier)
        if (propHead.Identifier == "sspc")
        {
            prop.PropertyType = PropertyType.Group;

            // User-defined effect name
            var fnamBlock = propHead.FindByType("fnam");
            if (fnamBlock != null)
                prop.Name = fnamBlock.ToAsciiString();

            // User-defined label from tdsn
            var tdgpBlock = propHead.SublistFind("tdgp");
            if (tdgpBlock != null)
            {
                var tdsnBlock = tdgpBlock.FindByType("tdsn");
                if (tdsnBlock != null)
                {
                    var label = tdsnBlock.ToAsciiString();
                    if (label != "-_0_/-")
                        prop.Label = label;
                }
            }

            // Pseudo effects (Animation Presets, scripted controls) have an empty fnam; the
            // name shown in the Effect Controls panel is then the tdsn label.
            if (string.IsNullOrEmpty(prop.Name) && !string.IsNullOrEmpty(prop.Label))
                prop.Name = prop.Label;

            // Parse parT sub-properties. After Effects writes an empty parT for repeat
            // instances of an effect type; the definitions then come from the project's
            // EfdG list (or an earlier instance), as py-aep resolves them.
            var parTList = propHead.SublistMerge("parT");
            if (effectDefinitions is not null)
                parTList = effectDefinitions.Resolve(matchName, parTList);

            // Current parameter values live in the effect's tdgp as tdbs lists keyed by
            // parameter match name; a parameter left untouched has no tdbs and keeps the
            // last value recorded in its pard.
            var valueLists = tdgpBlock is null
                ? new Dictionary<string, RifxList>()
                : IndexedGroupToMap(tdgpBlock).map;

            var (subMatchNames, subPards) = PairMatchNames(parTList);
            for (int idx = 0; idx < subMatchNames.Count; idx++)
            {
                // Skip first pard entry (describes parent)
                if (idx == 0) continue;
                if (idx >= subPards.Count) break;
                var subProp = ParseFromBlocks(subPards[idx], subMatchNames[idx]);
                subProp.Index = (uint)idx;
                if (valueLists.TryGetValue(subMatchNames[idx], out var valueList) && valueList.Identifier == "tdbs")
                    ApplyStoredValue(subProp, valueList);
                prop.Properties.Add(subProp);
            }
        }

        return prop;
    }

    // Copies an effect parameter's stored value (static cdat or keyframes, plus
    // expression and layer reference) over the pard-derived fallback.
    private static void ApplyStoredValue(AepProperty parameter, RifxList tdbs)
    {
        var stored = ParseFromList(tdbs, parameter.MatchName);
        parameter.Value = stored.Value;
        parameter.KeyframeCount = stored.KeyframeCount;
        parameter.Dimensions = stored.Dimensions;
        parameter.IsSpatial = stored.IsSpatial;
        parameter.Keyframes = stored.Keyframes;
        parameter.Expression = stored.Expression;
        parameter.ExpressionEnabled = stored.ExpressionEnabled;

        if (parameter.PropertyType == PropertyType.LayerSelect)
        {
            // The layer id is the reference; cdat holds only zeros.
            parameter.Value = null;
            var tdpi = tdbs.FindByType("tdpi")?.GetBytes();
            if (tdpi is { Length: >= 4 } && BinaryPrimitives.ReadUInt32BigEndian(tdpi) is var layerId and not 0)
                parameter.LayerReferenceId = layerId;
        }
    }

    private static IReadOnlyList<double>? DecodeStaticValue(RifxList tdbs)
    {
        var tdb4 = tdbs.FindByType("tdb4")?.GetBytes();
        var cdat = tdbs.FindByType("cdat")?.GetBytes();
        if (tdb4 is null || tdb4.Length < 4 || cdat is null)
            return null;

        var components = BinaryPrimitives.ReadUInt16BigEndian(tdb4.AsSpan(2));
        // Clamp to the doubles actually present — a truncated cdat must not throw.
        var available = cdat.Length / 8;
        var count = Math.Min(components, available);
        if (count == 0)
            return null;

        var value = new double[count];
        for (var i = 0; i < count; i++)
            value[i] = BinaryPrimitives.ReadDoubleBigEndian(cdat.AsSpan(i * 8));
        return value;
    }

    // Animated properties carry a "list" LIST whose lhd3 header holds the keyframe
    // count as a big-endian u16 at offset 10, after a fixed prefix (00 D0 0B EE and
    // zeros), and each keyframe's byte size as a u16 at offset 18. Count × size equals
    // the ldat payload length for every animated property in a 181-file production
    // corpus. Layout per py-aep (MIT), which writes files After Effects accepts.
    private static int DecodeKeyframeCount(RifxList tdbs)
    {
        var lhd3 = tdbs.SublistFind("list")?.FindByType("lhd3")?.GetBytes();
        if (lhd3 is null || lhd3.Length < 12)
            return 0;
        return BinaryPrimitives.ReadUInt16BigEndian(lhd3.AsSpan(10));
    }

    // tdb4 (property metadata): dimensions u16 at 2; spatial flag bit 3 of byte 5;
    // expression-disabled bit 0 of byte 119. The expression source itself is the
    // tdbs list's Utf8 block. Offsets per py-aep's Tdb4Chunk.
    private static void DecodeValueMetadata(AepProperty prop, RifxList tdbs)
    {
        var tdb4 = tdbs.FindByType("tdb4")?.GetBytes();
        if (tdb4 is { Length: >= 6 })
        {
            prop.Dimensions = BinaryPrimitives.ReadUInt16BigEndian(tdb4.AsSpan(2));
            prop.IsSpatial = (tdb4[5] & (1 << 3)) != 0;
        }

        var utf8 = tdbs.FindByType("Utf8");
        if (utf8 is not null)
        {
            var expression = utf8.ToAsciiString();
            if (!string.IsNullOrWhiteSpace(expression))
            {
                prop.Expression = expression;
                prop.ExpressionEnabled = tdb4 is not { Length: >= 120 } || (tdb4[119] & 1) == 0;
            }
        }
    }

    internal static AepProperty ParseFromBlocks(List<object> entries, string matchName)
    {
        var prop = new AepProperty
        {
            MatchName = matchName,
            Name = matchName
        };

        foreach (var entry in entries)
        {
            if (entry is RifxBlock block)
            {
                switch (block.Type)
                {
                    case "pdnm":
                        {
                            var strContent = block.ToAsciiString();
                            if (prop.PropertyType == PropertyType.Select)
                            {
                                prop.SelectOptions = new List<string>(strContent.Split('|'));
                            }
                            else if (!string.IsNullOrEmpty(strContent))
                            {
                                prop.Name = strContent;
                            }
                            break;
                        }
                    case "pard":
                        {
                            var data = block.GetBytes();
                            var typeValue = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(14));
                            prop.PropertyType = typeValue == 0x0a
                                ? PropertyType.OneD
                                : (PropertyType)typeValue;

                            // NUL-terminated: the rest of the 32-byte field can hold stale
                            // bytes ("Opacity\0olor").
                            var nameField = data.AsSpan(16, 32);
                            var nameEnd = nameField.IndexOf((byte)0);
                            var pardName = Encoding.UTF8.GetString(nameEnd < 0 ? nameField : nameField[..nameEnd]);
                            if (!string.IsNullOrEmpty(pardName))
                                prop.Name = pardName;
                            prop.Value = DecodePardValue(data);
                            prop.Dimensions = prop.Value?.Count ?? 0;
                            break;
                        }
                }
            }
        }

        return prop;
    }

    // The pard block is a serialized AE SDK PF_ParamDef: a 56-byte header (control type
    // u8 at 15, name at 16..47) followed by a type-specific body whose first field is the
    // parameter's last value. Layouts per py-aep's PardChunk variants (MIT). Values are
    // returned in the units the parameter's cdat uses, so a parameter reads the same
    // whether or not After Effects wrote a tdbs for it:
    //   1 integer slider   s32 at 56
    //   2 fixed slider     s32 16.16 fixed point at 56
    //   3 angle            s32 16.16 fixed point at 56 (degrees)
    //   4 checkbox         u8 default at 60 (0/1); py-aep prefers it over the u32 last
    //                      value at 56, matching what After Effects scripting reports
    //   5 colour           u8 A,R,G,B at 56..59 (cdat stores ARGB 0-255)
    //   6 2D point         s32 x,y at 56/60 in 1/128ths of a 0-512 range; cdat stores
    //                      fractions of the layer size, so both are divided by 512
    //   7 popup            u32 at 56 (1-based option index)
    //  10 float slider     f64 at 56
    // Other control types (layer, mask/path, group, 3D point, curves...) have no pard value.
    internal static IReadOnlyList<double>? DecodePardValue(byte[] data)
    {
        if (data.Length < 16)
            return null;
        var body = data.AsSpan(56 <= data.Length ? 56 : data.Length);
        return data[15] switch
        {
            1 when body.Length >= 4 => [BinaryPrimitives.ReadInt32BigEndian(body)],
            2 or 3 when body.Length >= 4 => [BinaryPrimitives.ReadInt32BigEndian(body) / 65536.0],
            4 when body.Length >= 5 => [body[4]],
            4 when body.Length >= 4 => [BinaryPrimitives.ReadUInt32BigEndian(body)],
            7 when body.Length >= 4 => [BinaryPrimitives.ReadUInt32BigEndian(body)],
            5 when body.Length >= 4 => [body[0], body[1], body[2], body[3]],
            6 when body.Length >= 8 =>
            [
                BinaryPrimitives.ReadInt32BigEndian(body) / 128.0 / 512.0,
                BinaryPrimitives.ReadInt32BigEndian(body[4..]) / 128.0 / 512.0,
            ],
            10 when body.Length >= 8 => [BinaryPrimitives.ReadDoubleBigEndian(body)],
            _ => null,
        };
    }

    internal static (List<string> matchNames, List<List<object>> data) PairMatchNames(RifxList head)
    {
        var matchNames = new List<string>();
        var datum = new List<List<object>>();

        if (head == null) return (matchNames, datum);

        int groupIdx = -1;
        bool skip = false;

        foreach (var block in head.Blocks)
        {
            if (block.Type == "tdmn")
            {
                var mn = Encoding.UTF8.GetString(((byte[])block.Data)).TrimEnd('\0');
                if (mn is "ADBE Group End" or "ADBE Effect Built In Params")
                {
                    skip = true;
                    continue;
                }
                matchNames.Add(mn);
                skip = false;
                groupIdx++;
            }
            else if (groupIdx >= 0 && !skip)
            {
                while (datum.Count <= groupIdx)
                    datum.Add(new List<object>());

                if (block.Data is RifxList list)
                    datum[groupIdx].Add(list);
                else
                    datum[groupIdx].Add(block);
            }
        }

        return (matchNames, datum);
    }

    internal static (Dictionary<string, RifxList> map, List<string> orderedNames) IndexedGroupToMap(RifxList tdgpHead)
    {
        var map = new Dictionary<string, RifxList>();
        var (matchNames, contents) = PairMatchNames(tdgpHead);
        var orderedNames = new List<string>();

        for (int i = 0; i < matchNames.Count; i++)
        {
            if (i < contents.Count && contents[i].Count > 0 && contents[i][0] is RifxList list)
            {
                map[matchNames[i]] = list;
                orderedNames.Add(matchNames[i]);
            }
        }

        return (map, orderedNames);
    }
}
