using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp;

/// <summary>
/// Decodes a property's keyframes from its tdbs "list" (lhd3 header + ldat items).
/// Record layouts follow py-aep (MIT, github.com/forticheprod/py-aep), which
/// reads and writes files After Effects accepts.
/// </summary>
internal static class KeyframeDecoder
{
    // Every keyframe item starts with an 8-byte header:
    //   s32 time (timebase units, layer-relative), u8 in interpolation,
    //   u8 out interpolation, u8 label, u8 temporal flags.
    private const int HeaderSize = 8;

    // Non-spatial numeric payload: 5 × dimensions doubles, grouped by kind:
    // values, in speeds, in influences, out speeds, out influences.
    private static int MultiDimensionalSize(int dimensions) => HeaderSize + 5 * dimensions * 8;

    // Spatial payload: 3 pad bytes, u8 spatial flags, 4 pad bytes, then doubles:
    // unknown, in speed, in influence, out speed, out influence, value[d],
    // in tangent[d], out tangent[d].
    private static int SpatialSize(int dimensions) => HeaderSize + 8 + 5 * 8 + 3 * dimensions * 8;

    internal static IReadOnlyList<AepKeyframe> Decode(RifxList tdbs, int dimensions, bool isSpatial)
    {
        var list = tdbs.SublistFind("list");
        var lhd3 = list?.FindByType("lhd3")?.GetBytes();
        var ldat = list?.FindByType("ldat")?.GetBytes();
        if (lhd3 is null || lhd3.Length < 20 || ldat is null)
            return Array.Empty<AepKeyframe>();

        var count = BinaryPrimitives.ReadUInt16BigEndian(lhd3.AsSpan(10));
        var itemSize = BinaryPrimitives.ReadUInt16BigEndian(lhd3.AsSpan(18));
        if (count == 0 || itemSize < HeaderSize)
            return Array.Empty<AepKeyframe>();

        // A truncated ldat yields only the complete items it holds.
        var available = Math.Min((int)count, ldat.Length / itemSize);
        var keyframes = new List<AepKeyframe>(available);
        for (var i = 0; i < available; i++)
            keyframes.Add(DecodeItem(ldat.AsSpan(i * itemSize, itemSize), dimensions, isSpatial));
        return keyframes;
    }

    private static AepKeyframe DecodeItem(ReadOnlySpan<byte> item, int dimensions, bool isSpatial)
    {
        var temporalFlags = item[7];
        var keyframe = new AepKeyframe
        {
            TimeUnits = BinaryPrimitives.ReadInt32BigEndian(item),
            InInterpolation = ToInterpolation(item[4]),
            OutInterpolation = ToInterpolation(item[5]),
            Label = item[6],
            TemporalContinuous = (temporalFlags & (1 << 3)) != 0,
            TemporalAutoBezier = (temporalFlags & (1 << 4)) != 0,
            Roving = (temporalFlags & (1 << 5)) != 0,
        };

        if (dimensions <= 0)
            return keyframe;

        if (isSpatial && item.Length >= SpatialSize(dimensions))
        {
            var payload = item[HeaderSize..];
            var spatialFlags = payload[3];
            keyframe.SpatialContinuous = (spatialFlags & 1) != 0;
            keyframe.SpatialAutoBezier = (spatialFlags & (1 << 1)) != 0;

            var doubles = payload[8..];
            keyframe.InEase = [Ease(ReadDouble(doubles, 1), ReadDouble(doubles, 2))];
            keyframe.OutEase = [Ease(ReadDouble(doubles, 3), ReadDouble(doubles, 4))];
            keyframe.Value = ReadDoubles(doubles, 5, dimensions);
            keyframe.InSpatialTangent = ReadDoubles(doubles, 5 + dimensions, dimensions);
            keyframe.OutSpatialTangent = ReadDoubles(doubles, 5 + 2 * dimensions, dimensions);
            return keyframe;
        }

        if (item.Length >= MultiDimensionalSize(dimensions))
        {
            var doubles = item[HeaderSize..];
            keyframe.Value = ReadDoubles(doubles, 0, dimensions);
            var inEase = new AepKeyframeEase[dimensions];
            var outEase = new AepKeyframeEase[dimensions];
            for (var d = 0; d < dimensions; d++)
            {
                inEase[d] = Ease(ReadDouble(doubles, dimensions + d), ReadDouble(doubles, 2 * dimensions + d));
                outEase[d] = Ease(ReadDouble(doubles, 3 * dimensions + d), ReadDouble(doubles, 4 * dimensions + d));
            }
            keyframe.InEase = inEase;
            keyframe.OutEase = outEase;
        }

        return keyframe;
    }

    // Influence is stored as a fraction (0–1); expose it as After Effects reports it (0–100).
    private static AepKeyframeEase Ease(double speed, double influenceFraction) => new(speed, influenceFraction * 100.0);

    // Stored as 1/2/3; After Effects scripting exposes 6612/6613/6614.
    private static KeyframeInterpolation ToInterpolation(byte raw) => raw switch
    {
        2 => KeyframeInterpolation.Bezier,
        3 => KeyframeInterpolation.Hold,
        _ => KeyframeInterpolation.Linear,
    };

    private static double ReadDouble(ReadOnlySpan<byte> doubles, int index) =>
        BinaryPrimitives.ReadDoubleBigEndian(doubles.Slice(index * 8, 8));

    private static double[] ReadDoubles(ReadOnlySpan<byte> doubles, int start, int length)
    {
        var values = new double[length];
        for (var i = 0; i < length; i++)
            values[i] = ReadDouble(doubles, start + i);
        return values;
    }
}
