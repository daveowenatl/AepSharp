using System.Buffers.Binary;
using AepSharp.Rifx;

namespace AepSharp;

/// <summary>A point of a path, in the path's normalized bounds (0–1 across <see cref="AepPath.Bounds"/>).</summary>
public readonly record struct AepPathPoint(double X, double Y);

/// <summary>The bounds a path's points are normalized to, as the file stores them (four floats after the header flags).</summary>
public readonly record struct AepPathBounds(double Left, double Top, double Right, double Bottom);

/// <summary>
/// A bezier path as a mask shape or a shape layer's Path property stores it: a header
/// (<c>shph</c>: flags and bounds) and a vertex table (<c>ldat</c>: three points per vertex).
/// </summary>
/// <remarks>
/// Layout observed on masks and shape paths written by After Effects 2026: bit 3 of the header's
/// fourth byte is set for an open path; the bounds are four big-endian f32s at offset 4; each vertex
/// is three (x, y) f32 pairs, read here as incoming tangent, vertex, outgoing tangent, all in the
/// normalized bounds. A path with no static vertices (an expression builds it, or it was never
/// drawn) has an empty <see cref="Vertices"/>.
/// </remarks>
public sealed class AepPath
{
    public bool Closed { get; internal set; }
    public AepPathBounds Bounds { get; internal set; }
    public IReadOnlyList<AepPathPoint> Vertices { get; internal set; } = Array.Empty<AepPathPoint>();
    public IReadOnlyList<AepPathPoint> InTangents { get; internal set; } = Array.Empty<AepPathPoint>();
    public IReadOnlyList<AepPathPoint> OutTangents { get; internal set; } = Array.Empty<AepPathPoint>();

    private const int PointSize = 8;
    private const int PointsPerVertex = 3;

    internal static AepPath? Decode(RifxList shap)
    {
        var header = shap.FindByType("shph")?.GetBytes();
        if (header is null || header.Length < 20)
            return null;
        var path = new AepPath
        {
            Closed = (header[3] & (1 << 3)) == 0,
            Bounds = new AepPathBounds(
                BinaryPrimitives.ReadSingleBigEndian(header.AsSpan(4)),
                BinaryPrimitives.ReadSingleBigEndian(header.AsSpan(8)),
                BinaryPrimitives.ReadSingleBigEndian(header.AsSpan(12)),
                BinaryPrimitives.ReadSingleBigEndian(header.AsSpan(16))),
        };

        var list = shap.SublistFind("list");
        var lhd3 = list?.FindByType("lhd3")?.GetBytes();
        var ldat = list?.FindByType("ldat")?.GetBytes();
        if (lhd3 is null || lhd3.Length < 20 || ldat is null)
            return path;
        var count = BinaryPrimitives.ReadUInt16BigEndian(lhd3.AsSpan(10));
        var itemSize = BinaryPrimitives.ReadUInt16BigEndian(lhd3.AsSpan(18));
        if (count == 0 || itemSize < PointSize)
            return path;

        var points = Math.Min((int)count, ldat.Length / itemSize);
        var vertices = new List<AepPathPoint>();
        var ins = new List<AepPathPoint>();
        var outs = new List<AepPathPoint>();
        for (var i = 0; i + PointsPerVertex <= points; i += PointsPerVertex)
        {
            ins.Add(Point(ldat, i * itemSize));
            vertices.Add(Point(ldat, (i + 1) * itemSize));
            outs.Add(Point(ldat, (i + 2) * itemSize));
        }
        path.Vertices = vertices;
        path.InTangents = ins;
        path.OutTangents = outs;
        return path;
    }

    private static AepPathPoint Point(byte[] ldat, int offset) =>
        new(BinaryPrimitives.ReadSingleBigEndian(ldat.AsSpan(offset)), BinaryPrimitives.ReadSingleBigEndian(ldat.AsSpan(offset + 4)));
}
