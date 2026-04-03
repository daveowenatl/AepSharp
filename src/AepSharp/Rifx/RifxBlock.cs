using System.Buffers.Binary;
using System.Text;

namespace AepSharp.Rifx;

internal class RifxBlock
{
    public string Type { get; set; } = "";
    public uint Size { get; set; }
    public object Data { get; set; } = Array.Empty<byte>();

    public byte[] GetBytes() => (byte[])Data;

    public string ToAsciiString()
    {
        var data = GetBytes();
        if (data.Length >= 8
            && Encoding.ASCII.GetString(data, 0, 4).Equals("Utf8", StringComparison.OrdinalIgnoreCase))
        {
            var strLen = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            if (strLen + 8 <= Size)
                return Encoding.UTF8.GetString(data, 8, (int)strLen).TrimEnd('\0');
        }
        return Encoding.UTF8.GetString(data).TrimEnd('\0');
    }

    public ushort ToUInt16() => BinaryPrimitives.ReadUInt16BigEndian(GetBytes());
    public uint ToUInt32() => BinaryPrimitives.ReadUInt32BigEndian(GetBytes());
}
