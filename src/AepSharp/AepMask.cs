using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp;

/// <summary>How a mask combines with the masks before it, as the <c>mkif</c> block codes it (values observed so far).</summary>
public enum AepMaskMode
{
    Unknown = 0,
    Add = 1,
}

/// <summary>
/// One mask of a layer: an <c>ADBE Mask Atom</c> of the layer's <c>ADBE Mask Parade</c>, with its
/// shape (<c>ADBE Mask Shape</c>), feather, opacity and expansion properties.
/// </summary>
public sealed class AepMask
{
    public string Name { get; internal set; } = "";

    public AepMaskMode Mode { get; internal set; }

    /// <summary>The raw mode code from <c>mkif</c>, kept for modes this library doesn't name.</summary>
    public uint ModeCode { get; internal set; }

    /// <summary>The mask's path; null when the shape property holds no static path.</summary>
    public AepPath? Path { get; internal set; }

    /// <summary>The shape property itself (its expression and keyframe count, when any).</summary>
    public AepProperty? Shape { get; internal set; }

    /// <summary>Feather in pixels, horizontal and vertical.</summary>
    public IReadOnlyList<double> Feather { get; internal set; } = [0, 0];

    /// <summary>Mask opacity as a fraction (1 = 100 %).</summary>
    public double Opacity { get; internal set; } = 1;

    /// <summary>Mask expansion in pixels (negative contracts).</summary>
    public double Expansion { get; internal set; }

    // The parade's tdgp holds, per mask: tdmn "ADBE Mask Atom", an mkif block, then the atom's
    // own tdgp with the shape, feather, opacity and expansion properties.
    internal static List<AepMask> ParseParade(RifxList paradeTdgp)
    {
        var masks = new List<AepMask>();
        AepMask? current = null;
        foreach (var block in paradeTdgp.Blocks)
        {
            if (block.Type == "tdmn" && block.ToAsciiString() == "ADBE Mask Atom")
            {
                current = new AepMask();
                masks.Add(current);
            }
            else if (current is not null && block.Type == "mkif")
            {
                var mkif = block.GetBytes();
                if (mkif.Length >= 8)
                {
                    current.ModeCode = BinaryPrimitives.ReadUInt32BigEndian(mkif.AsSpan(4));
                    current.Mode = current.ModeCode == 1 ? AepMaskMode.Add : AepMaskMode.Unknown;
                }
            }
            else if (current is not null && block.Type == "LIST" && block.Data is RifxList { Identifier: "tdgp" } atom)
            {
                var name = atom.FindByType("tdsn")?.ToAsciiString();
                if (!string.IsNullOrEmpty(name) && name != "-_0_/-")
                    current.Name = name;
                var group = AepProperty.ParseFromList(atom, "ADBE Mask Atom");
                foreach (var property in group.Properties)
                {
                    switch (property.MatchName)
                    {
                        case "ADBE Mask Shape":
                            current.Shape = property;
                            current.Path = property.Path;
                            break;
                        case "ADBE Mask Feather" when property.Value is { Count: >= 2 } feather:
                            current.Feather = [feather[0], feather[1]];
                            break;
                        case "ADBE Mask Opacity" when property.Value is { Count: >= 1 } opacity:
                            current.Opacity = opacity[0];
                            break;
                        case "ADBE Mask Offset" when property.Value is { Count: >= 1 } expansion:
                            current.Expansion = expansion[0];
                            break;
                    }
                }
                current = null;
            }
        }
        return masks;
    }
}
