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

    /// <summary>
    /// The layer's on-screen text copy, decoded from the text-document EngineData
    /// blob. Null for non-text layers; "" when the text run is empty. Best-effort:
    /// see <see cref="EngineData"/> for the heuristic's known limits.
    /// </summary>
    public string? SourceText { get; internal set; }

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
            layer.SourceText = ExtractSourceText(layerHead);
        }

        return layer;
    }

    /// <summary>
    /// Decodes the layer's display copy from its text-document EngineData blob.
    /// That blob is the single anomalous (ANON) block in the layer's subtree —
    /// the reader captures it as anomalous because its leading bytes look like a
    /// bogus chunk size. Returns "" if found-but-empty, null if not found.
    /// </summary>
    private static string? ExtractSourceText(RifxList layerHead)
    {
        var block = FindEngineDataBlock(layerHead);
        return block is null ? null : EngineData.ExtractDisplayText(block.GetBytes());
    }

    private static RifxBlock? FindEngineDataBlock(RifxList list)
    {
        foreach (var block in list.Blocks)
        {
            if (block.IsAnomalous && block.Data is byte[] bytes && HasEngineDataSignature(bytes))
                return block;
            if (block.Data is RifxList sub)
            {
                var found = FindEngineDataBlock(sub);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private static bool HasEngineDataSignature(byte[] bytes)
    {
        // The EngineData run strings begin with '(' + FE FF (UTF-16BE BOM).
        for (var i = 0; i + 2 < bytes.Length; i++)
            if (bytes[i] == 0x28 && bytes[i + 1] == 0xFE && bytes[i + 2] == 0xFF)
                return true;
        return false;
    }
}
