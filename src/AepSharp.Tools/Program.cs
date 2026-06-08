using AepSharp.Rifx;

// aepdump — inspect the RIFX chunk tree of an After Effects .aep file.
//
// Usage:
//   aepdump <file.aep> [--format text|json] [--no-preview]
//
// Prints every chunk with its absolute offset, FourCC, declared size, the bytes
// actually consumed, and flags anomalies (truncated / size-overflowing chunks).
// Use it to verify parity against real templates and to reverse-engineer new chunks.

return Run(args);

static int Run(string[] args)
{
    string? path = null;
    var format = "text";
    var includePreview = true;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        switch (arg)
        {
            case "-h" or "--help":
                PrintUsage(Console.Out);
                return 0;
            case "--format":
                if (i + 1 >= args.Length)
                    return Fail("--format requires a value (text|json)");
                format = args[++i].ToLowerInvariant();
                break;
            case "--no-preview":
                includePreview = false;
                break;
            default:
                if (arg.StartsWith('-'))
                    return Fail($"unknown option: {arg}");
                if (path is not null)
                    return Fail($"unexpected extra argument: {arg}");
                path = arg;
                break;
        }
    }

    if (path is null)
        return Fail("no input file given");
    if (format is not ("text" or "json"))
        return Fail($"unknown format '{format}' (expected text|json)");
    if (!File.Exists(path))
        return Fail($"file not found: {path}");

    RifxList root;
    try
    {
        using var stream = File.OpenRead(path);
        root = RifxReader.FromStream(stream);
    }
    catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
    {
        // Malformed/truncated input: report cleanly, never crash with a stack trace.
        return Fail($"not a readable RIFX/.aep file: {ex.Message}");
    }

    var options = new RifxDump.Options { IncludePreview = includePreview };
    Console.Out.Write(format == "json"
        ? RifxDump.ToJson(root, options)
        : RifxDump.ToText(root, options));
    Console.Out.Write('\n');
    return 0;
}

static int Fail(string message)
{
    Console.Error.WriteLine($"aepdump: {message}");
    Console.Error.WriteLine("try 'aepdump --help'");
    return 1;
}

static void PrintUsage(TextWriter w)
{
    w.WriteLine("aepdump — inspect the RIFX chunk tree of an After Effects .aep file");
    w.WriteLine();
    w.WriteLine("usage: aepdump <file.aep> [--format text|json] [--no-preview]");
    w.WriteLine();
    w.WriteLine("options:");
    w.WriteLine("  --format text|json   output format (default: text)");
    w.WriteLine("  --no-preview         omit hex+ascii payload previews");
    w.WriteLine("  -h, --help           show this help");
}
