namespace AepSharp;

public enum BitsPerChannel : byte
{
    Bpc8 = 0x00,
    Bpc16 = 0x01,
    Bpc32 = 0x02
}

public enum ItemType
{
    Folder,
    Composition,
    Footage
}

/// <summary>
/// Footage source kind, read from the footage item's <c>opti</c> block (u16 at offset 4).
/// Values other than these can occur; they are exposed as the raw number.
/// </summary>
public enum FootageType : ushort
{
    /// <summary>Still image file (PNG, JPEG, TIFF...).</summary>
    Image = 0x01,
    Placeholder = 0x02,
    /// <summary>Audio and/or video file (MP4, MOV, MP3, WAV...).</summary>
    AudioVideo = 0x05,
    /// <summary>Vector file (AI, EPS, PDF).</summary>
    Vector = 0x08,
    Solid = 0x09,
    /// <summary>Photoshop document.</summary>
    Photoshop = 0x109
}

/// <summary>Layer kind stored in the layer's ldta block (byte 131), per py-aep's LayerType.</summary>
public enum LayerType : byte
{
    /// <summary>Audio/video layer: footage, composition, solid, or a null (see <see cref="AepLayer.NullLayer"/>).</summary>
    AudioVideo = 0,
    Light = 1,
    Camera = 2,
    Text = 3,
    Shape = 4,
    ThreeDModel = 5,
    ParametricMesh = 7,
}

public enum LayerQuality : ushort
{
    Wireframe = 0x0000,
    Draft = 0x0001,
    Best = 0x0002
}

public enum LayerSamplingMode : byte
{
    Bilinear = 0x00,
    Bicubic = 0x01
}

public enum LayerFrameBlendMode : byte
{
    FrameMix = 0x00,
    PixelMotion = 0x01
}

public enum PropertyType : ushort
{
    LayerSelect = 0x00,
    OneD = 0x02,
    Angle = 0x03,
    Boolean = 0x04,
    Color = 0x05,
    TwoD = 0x06,
    Select = 0x07,
    Group = 0x0D,
    Custom = 0x0F,
    ThreeD = 0x12
}

/// <summary>
/// A layer's blending (transfer) mode, as After Effects scripting names them
/// (<c>AVLayer.blendingMode</c>). Decoded from the SDK <c>PF_Xfer</c> value at ldta
/// byte 99 using py-aep's mapping; <see cref="DancingDissolve"/> is Dissolve plus a
/// flag bit.
/// </summary>
public enum BlendingMode
{
    Normal,
    Dissolve,
    DancingDissolve,
    Darken,
    Multiply,
    LinearBurn,
    ColorBurn,
    ClassicColorBurn,
    Add,
    Lighten,
    Screen,
    LinearDodge,
    ColorDodge,
    ClassicColorDodge,
    Overlay,
    SoftLight,
    HardLight,
    LinearLight,
    VividLight,
    PinLight,
    HardMix,
    Difference,
    ClassicDifference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
    StencilAlpha,
    StencilLuma,
    SilhouetteAlpha,
    SilhouetteLuma,
    AlphaAdd,
    LuminescentPremul,
    LighterColor,
    DarkerColor,
    Subtract,
    Divide,
}

/// <summary>How a layer uses its track matte (<c>AVLayer.trackMatteType</c>).</summary>
public enum TrackMatteType : byte
{
    None = 0,
    Alpha = 1,
    AlphaInverted = 2,
    Luma = 3,
    LumaInverted = 4,
}

/// <summary>Paragraph justification of a text layer (<c>TextDocument.justification</c>).</summary>
public enum TextJustification
{
    Left = 0,
    Right = 1,
    Center = 2,
    FullJustifyLastLineLeft = 3,
    FullJustifyLastLineRight = 4,
    FullJustifyLastLineCenter = 5,
    FullJustifyLastLineFull = 6,
}
