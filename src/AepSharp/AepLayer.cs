using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp;

public class AepLayer
{
    public uint Index { get; internal set; }
    public string Name { get; internal set; } = "";
    public uint SourceId { get; internal set; }
    public LayerQuality Quality { get; internal set; }
    public LayerSamplingMode SamplingMode { get; internal set; }
    public LayerFrameBlendMode FrameBlendMode { get; internal set; }
    public bool GuideEnabled { get; internal set; }
    public bool SoloEnabled { get; internal set; }
    public bool ThreeDEnabled { get; internal set; }
    public bool AdjustmentLayerEnabled { get; internal set; }
    public bool CollapseTransformEnabled { get; internal set; }
    public bool ShyEnabled { get; internal set; }
    public bool LockEnabled { get; internal set; }
    public bool FrameBlendEnabled { get; internal set; }
    public bool MotionBlurEnabled { get; internal set; }
    public bool EffectsEnabled { get; internal set; }
    public bool AudioEnabled { get; internal set; }
    public bool VideoEnabled { get; internal set; }
    public List<AepProperty> Effects { get; internal set; } = new();
    public AepProperty? Text { get; internal set; }

    internal static AepLayer Parse(RifxList layerHead, AepProject project)
    {
        var layer = new AepLayer();

        // ldta block — quality at offset 4, bit flags at offset 37-39, source ID at offset 40
        var ldtaBlock = layerHead.FindByType("ldta");
        if (ldtaBlock == null)
            throw new InvalidDataException("Missing ldta block in layer");
        var ldta = ldtaBlock.GetBytes();

        layer.Quality = (LayerQuality)BinaryPrimitives.ReadUInt16BigEndian(ldta.AsSpan(4));
        layer.SourceId = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(40));

        // Bit flags from bytes at offsets 37, 38, 39
        byte bits0 = ldta[37];
        byte bits1 = ldta[38];
        byte bits2 = ldta[39];

        layer.SamplingMode = (LayerSamplingMode)((bits0 & (1 << 6)) >> 6);
        layer.FrameBlendMode = (LayerFrameBlendMode)((bits0 & (1 << 2)) >> 2);
        layer.GuideEnabled = ((bits0 & (1 << 1)) >> 1) == 1;
        layer.SoloEnabled = ((bits1 & (1 << 3)) >> 3) == 1;
        layer.ThreeDEnabled = ((bits1 & (1 << 2)) >> 2) == 1;
        layer.AdjustmentLayerEnabled = ((bits1 & (1 << 1)) >> 1) == 1;
        layer.CollapseTransformEnabled = ((bits2 & (1 << 7)) >> 7) == 1;
        layer.ShyEnabled = ((bits2 & (1 << 6)) >> 6) == 1;
        layer.LockEnabled = ((bits2 & (1 << 5)) >> 5) == 1;
        layer.FrameBlendEnabled = ((bits2 & (1 << 4)) >> 4) == 1;
        layer.MotionBlurEnabled = ((bits2 & (1 << 3)) >> 3) == 1;
        layer.EffectsEnabled = ((bits2 & (1 << 2)) >> 2) == 1;
        layer.AudioEnabled = ((bits2 & (1 << 1)) >> 1) == 1;
        layer.VideoEnabled = (bits2 & 1) == 1;

        // Layer name
        var nameBlock = layerHead.FindByType("Utf8");
        if (nameBlock != null)
            layer.Name = nameBlock.ToAsciiString();

        // Parse properties via tdgp groups
        var (rootTDGP, _) = AepProperty.IndexedGroupToMap(layerHead.SublistMerge("tdgp"));

        // Effects
        if (rootTDGP.TryGetValue("ADBE Effect Parade", out var effectsTDGP))
        {
            var effectsProp = AepProperty.ParseFromList(effectsTDGP, "ADBE Effect Parade");
            layer.Effects = effectsProp.Properties;
        }

        // Text
        if (rootTDGP.TryGetValue("ADBE Text Properties", out var textTDGP))
        {
            layer.Text = AepProperty.ParseFromList(textTDGP, "ADBE Text Properties");
        }

        return layer;
    }
}
