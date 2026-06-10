using System.ComponentModel;
using AepSharp;
using AepSharp.Rifx;
using Spectre.Console;
using Spectre.Console.Cli;

// aepdump — inspect an After Effects .aep file.
//   aepdump <file.aep>            pretty project tree (comps, layers, text copy)
//   aepdump tree <file.aep>       (same, explicit)
//   aepdump rifx <file.aep>       raw RIFX chunk tree with offsets/sizes

var app = new CommandApp<TreeCommand>();
app.Configure(config =>
{
    config.SetApplicationName("aepdump");
    config.AddCommand<TreeCommand>("tree")
        .WithDescription("Print the parsed project model (comps, layers, text copy) as a tree.");
    config.AddCommand<RifxCommand>("rifx")
        .WithDescription("Print the raw RIFX chunk tree with absolute offsets and sizes.");
});
return app.Run(args);

// ---------------------------------------------------------------------------

/// <summary>Error messages belong on stderr so piped/captured stdout stays clean.</summary>
internal static class ErrorConsole
{
    public static readonly IAnsiConsole Instance = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error),
    });
}

internal sealed class TreeSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    [Description("Path to the .aep file")]
    public string File { get; init; } = "";
}

internal sealed class TreeCommand : Command<TreeSettings>
{
    protected override int Execute(CommandContext context, TreeSettings settings, CancellationToken cancellation)
    {
        if (!System.IO.File.Exists(settings.File))
            return Error($"file not found: {settings.File}");

        AepProject project;
        try
        {
            project = AepProject.Open(settings.File);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            return Error($"not a readable .aep file: {ex.Message}");
        }

        AnsiConsole.MarkupLineInterpolated(
            $"[bold]{Path.GetFileName(settings.File)}[/]  engine={project.ExpressionEngine}  depth={project.Depth}  items={project.Items.Count}");

        var tree = new Tree(ItemLabel(project.RootFolder));
        foreach (var child in project.RootFolder.FolderContents)
            AddItem(tree, child);
        AnsiConsole.Write(tree);
        return 0;
    }

    private static void AddItem(IHasTreeNodes parent, AepItem item)
    {
        var node = parent.AddNode(ItemLabel(item));
        if (item.ItemType == ItemType.Folder)
        {
            foreach (var child in item.FolderContents)
                AddItem(node, child);
        }
        else if (item.ItemType == ItemType.Composition)
        {
            foreach (var layer in item.CompositionLayers)
                AddLayer(node, layer);
        }
    }

    private static void AddLayer(IHasTreeNodes parent, AepLayer layer)
    {
        var node = parent.AddNode(LayerLabel(layer));
        foreach (var fx in layer.Effects)
            node.AddNode($"[grey]fx:[/] {Esc(Show(fx.Name))}");
    }

    private static string ItemLabel(AepItem item) => item.ItemType switch
    {
        ItemType.Folder => $"📁 {Esc(Show(item.Name))}  [grey]({item.FolderContents.Count})[/]",
        ItemType.Composition =>
            $"🎬 [green]{Esc(Show(item.Name))}[/]  [grey]{item.Width}x{item.Height}  {item.Framerate:0.###}fps  {item.DurationSeconds:0.###}s  ({item.CompositionLayers.Count} layers)[/]",
        ItemType.Footage =>
            $"🖼  [blue]{Esc(Show(item.Name))}[/]  [grey]{item.Width}x{item.Height}  type={item.FootageType}[/]",
        _ => Esc(Show(item.Name))
    };

    private static string LayerLabel(AepLayer layer)
    {
        var parts = new List<string>();
        if (layer.SourceId != 0) parts.Add($"[grey]← src={layer.SourceId}[/]");

        if (layer.SourceText is { } text)
            parts.Add(text.Length == 0 ? "[grey](empty text)[/]" : $"[yellow]\"{Esc(text)}\"[/]");

        var flags = NonDefaultFlags(layer);
        if (flags.Count > 0) parts.Add($"[grey]{string.Join(",", flags)}[/]");

        var suffix = parts.Count > 0 ? "  " + string.Join("  ", parts) : "";
        return $"[grey]#{layer.Index}[/] {Esc(Show(layer.Name))}{suffix}";
    }

    private static List<string> NonDefaultFlags(AepLayer layer)
    {
        var flags = new List<string>();
        if (layer.GuideEnabled) flags.Add("guide");
        if (layer.SoloEnabled) flags.Add("solo");
        if (layer.ThreeDEnabled) flags.Add("3D");
        if (layer.AdjustmentLayerEnabled) flags.Add("adjustment");
        if (layer.CollapseTransformEnabled) flags.Add("collapse");
        if (layer.ShyEnabled) flags.Add("shy");
        if (layer.LockEnabled) flags.Add("lock");
        if (layer.FrameBlendEnabled) flags.Add("frameBlend");
        if (layer.MotionBlurEnabled) flags.Add("motionBlur");
        if (!layer.VideoEnabled) flags.Add("hidden");
        if (!layer.AudioEnabled) flags.Add("muted");
        if (layer.Quality != LayerQuality.Best) flags.Add(layer.Quality.ToString().ToLowerInvariant());
        return flags;
    }

    private static string Esc(string s) => Markup.Escape(s);
    private static string Show(string s) => string.IsNullOrEmpty(s) ? "(unnamed)" : s;

    private static int Error(string message)
    {
        ErrorConsole.Instance.MarkupLineInterpolated($"[red]aepdump:[/] {message}");
        return 1;
    }
}

// ---------------------------------------------------------------------------

internal sealed class RifxSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    [Description("Path to the .aep file")]
    public string File { get; init; } = "";

    [CommandOption("-f|--format <FORMAT>")]
    [Description("Output format: text or json")]
    [DefaultValue("text")]
    public string Format { get; init; } = "text";

    [CommandOption("--no-preview")]
    [Description("Omit hex+ascii payload previews")]
    public bool NoPreview { get; init; }
}

internal sealed class RifxCommand : Command<RifxSettings>
{
    protected override ValidationResult Validate(CommandContext context, RifxSettings settings)
    {
        var fmt = settings.Format.ToLowerInvariant();
        if (fmt is not ("text" or "json"))
            return ValidationResult.Error($"unknown format '{settings.Format}' (expected text|json)");
        if (!System.IO.File.Exists(settings.File))
            return ValidationResult.Error($"file not found: {settings.File}");
        return ValidationResult.Success();
    }

    protected override int Execute(CommandContext context, RifxSettings settings, CancellationToken cancellation)
    {
        RifxList root;
        try
        {
            using var stream = System.IO.File.OpenRead(settings.File);
            root = RifxReader.FromStream(stream);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            ErrorConsole.Instance.MarkupLineInterpolated($"[red]aepdump:[/] not a readable RIFX/.aep file: {ex.Message}");
            return 1;
        }

        var options = new RifxDump.Options { IncludePreview = !settings.NoPreview };
        var output = settings.Format.ToLowerInvariant() == "json"
            ? RifxDump.ToJson(root, options)
            : RifxDump.ToText(root, options);
        Console.Out.Write(output);
        Console.Out.Write('\n');
        return 0;
    }
}
