using System.ComponentModel;
using AepSharp;
using AepSharp.Rifx;
using Spectre.Console;
using Spectre.Console.Cli;

// aepdump — inspect an After Effects .aep file.
//   aepdump <file.aep>            pretty project tree (comps, layers, text copy)
//   aepdump tree <file.aep>       (same, explicit)
//   aepdump rifx <file.aep>       raw RIFX chunk tree with offsets/sizes
//   aepdump scene <file.aep>      compositions, layers, timing, transforms, keyframes, effects and text as JSON
//                  [--bake]         plus per-frame values of animated transform and effect properties

var app = new CommandApp<TreeCommand>();
app.Configure(config =>
{
    config.SetApplicationName("aepdump");
    config.AddCommand<TreeCommand>("tree")
        .WithDescription("Print the parsed project model (comps, layers, text copy) as a tree.");
    config.AddCommand<SceneCommand>("scene")
        .WithDescription("Print compositions, layers, timing, transforms, effects and text as JSON (for renderers and tooling).");
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

// ---------------------------------------------------------------------------

internal sealed class SceneSettings : CommandSettings
{
    [CommandArgument(0, "<file>")]
    [Description("Path to the .aep file")]
    public string File { get; init; } = "";

    [CommandOption("--bake")]
    [Description("Also emit per-frame values for animated transform and effect properties over each layer's visible range")]
    public bool Bake { get; init; }
}

/// <summary>
/// Emits a renderer-oriented JSON view of the project: every composition with its
/// layers' timing (composition time), compositing (blend mode, track matte, stretch),
/// static transform values, animated properties, effect parameters, source item and
/// text layout and runs; plus footage with source paths. Transform values absent from the output
/// are at their After Effects defaults.
/// </summary>
internal sealed class SceneCommand : Command<SceneSettings>
{
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
    };

    protected override int Execute(CommandContext context, SceneSettings settings, CancellationToken cancellation)
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

        var compositions = project.Items.Values
            .Where(i => i.ItemType == ItemType.Composition)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.Width,
                c.Height,
                c.Framerate,
                Duration = c.DurationSeconds,
                Layers = c.CompositionLayers.Select(l => Layer(project, c, l, settings.Bake)).ToList(),
            })
            .ToList();

        var footage = project.Items.Values
            .Where(i => i.ItemType == ItemType.Footage)
            .Select(f => new { f.Id, f.Name, f.Width, f.Height, Type = f.FootageType.ToString(), Duration = f.DurationSeconds, f.SourcePath, f.SolidColor })
            .ToList();

        Console.Out.Write(System.Text.Json.JsonSerializer.Serialize(new { File = Path.GetFileName(settings.File), Compositions = compositions, Footage = footage }, JsonOptions));
        Console.Out.Write('\n');
        return 0;
    }

    private static object Layer(AepProject project, AepItem composition, AepLayer layer, bool bake)
    {
        var source = layer.SourceId != 0 && project.Items.TryGetValue(layer.SourceId, out var item) ? item : null;
        return new
        {
            layer.Index,
            layer.Name,
            Source = source is null ? null : new { source.Id, source.Name, Kind = source.ItemType.ToString() },
            Type = layer.LayerType?.ToString(),
            Null = layer.NullLayer ? true : (bool?)null,
            ThreeD = layer.ThreeDEnabled ? true : (bool?)null,
            Adjustment = layer.AdjustmentLayerEnabled ? true : (bool?)null,
            CollapseTransform = layer.CollapseTransformEnabled ? true : (bool?)null,
            TimeRemap = layer.TimeRemapEnabled ? true : (bool?)null,
            EffectsEnabled = layer.EffectsEnabled ? (bool?)null : false,
            Masks = layer.MaskCount > 0 ? layer.MaskCount : (int?)null,
            TextAnimators = layer.TextAnimatorCount > 0 ? layer.TextAnimatorCount : (int?)null,
            Visible = layer.VideoEnabled,
            Audible = layer.AudioEnabled,
            MotionBlur = layer.MotionBlurEnabled ? true : (bool?)null,
            layer.GuideEnabled,
            In = layer.CompositionInPoint,
            Out = layer.CompositionOutPoint,
            OutUnclamped = Math.Abs(layer.UnclampedCompositionOutPoint - layer.CompositionOutPoint) > 1e-9 ? layer.UnclampedCompositionOutPoint : (double?)null,
            layer.StartTime,
            Stretch = layer.Stretch == 1.0 ? (double?)null : layer.Stretch,
            BlendingMode = layer.BlendingMode.ToString(),
            TrackMatte = layer.TrackMatte == TrackMatteType.None ? null : layer.TrackMatte.ToString(),
            TrackMatteLayerId = layer.TrackMatteLayerId,
            ParentLayerId = layer.ParentLayerId,
            layer.Id,
            Transform = new
            {
                Anchor = layer.AnchorPoint,
                layer.Position,
                layer.Scale,
                layer.Rotation,
                layer.Opacity,
            },
            Animated = layer.Transform?.Properties.Where(p => p.IsAnimated)
                .Select(p => Animated(composition, layer, p, bake)).ToList() is { Count: > 0 } animated ? animated : null,
            Effects = layer.Effects.Count > 0 ? layer.Effects.Select(e => Effect(composition, layer, e, bake)).ToList() : null,
            Expressions = Expressions(layer) is { Count: > 0 } expressions ? expressions : null,
            Contents = layer.Contents is null ? null : ShapeTree(layer.Contents),
            Text = layer.SourceText is null ? null : new
            {
                Content = layer.SourceText,
                Justification = layer.TextJustification?.ToString(),
                BoxText = layer.IsBoxText,
                BoxSize = layer.TextBoxSize,
                BoxPosition = layer.TextBoxPosition,
                Runs = layer.TextRuns.Select(r => new { r.Text, Font = r.FontName, Size = r.FontSize, Fill = r.FillColor, Stroke = r.StrokeColor, r.Tracking, r.Leading }).ToList(),
                Animators = TextAnimators(layer) is { Count: > 0 } animators ? animators : null,
            },
        };
    }

    // Effect parameter values are emitted as stored: sliders and angles as numbers,
    // checkboxes 0/1, popups as 1-based indices, colours ARGB 0-255, points as fractions
    // of the layer size. Layer references carry the referenced layer's id.
    private static object Effect(AepItem composition, AepLayer layer, AepProperty effect, bool bake) => new
    {
        effect.MatchName,
        effect.Name,
        Parameters = effect.Properties.Select(p => new
        {
            p.Name,
            p.MatchName,
            p.Value,
            Layer = p.LayerReferenceId,
            Animated = p.IsAnimated ? Animated(composition, layer, p, bake) : null,
        }).ToList(),
    };

    // A shape layer's contents as stored: groups with their children, leaf properties with
    // their static value (stored units: percentages as fractions), animated and expression
    // flags, and the enabled switch when it's off.
    private static List<object> ShapeTree(AepProperty group) =>
        group.Properties.Select(p => (object)new
        {
            p.MatchName,
            Enabled = p.Enabled ? (bool?)null : false,
            Value = p.Properties.Count == 0 ? p.Value : null,
            Animated = p.IsAnimated ? true : (bool?)null,
            Expression = p.Expression is null ? null : (bool?)p.ExpressionEnabled,
            Children = p.Properties.Count > 0 ? ShapeTree(p) : null,
        }).ToList();

    // Text animators as stored: only the properties added to each animator are in the
    // file, so an animator whose properties sit at their defaults (or are driven by a
    // no-op expression) changes nothing.
    private static List<object> TextAnimators(AepLayer layer)
    {
        var group = layer.Text?.Properties.FirstOrDefault(p => p.MatchName == "ADBE Text Animators");
        return group?.Properties.Select(animator => (object)new
        {
            animator.Name,
            Selectors = animator.Properties.FirstOrDefault(p => p.MatchName == "ADBE Text Selectors")?.Properties.Count ?? 0,
            Properties = animator.Properties.FirstOrDefault(p => p.MatchName == "ADBE Text Animator Properties")?.Properties
                .Select(p => new { p.MatchName, p.Value, Animated = p.IsAnimated ? true : (bool?)null, Expression = p.Expression is null ? null : (bool?)p.ExpressionEnabled })
                .ToList(),
        }).ToList() ?? new List<object>();
    }

    // Every expression on the layer's transform, effect and text properties, with a
    // path of match names (e.g. "ADBE Transform Group/ADBE Opacity").
    private static List<object> Expressions(AepLayer layer)
    {
        var found = new List<object>();
        void Walk(AepProperty? property, string path)
        {
            if (property is null)
                return;
            var here = path.Length == 0 ? property.MatchName : $"{path}/{property.MatchName}";
            if (property.Expression is { } expression)
                found.Add(new { Property = here, Expression = expression, Enabled = property.ExpressionEnabled });
            foreach (var child in property.Properties)
                Walk(child, here);
        }
        Walk(layer.Transform, "");
        foreach (var effect in layer.Effects)
            Walk(effect, "ADBE Effect Parade");
        Walk(layer.Text, "");
        Walk(layer.Contents, "");
        return found;
    }

    // Keyframe times are emitted in composition time. Baked values sample the
    // pre-expression value at each composition frame the layer is visible.
    private static object Animated(AepItem composition, AepLayer layer, AepProperty property, bool bake)
    {
        List<object>? frames = null;
        if (bake && composition.Framerate > 0)
        {
            frames = new List<object>();
            var first = Math.Max(0, (int)Math.Ceiling(layer.CompositionInPoint * composition.Framerate - 1e-6));
            var last = (int)Math.Floor(Math.Min(layer.CompositionOutPoint, composition.DurationSeconds) * composition.Framerate - 1e-6);
            for (var frame = first; frame <= last; frame++)
                frames.Add(new { Frame = frame, Value = property.ValueAtTime(frame / composition.Framerate - layer.StartTime) });
        }

        return new
        {
            property.MatchName,
            property.IsSpatial,
            Expression = property.ExpressionEnabled ? property.Expression : null,
            Keyframes = property.Keyframes.Select(k => new
            {
                Time = k.Time + layer.StartTime,
                k.Value,
                In = k.InInterpolation.ToString(),
                Out = k.OutInterpolation.ToString(),
                InEase = k.InEase.Select(e => new { e.Speed, e.Influence }),
                OutEase = k.OutEase.Select(e => new { e.Speed, e.Influence }),
                k.InSpatialTangent,
                k.OutSpatialTangent,
                TemporalAutoBezier = k.TemporalAutoBezier ? true : (bool?)null,
                SpatialAutoBezier = k.SpatialAutoBezier ? true : (bool?)null,
                Roving = k.Roving ? true : (bool?)null,
            }).ToList(),
            Frames = frames,
        };
    }

    private static int Error(string message)
    {
        ErrorConsole.Instance.MarkupLineInterpolated($"[red]aepdump:[/] {message}");
        return 1;
    }
}
