using System.Buffers.Binary;
using System.Text;

namespace AepSharp.Rifx;

internal static class RifxReader
{
    public static RifxList FromStream(Stream stream)
    {
        Span<byte> b4 = stackalloc byte[4];
        ReadExact(stream, b4);
        var magic = Encoding.ASCII.GetString(b4);
        if (magic != "RIFX")
            throw new InvalidDataException($"Expected RIFX magic, got '{magic}'");

        ReadExact(stream, b4);
        var fileSize = BinaryPrimitives.ReadUInt32BigEndian(b4);
        return ReadList(stream, fileSize);
    }

    private static RifxList ReadList(Stream stream, uint limit)
    {
        var list = new RifxList();
        uint bytesRead = 0;

        var idBytes = new byte[4];
        bytesRead += ReadExact(stream, idBytes);
        list.Identifier = Encoding.ASCII.GetString(idBytes);

        while (bytesRead < limit)
        {
            var (block, n) = ReadBlock(stream, limit - bytesRead);
            bytesRead += n;
            list.Blocks.Add(block);
        }

        return list;
    }

    private static (RifxBlock block, uint bytesRead) ReadBlock(Stream stream, uint limit)
    {
        var block = new RifxBlock();
        uint bytesRead = 0;
        Span<byte> b4 = stackalloc byte[4];

        bytesRead += ReadExact(stream, b4);
        block.Type = Encoding.ASCII.GetString(b4);

        bytesRead += ReadExact(stream, b4);
        block.Size = BinaryPrimitives.ReadUInt32BigEndian(b4);

        if (block.Size > limit - bytesRead)
        {
            var rest = new byte[limit - bytesRead];
            bytesRead += ReadExact(stream, rest);
            var combined = new byte[4 + 4 + rest.Length];
            Encoding.ASCII.GetBytes(block.Type).CopyTo(combined, 0);
            b4.CopyTo(combined.AsSpan(4));
            rest.CopyTo(combined, 8);
            block.Type = "ANON";
            block.Data = combined;
        }
        else if (block.Type == "LIST")
        {
            var subList = ReadList(stream, block.Size);
            bytesRead += block.Size;
            block.Data = subList;
        }
        else
        {
            var data = new byte[block.Size];
            bytesRead += ReadExact(stream, data);
            block.Data = data;
        }

        if (block.Size % 2 != 0)
        {
            var pad = new byte[1];
            bytesRead += ReadExact(stream, pad);
        }

        return (block, bytesRead);
    }

    private static uint ReadExact(Stream stream, Span<byte> buffer)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer[totalRead..]);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of RIFX stream");
            totalRead += read;
        }
        return (uint)totalRead;
    }
}
