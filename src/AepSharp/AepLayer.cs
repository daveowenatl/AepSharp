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
    /// <summary>The layer's kind (text, shape, camera, light, …); null when the ldta block is too short to hold it.</summary>
    public LayerType? LayerType { get; internal set; }
    /// <summary>True for null object layers (AV layers with no rendered content).</summary>
    public bool NullLayer { get; internal set; }
    /// <summary>Number of masks on the layer.</summary>
    public int MaskCount { get; internal set; }
    /// <summary>Number of text animators on a text layer.</summary>
    public int TextAnimatorCount { get; internal set; }
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
    /// <summary>A shape layer's contents (the "ADBE Root Vectors Group" tree); null for other layers.</summary>
    public AepProperty? Contents { get; internal set; }

    /// <summary>
    /// When the layer starts in its parent composition, in seconds. Layer time zero
    /// maps to this composition time.
    /// </summary>
    public double StartTime { get; internal set; }

    /// <summary>
    /// The layer's in point in layer time (seconds after <see cref="StartTime"/>),
    /// as stored in the file. Add <see cref="StartTime"/> for composition time.
    /// </summary>
    public double InPoint { get; internal set; }

    /// <summary>
    /// The layer's out point in layer time (seconds after <see cref="StartTime"/>),
    /// as stored in the file. Add <see cref="StartTime"/> for composition time. May
    /// extend past the end of the composition or the layer source.
    /// </summary>
    public double OutPoint { get; internal set; }

    /// <summary>
    /// Time stretch as a factor (1.0 = 100%, 2.0 = 200% slower, negative = time-reversed),
    /// from the (s32 dividend at ldta byte 8, u32 divisor at byte 108) pair. A zero or
    /// missing stretch reads as 1.0, as After Effects treats it.
    /// </summary>
    public double Stretch { get; internal set; } = 1.0;

    /// <summary>
    /// Composition time the layer becomes visible: <c>StartTime + InPoint × Stretch</c>
    /// (the stored in/out points are in unstretched layer time), clamped as After Effects
    /// reports it — see <see cref="CompositionOutPoint"/>. For a time-reversed layer
    /// (negative stretch) the stretched in and out swap, so this is always the earlier of
    /// the two; py-aep reports them unswapped.
    /// </summary>
    public double CompositionInPoint => Math.Min(StretchedInPoint, StretchedOutPoint);

    /// <summary>
    /// Composition time the layer stops being visible: <c>StartTime + OutPoint × Stretch</c>.
    /// For a layer whose source is time-based (video, audio or a composition) with no
    /// time remapping and a positive stretch, After Effects clamps the in point to
    /// <see cref="StartTime"/> and the out point to <c>StartTime + source duration × Stretch</c>;
    /// the same clamp applies here when the layer was parsed as part of a project. Stills,
    /// solids and sourceless layers are not clamped.
    /// </summary>
    public double CompositionOutPoint => Math.Max(StretchedInPoint, StretchedOutPoint);

    /// <summary>
    /// The layer's out point as stored, before the clamp to its source's duration:
    /// <c>StartTime + OutPoint × Stretch</c> (the later of the stretched in and out for a
    /// time-reversed layer). After Effects re-applies the clamp against whatever the source
    /// is, so when footage is replaced with a longer file the layer plays up to this point.
    /// </summary>
    public double UnclampedCompositionOutPoint => Math.Max(StartTime + InPoint * Stretch, StartTime + OutPoint * Stretch);

    /// <summary>
    /// True when the layer has time remapping enabled (an animated "ADBE Time Remapping"
    /// property). The source is then not clamped to its duration.
    /// </summary>
    public bool TimeRemapEnabled { get; internal set; }

    // Duration of a time-based source, set by the project after all items are parsed;
    // null when After Effects would not clamp the layer's in/out to its source.
    internal double? ClampSourceDuration { get; set; }

    /// <summary>
    /// After Effects clamps a layer's in/out to a time-based source (footage with a
    /// duration, or a composition) unless time remapping is on or the layer is
    /// time-reversed. Stills and solids have zero duration. Rule per py-aep's
    /// AVLayer._should_clamp_times (MIT).
    /// </summary>
    internal static double? SourceClampDuration(AepLayer layer, AepItem source) =>
        source.ItemType is ItemType.Footage or ItemType.Composition
        && source.DurationSeconds > 0
        && !layer.TimeRemapEnabled
        && layer.Stretch >= 0
            ? source.DurationSeconds
            : null;

    private double StretchedInPoint => ClampSourceDuration is null
        ? StartTime + InPoint * Stretch
        : Math.Max(StartTime + InPoint * Stretch, StartTime);

    private double StretchedOutPoint => ClampSourceDuration is { } duration
        ? Math.Min(StartTime + OutPoint * Stretch, StartTime + duration * Stretch)
        : StartTime + OutPoint * Stretch;

    /// <summary>The layer's id, unique within the project (ldta u32 at offset 0).</summary>
    public uint Id { get; internal set; }

    /// <summary>
    /// Id (<see cref="Id"/>) of the layer's parent in the same composition, or null when
    /// the layer has no parent (ldta u32 at offset 132).
    /// </summary>
    public uint? ParentLayerId { get; internal set; }

    /// <summary>Blending mode. <see cref="BlendingMode.Normal"/> for layers without one (cameras, lights).</summary>
    public BlendingMode BlendingMode { get; internal set; }

    /// <summary>How this layer uses a track matte; <see cref="TrackMatteType.None"/> when it has none.</summary>
    public TrackMatteType TrackMatte { get; internal set; }

    /// <summary>
    /// Id of the layer used as this layer's track matte (After Effects 2023+, ldta u32 at
    /// offset 160). Null when the layer has no matte layer or the file predates explicit
    /// matte layers — there the matte is the layer directly above.
    /// </summary>
    public uint? TrackMatteLayerId { get; internal set; }

    /// <summary>True when "Preserve Underlying Transparency" is on.</summary>
    public bool PreserveTransparency { get; internal set; }

    /// <summary>
    /// The layer's Transform group ("ADBE Transform Group"), holding anchor point,
    /// position, scale, rotation and opacity. Null if the layer has none.
    /// </summary>
    public AepProperty? Transform { get; internal set; }

    // After Effects only stores a transform value that differs from its default, so
    // the accessors below return null for "default or animated". Defaults: anchor =
    // source centre (0,0 for text), position = composition centre, scale = 100%,
    // rotation = 0, opacity = 100%.

    /// <summary>Static anchor point [x, y, z] in layer pixels; null if animated or left at its default.</summary>
    public IReadOnlyList<double>? AnchorPoint => TransformValue("ADBE Anchor Point");

    /// <summary>
    /// Static position [x, y, z] in composition pixels; null if animated, left at its default,
    /// or separated into X/Y/Z Position (see <see cref="PositionDimensionsSeparated"/>).
    /// </summary>
    public IReadOnlyList<double>? Position => PositionDimensionsSeparated ? null : TransformValue("ADBE Position");

    /// <summary>True when Position is split into X Position, Y Position (and Z Position) properties.</summary>
    public bool PositionDimensionsSeparated => FindTransformProperty("ADBE Position")?.DimensionsSeparated ?? false;

    /// <summary>
    /// Static X, Y and Z Position of a layer whose position dimensions are separated, in
    /// composition pixels; each null if animated or left at its default (the composition
    /// centre for X and Y, 0 for Z).
    /// </summary>
    public (double? X, double? Y, double? Z) SeparatedPosition => (
        TransformValue("ADBE Position_0") is [var x, ..] ? x : null,
        TransformValue("ADBE Position_1") is [var y, ..] ? y : null,
        TransformValue("ADBE Position_2") is [var z, ..] ? z : null);

    /// <summary>
    /// Static scale as fractions [x, y, z] (100% = 1.0); null if animated or left at its default.
    /// For 2D layers the z component is not meaningful.
    /// </summary>
    public IReadOnlyList<double>? Scale => TransformValue("ADBE Scale");

    /// <summary>Static 2D rotation in degrees; null if animated or left at its default.</summary>
    public double? Rotation => TransformValue("ADBE Rotate Z") is [var degrees, ..] ? degrees : null;

    /// <summary>Static opacity as a fraction (100% = 1.0); null if animated or left at its default.</summary>
    public double? Opacity => TransformValue("ADBE Opacity") is [var opacity, ..] ? opacity : null;

    /// <summary>
    /// A layer with a source (footage, solid or composition) stores its anchor point as a
    /// fraction of the source's size; source-less layers (text, shape, null) store pixels.
    /// Scales the stored value, keyframe values and spatial tangents to layer pixels, as
    /// py-aep (MIT) resolves them. Ease speeds are already in pixels.
    /// </summary>
    internal void DenormalizeAnchorPoint(double width, double height)
    {
        if (FindTransformProperty("ADBE Anchor Point") is not { } anchor || width <= 0 || height <= 0)
            return;
        IReadOnlyList<double>? Scaled(IReadOnlyList<double>? v) =>
            v?.Select((c, i) => i == 0 ? c * width : i == 1 ? c * height : c).ToArray();
        anchor.Value = Scaled(anchor.Value);
        foreach (var keyframe in anchor.Keyframes)
        {
            keyframe.Value = Scaled(keyframe.Value);
            keyframe.InSpatialTangent = Scaled(keyframe.InSpatialTangent);
            keyframe.OutSpatialTangent = Scaled(keyframe.OutSpatialTangent);
        }
    }

    /// <summary>Finds a property in the Transform group by match name.</summary>
    public AepProperty? FindTransformProperty(string matchName) =>
        Transform?.Properties.FirstOrDefault(p => p.MatchName == matchName);

    private IReadOnlyList<double>? TransformValue(string matchName) => FindTransformProperty(matchName)?.Value;

    /// <summary>
    /// A Transform property's pre-expression value at <paramref name="compositionTime"/>
    /// seconds of composition time, interpolating keyframes when the property is
    /// animated. Null when the property is absent or left at its default.
    /// </summary>
    public IReadOnlyList<double>? TransformValueAt(string matchName, double compositionTime) =>
        FindTransformProperty(matchName)?.ValueAtTime(compositionTime - StartTime);

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

    /// <summary>
    /// Paragraph justification of the text layer's first paragraph, or null for non-text
    /// layers and documents that don't store one.
    /// </summary>
    public TextJustification? TextJustification { get; internal set; }

    /// <summary>True for box (paragraph) text; false for point text and non-text layers.</summary>
    public bool IsBoxText { get; internal set; }

    /// <summary>The text box [width, height] in layer pixels; null for point text and non-text layers.</summary>
    public IReadOnlyList<double>? TextBoxSize { get; internal set; }

    /// <summary>
    /// The text box's top-left corner [x, y] in layer pixels, relative to the layer's
    /// origin (the anchor of a text layer); null for point text and non-text layers.
    /// </summary>
    public IReadOnlyList<double>? TextBoxPosition { get; internal set; }

    /// <param name="timeBase">
    /// The containing composition's internal timebase (units per second, cdta offset 8),
    /// used to convert keyframe times. Zero leaves keyframe times unresolved (NaN).
    /// </param>
    internal static AepLayer Parse(RifxList layerHead, AepProject project, uint timeBase = 0)
    {
        var layer = new AepLayer();

        // ldta block — quality at offset 4, bit flags at offset 37-39, source ID at offset 40
        var ldtaBlock = layerHead.FindByType("ldta");
        if (ldtaBlock == null)
            throw new InvalidDataException("Missing ldta block in layer");
        var ldta = ldtaBlock.GetBytes();

        layer.Id = BinaryPrimitives.ReadUInt32BigEndian(ldta);
        layer.Quality = (LayerQuality)BinaryPrimitives.ReadUInt16BigEndian(ldta.AsSpan(4));
        ReadCompositingFields(layer, ldta);

        // Times are (signed dividend, unsigned divisor) pairs: start at 12, in at 20,
        // out at 28. The divisor is the time base (frame rate × 1000 for comps).
        layer.StartTime = ReadTime(ldta, 12);
        layer.InPoint = ReadTime(ldta, 20);
        layer.OutPoint = ReadTime(ldta, 28);
        layer.SourceId = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(40));

        // Bit flags from bytes at offsets 37, 38, 39
        byte bits0 = ldta[37];
        byte bits1 = ldta[38];
        byte bits2 = ldta[39];

        layer.SamplingMode = (LayerSamplingMode)((bits0 & (1 << 6)) >> 6);
        layer.FrameBlendMode = (LayerFrameBlendMode)((bits0 & (1 << 2)) >> 2);
        layer.GuideEnabled = ((bits0 & (1 << 1)) >> 1) == 1;
        layer.NullLayer = (bits1 & (1 << 7)) != 0;
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
            var effectsProp = AepProperty.ParseFromList(effectsTDGP, "ADBE Effect Parade", project?.EffectDefinitions);
            layer.Effects = effectsProp.Properties;
        }

        // Each mask is an "ADBE Mask Atom" whose entries start with an mkif block before its
        // property list, so count the atoms by match name rather than as parsed groups.
        if (rootTDGP.TryGetValue("ADBE Mask Parade", out var masksTDGP))
            layer.MaskCount = AepProperty.PairMatchNames(masksTDGP).matchNames.Count(n => n == "ADBE Mask Atom");

        if (rootTDGP.TryGetValue("ADBE Time Remapping", out var timeRemapTDBS))
            layer.TimeRemapEnabled = AepProperty.ParseFromList(timeRemapTDBS, "ADBE Time Remapping").IsAnimated;

        if (rootTDGP.TryGetValue("ADBE Root Vectors Group", out var contentsTDGP))
            layer.Contents = AepProperty.ParseFromList(contentsTDGP, "ADBE Root Vectors Group");

        // Transform
        if (rootTDGP.TryGetValue("ADBE Transform Group", out var transformTDGP))
            layer.Transform = AepProperty.ParseFromList(transformTDGP, "ADBE Transform Group");

        // Text
        if (rootTDGP.TryGetValue("ADBE Text Properties", out var textTDGP))
        {
            layer.Text = AepProperty.ParseFromList(textTDGP, "ADBE Text Properties");
            PopulateTextContent(layer, layerHead);
            layer.TextAnimatorCount = layer.Text.Properties.FirstOrDefault(p => p.MatchName == "ADBE Text Animators")?.Properties.Count ?? 0;
        }

        if (timeBase != 0)
        {
            foreach (var root in layer.Effects.Append(layer.Transform).Append(layer.Text).Append(layer.Contents))
                AssignTimeBase(root, timeBase);
        }

        return layer;
    }

    // ldta layout past the name (bytes 96+), per py-aep's LdtaChunk (MIT): blending mode
    // u8 at 99, transfer flags at 103 (bit 0 preserve transparency, bit 1 dancing
    // dissolve), track matte type u8 at 107, stretch divisor u32 at 108 (dividend s32 at
    // 8), layer type u8 at 131, parent layer id u32 at 132, and — in files from After Effects 2023 on — the
    // matte layer id u32 at 160. Shorter (older or synthetic) ldta blocks keep defaults.
    private static void ReadCompositingFields(AepLayer layer, byte[] ldta)
    {
        if (ldta.Length >= 104)
        {
            layer.PreserveTransparency = (ldta[103] & 1) != 0;
            layer.BlendingMode = ToBlendingMode(ldta[99], dancingDissolve: (ldta[103] & (1 << 1)) != 0);
        }
        if (ldta.Length >= 108)
            layer.TrackMatte = ldta[107] <= (byte)TrackMatteType.LumaInverted ? (TrackMatteType)ldta[107] : TrackMatteType.None;
        if (ldta.Length >= 112)
        {
            var dividend = BinaryPrimitives.ReadInt32BigEndian(ldta.AsSpan(8));
            var divisor = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(108));
            if (dividend != 0 && divisor != 0)
                layer.Stretch = (double)dividend / divisor;
        }
        if (ldta.Length >= 132)
            layer.LayerType = (LayerType)ldta[131];
        if (ldta.Length >= 136)
        {
            var parent = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(132));
            layer.ParentLayerId = parent == 0 ? null : parent;
        }
        if (ldta.Length >= 164)
        {
            var matte = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(160));
            layer.TrackMatteLayerId = matte == 0 ? null : matte;
        }
    }

    // The stored value is the After Effects SDK PF_Xfer transfer mode; mapping per
    // py-aep's _BLENDING_MODE_BINARY_MAP. 0 appears on cameras, lights and nulls.
    internal static BlendingMode ToBlendingMode(byte raw, bool dancingDissolve = false) => raw switch
    {
        3 => dancingDissolve ? BlendingMode.DancingDissolve : BlendingMode.Dissolve,
        4 => BlendingMode.Add,
        5 => BlendingMode.Multiply,
        6 => BlendingMode.Screen,
        7 => BlendingMode.Overlay,
        8 => BlendingMode.SoftLight,
        9 => BlendingMode.HardLight,
        10 => BlendingMode.Darken,
        11 => BlendingMode.Lighten,
        12 => BlendingMode.ClassicDifference,
        13 => BlendingMode.Hue,
        14 => BlendingMode.Saturation,
        15 => BlendingMode.Color,
        16 => BlendingMode.Luminosity,
        17 => BlendingMode.StencilAlpha,
        18 => BlendingMode.StencilLuma,
        19 => BlendingMode.SilhouetteAlpha,
        20 => BlendingMode.SilhouetteLuma,
        21 => BlendingMode.LuminescentPremul,
        22 => BlendingMode.AlphaAdd,
        23 => BlendingMode.ClassicColorDodge,
        24 => BlendingMode.ClassicColorBurn,
        25 => BlendingMode.Exclusion,
        26 => BlendingMode.Difference,
        27 => BlendingMode.ColorDodge,
        28 => BlendingMode.ColorBurn,
        29 => BlendingMode.LinearDodge,
        30 => BlendingMode.LinearBurn,
        31 => BlendingMode.LinearLight,
        32 => BlendingMode.VividLight,
        33 => BlendingMode.PinLight,
        34 => BlendingMode.HardMix,
        35 => BlendingMode.LighterColor,
        36 => BlendingMode.DarkerColor,
        37 => BlendingMode.Subtract,
        38 => BlendingMode.Divide,
        _ => BlendingMode.Normal,
    };

    private static void AssignTimeBase(AepProperty? property, uint timeBase)
    {
        if (property is null)
            return;
        foreach (var keyframe in property.Keyframes)
            keyframe.TimeBase = timeBase;
        foreach (var child in property.Properties)
            AssignTimeBase(child, timeBase);
    }

    private static double ReadTime(byte[] ldta, int offset)
    {
        if (ldta.Length < offset + 8)
            return 0;
        var dividend = BinaryPrimitives.ReadInt32BigEndian(ldta.AsSpan(offset));
        var divisor = BinaryPrimitives.ReadUInt32BigEndian(ldta.AsSpan(offset + 4));
        return divisor == 0 ? 0 : (double)dividend / divisor;
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
                layer.TextJustification = document.Justification is >= 0 and <= (int)AepSharp.TextJustification.FullJustifyLastLineFull
                    ? (TextJustification)document.Justification.Value
                    : null;
                layer.IsBoxText = document.IsBoxText;
                layer.TextBoxSize = document.BoxSize;
                layer.TextBoxPosition = document.BoxPosition;
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
                Tracking = run.Tracking,
                Leading = run.Leading,
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
