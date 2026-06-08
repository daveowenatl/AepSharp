using System.Text;
using System.Text.Json;

namespace AepSharp.Rifx;

/// <summary>
/// Renders a parsed RIFX tree as human-readable text or stable JSON. This is the
/// inspection surface behind the <c>aepdump</c> tool: it exposes every chunk's
/// absolute offset, FourCC, declared size, the bytes actually consumed, and flags
/// anomalies (truncated / size-overflowing chunks captured as ANON). It is the lens
/// for verifying parity against real templates and for reverse-engineering new chunks.
/// </summary>
internal static class RifxDump
{
    internal sealed class Options
    {
        /// <summary>Include a short hex+ascii preview of leaf-block payloads.</summary>
        public bool IncludePreview { get; init; } = true;

        /// <summary>How many leading payload bytes to show in the preview.</summary>
        public int PreviewBytes { get; init; } = 16;
    }

    internal static string ToText(RifxList root, Options? options = null)
    {
        options ??= new Options();
        var sb = new StringBuilder();
        sb.Append("RIFX  ").Append(Fourcc(root.Identifier))
          .Append("  [offset 0x").Append(root.Offset.ToString("x8")).Append("]\n");
        foreach (var block in root.Blocks)
            WriteBlock(sb, block, 1, options);
        return sb.ToString();
    }

    private static void WriteBlock(StringBuilder sb, RifxBlock block, int depth, Options options)
    {
        var indent = new string(' ', depth * 2);
        long consumed = block.Data is RifxList ? block.Size
            : block.Data is byte[] bytes ? bytes.Length
            : 0;

        sb.Append(indent)
          .Append("[0x").Append(block.Offset.ToString("x8")).Append("] ")
          .Append(Fourcc(block.Type))
          .Append("  declared=").Append(block.Size)
          .Append("  consumed=").Append(consumed);

        if (block.IsAnomalous)
            sb.Append("  ** ANOMALOUS (declared size exceeds remaining bytes) **");

        if (block.Data is RifxList list)
        {
            sb.Append("  -> ").Append(Fourcc(list.Identifier)).Append('\n');
            foreach (var child in list.Blocks)
                WriteBlock(sb, child, depth + 1, options);
        }
        else
        {
            if (options.IncludePreview && block.Data is byte[] payload && payload.Length > 0)
                sb.Append("  ").Append(Preview(payload, options.PreviewBytes));
            sb.Append('\n');
        }
    }

    internal static string ToJson(RifxList root, Options? options = null)
    {
        options ??= new Options();
        var node = ListNode(root, options);
        return JsonSerializer.Serialize(node, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Dictionary<string, object?> ListNode(RifxList list, Options options)
    {
        return new Dictionary<string, object?>
        {
            ["kind"] = "list",
            ["identifier"] = list.Identifier,
            ["offset"] = list.Offset,
            ["blocks"] = list.Blocks.ConvertAll(b => BlockNode(b, options)),
        };
    }

    private static Dictionary<string, object?> BlockNode(RifxBlock block, Options options)
    {
        var node = new Dictionary<string, object?>
        {
            ["kind"] = "block",
            ["type"] = block.Type,
            ["offset"] = block.Offset,
            ["declaredSize"] = block.Size,
            ["anomalous"] = block.IsAnomalous,
        };
        if (block.Data is RifxList list)
        {
            node["consumed"] = block.Size;
            node["list"] = ListNode(list, options);
        }
        else if (block.Data is byte[] payload)
        {
            node["consumed"] = payload.Length;
            if (options.IncludePreview && payload.Length > 0)
                node["preview"] = Convert.ToHexString(payload.AsSpan(0, Math.Min(options.PreviewBytes, payload.Length)));
        }
        return node;
    }

    private static string Fourcc(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (var c in raw)
            sb.Append(c is >= ' ' and < (char)0x7f ? c : '.');
        return sb.ToString();
    }

    private static string Preview(byte[] data, int max)
    {
        var n = Math.Min(max, data.Length);
        var hex = new StringBuilder(n * 3);
        var ascii = new StringBuilder(n);
        for (var i = 0; i < n; i++)
        {
            hex.Append(data[i].ToString("x2")).Append(' ');
            ascii.Append(data[i] is >= 0x20 and < 0x7f ? (char)data[i] : '.');
        }
        var ellipsis = data.Length > n ? "…" : "";
        return $"| {hex.ToString().TrimEnd()}{ellipsis}  '{ascii}{ellipsis}'";
    }
}
