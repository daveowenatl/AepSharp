using System.Buffers.Binary;
using AepSharp.EngineModel;
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
    /// blob. Null for non-text layers; "" when the text run is empty. Read
    /// structurally (all runs concatenated) with a heuristic fallback.
    /// </summary>
    public string? SourceText { get; internal set; }

    /// <summary>
    /// PostScript names of the fonts the text layer's document references, in order
    /// (e.g. "Heebo-ExtraBold"). Empty for non-text layers or when the EngineData
    /// could not be parsed structurally. Useful for knowing which fonts a template
    /// requires before rendering.
    /// </summary>
    public IReadOnlyList<string> Fonts { get; internal set; } = Array.Empty<string>();

    /// <summary>
    /// The text layer's copy split into styled runs (per-character StyleRun), each
    /// carrying its own font, size, and fill/stroke colour. One run for uniform text;
    /// several when the copy mixes styles. Empty for non-text layers or when the
    /// EngineData could not be parsed structurally.
    /// </summary>
    public IReadOnlyList<AepTextRun> TextRuns { get; internal set; } = Array.Empty<AepTextRun>();

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
            PopulateTextContent(layer, layerHead);
        }

        return layer;
    }

    /// <summary>
    /// Decodes the layer's text-document EngineData blob into <see cref="SourceText"/>,
    /// <see cref="Fonts"/>, and <see cref="TextRuns"/>. The blob is located via the
    /// "btdk" list's opaque payload (with an ANON-block fallback). Prefers the
    /// structural parse; falls back to the heuristic for text-only when a blob won't
    /// parse.
    /// </summary>
    private static void PopulateTextContent(AepLayer layer, RifxList layerHead)
    {
        var bytes = FindEngineDataBytes(layerHead);
        if (bytes is null)
            return;
        try
        {
            var document = EngineTextExtractor.Extract(EngineDataParser.Parse(bytes));
            if (document is not null)
            {
                layer.SourceText = document.Text;
                layer.Fonts = document.Fonts;
                layer.TextRuns = BuildTextRuns(document);
                return;
            }
        }
        catch (InvalidDataException)
        {
            // malformed EngineData — fall through to the tolerant heuristic
        }

        layer.SourceText = EngineData.ExtractDisplayText(bytes);
    }

    private static List<AepTextRun> BuildTextRuns(EngineModel.EngineTextDocument document)
    {
        var runs = new List<AepTextRun>(document.StyledRuns.Count);
        foreach (var run in document.StyledRuns)
        {
            string? fontName = null;
            if (run.FontIndex is { } i && i >= 0 && i < document.Fonts.Count)
                fontName = document.Fonts[i];

            runs.Add(new AepTextRun
            {
                Text = run.Text,
                FontName = fontName,
                FontSize = run.FontSize,
                FillColor = run.Fill,
                StrokeColor = run.Stroke,
            });
        }
        return runs;
    }

    /// <summary>
    /// Locates the layer's text-document EngineData. Primary signal: the "btdk"
    /// list, whose raw payload the reader captures opaquely — a stable format
    /// anchor. Fallback: an anomalous (ANON) block carrying the EngineData string
    /// signature, for variants where the blob sits outside a btdk list.
    /// </summary>
    private static byte[]? FindEngineDataBytes(RifxList layerHead)
    {
        return FindBtdkPayload(layerHead) ?? FindAnomalousEngineData(layerHead);
    }

    private static byte[]? FindBtdkPayload(RifxList list)
    {
        if (list.Identifier == "btdk" && list.RawPayload is { } payload)
            return payload;
        foreach (var block in list.Blocks)
        {
            if (block.Data is RifxList sub)
            {
                var found = FindBtdkPayload(sub);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    private static byte[]? FindAnomalousEngineData(RifxList list)
    {
        foreach (var block in list.Blocks)
        {
            if (block.IsAnomalous && block.Data is byte[] bytes && HasEngineDataSignature(bytes))
                return bytes;
            if (block.Data is RifxList sub)
            {
                var found = FindAnomalousEngineData(sub);
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
