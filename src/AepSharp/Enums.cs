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

public enum FootageType : ushort
{
    /// <summary>Raster still image (.png, .jpg, …).</summary>
    Image = 0x01,
    /// <summary>Stand-in for missing/external media.</summary>
    Placeholder = 0x02,
    /// <summary>Time-based media: movie or audio (.mp4, .mov, .mp3, .wav).</summary>
    AudioVideo = 0x05,
    /// <summary>Illustrator / vector artwork (.ai).</summary>
    Vector = 0x08,
    /// <summary>A solid-colour source; AE adjustment layers are solids too.</summary>
    Solid = 0x09,
    /// <summary>Layered Photoshop document (.psd).</summary>
    Photoshop = 0x109
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
