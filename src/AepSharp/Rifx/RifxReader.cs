using System.Buffers.Binary;
using System.Text;

namespace AepSharp.Rifx;

internal static class RifxReader
{
    /// <summary>
    /// LIST identifiers whose contents are an opaque payload, not RIFF chunks.
    /// "btdk" carries a text layer's EngineData (PostScript-style text); parsing it
    /// as chunks only "works" by accident when its leading bytes misparse as an
    /// oversized chunk. Captured raw into <see cref="RifxList.RawPayload"/> instead.
    /// </summary>
    private static readonly string[] OpaqueListIdentifiers = ["btdk"];

    public static RifxList FromStream(Stream stream)
    {
        long pos = 0;
        Span<byte> b4 = stackalloc byte[4];
        ReadExact(stream, b4, ref pos);
        var magic = Encoding.ASCII.GetString(b4);
        if (magic != "RIFX")
            throw new InvalidDataException($"Expected RIFX magic, got '{magic}'");

        ReadExact(stream, b4, ref pos);
        var fileSize = BinaryPrimitives.ReadUInt32BigEndian(b4);
        return ReadList(stream, fileSize, ref pos);
    }

    private static RifxList ReadList(Stream stream, uint limit, ref long pos)
    {
        var list = new RifxList { Offset = pos };
        uint bytesRead = 0;

        var idBytes = new byte[4];
        bytesRead += ReadExact(stream, idBytes, ref pos);
        list.Identifier = Encoding.ASCII.GetString(idBytes);

        if (Array.IndexOf(OpaqueListIdentifiers, list.Identifier) >= 0)
        {
            var payload = new byte[limit - bytesRead];
            ReadExact(stream, payload, ref pos);
            list.RawPayload = payload;
            return list;
        }

        while (bytesRead < limit)
        {
            var (block, n) = ReadBlock(stream, limit - bytesRead, ref pos);
            bytesRead += n;
            list.Blocks.Add(block);
        }

        return list;
    }

    private static (RifxBlock block, uint bytesRead) ReadBlock(Stream stream, uint limit, ref long pos)
    {
        var block = new RifxBlock { Offset = pos };
        uint bytesRead = 0;
        Span<byte> b4 = stackalloc byte[4];

        bytesRead += ReadExact(stream, b4, ref pos);
        block.Type = Encoding.ASCII.GetString(b4);

        bytesRead += ReadExact(stream, b4, ref pos);
        block.Size = BinaryPrimitives.ReadUInt32BigEndian(b4);

        if (block.Size > limit - bytesRead)
        {
            var rest = new byte[limit - bytesRead];
            bytesRead += ReadExact(stream, rest, ref pos);
            var combined = new byte[4 + 4 + rest.Length];
            Encoding.ASCII.GetBytes(block.Type).CopyTo(combined, 0);
            b4.CopyTo(combined.AsSpan(4));
            rest.CopyTo(combined, 8);
            block.Type = "ANON";
            block.Data = combined;
            block.IsAnomalous = true;
        }
        else if (block.Type == "LIST")
        {
            var subList = ReadList(stream, block.Size, ref pos);
            bytesRead += block.Size;
            block.Data = subList;
        }
        else
        {
            var data = new byte[block.Size];
            bytesRead += ReadExact(stream, data, ref pos);
            block.Data = data;
        }

        if (block.Size % 2 != 0)
        {
            var pad = new byte[1];
            bytesRead += ReadExact(stream, pad, ref pos);
        }

        return (block, bytesRead);
    }

    private static uint ReadExact(Stream stream, Span<byte> buffer, ref long pos)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of RIFX stream");
            totalRead += read;
        }
        pos += totalRead;
        return (uint)totalRead;
    }
}
