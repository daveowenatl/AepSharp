# AepSharp

[![CI](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AepSharp.svg)](https://www.nuget.org/packages/AepSharp/)

Reads Adobe After Effects `.aep` files in C#: compositions, footage, folders, layers,
and their effect and text properties. No After Effects install or ExtendScript needed.

## Install

```bash
dotnet add package AepSharp
```

Targets .NET 10.

## Usage

```csharp
using AepSharp;

var project = AepProject.Open("template.aep");

foreach (var item in project.Items.Values)
{
    if (item.ItemType == ItemType.Composition)
        Console.WriteLine($"{item.Name}  {item.Width}x{item.Height} @ {item.Framerate}fps  ({item.DurationSeconds}s)");
}
```

`AepProject.Open(path)` (or `AepProject.FromStream(stream)`) returns:

- `ExpressionEngine`, e.g. `javascript-1.0`
- `Depth`, the bit depth (`Bpc8`, `Bpc16`, `Bpc32`)
- `RootFolder`, the project's item tree (`AepItem` with `FolderContents`)
- `Items`, every item keyed by id (`Dictionary<uint, AepItem>`)

An `AepItem` has `Name`, `Id`, `ItemType` (`Folder`, `Composition`, or `Footage`),
and for comps and footage, `Width`, `Height`, `Framerate`, `DurationSeconds`,
`FootageType`, and `CompositionLayers`. An `AepLayer` carries its index, name, source
id, quality and blend modes, the layer flags (3D, solo, shy, locked, adjustment, and
the rest), its timing (`StartTime`, `CompositionInPoint`, `CompositionOutPoint`), its
`Transform`, `Effects` and `Text` property trees, and the text copy and styled runs.

### Transforms and keyframes

```csharp
var layer = comp.CompositionLayers[0];

// Static values (null when left at the After Effects default or animated)
var position = layer.Position;   // [x, y, z]
var scale = layer.Scale;         // fractions: 100% = 1.0

// Animated values, interpolated like After Effects (pre-expression)
var opacity = layer.FindTransformProperty("ADBE Opacity");
foreach (var key in opacity.Keyframes)
    Console.WriteLine($"{key.Time + layer.StartTime}s  {key.Value[0]}  {key.OutInterpolation}");

var positionAtTwoSeconds = layer.TransformValueAt("ADBE Position", 2.0); // composition time
```

`AepProperty` exposes `Keyframes` (time, value, in/out interpolation, per-dimension
ease, spatial tangents, auto-Bezier and roving flags), `IsSpatial`, `Dimensions`,
`Expression` / `ExpressionEnabled`, and `ValueAtTime(layerTime)`. Interpolation covers
hold, linear and Bezier keyframes, temporal ease, spatial Bezier paths (by arc length)
and auto-Bezier. Expressions are reported, not evaluated.

## What it reads

Reads:

- Project metadata (expression engine, bit depth)
- The folder and item tree
- Compositions: dimensions, frame rate, duration, background color
- Footage: dimensions, frame rate, duration, solid or placeholder
- Layers: flags, quality, sampling and frame-blend modes, source, start/in/out times
- Transform values (anchor point, position, scale, rotation, opacity)
- Keyframes for numeric properties, with After Effects-style interpolation
- Expression source and whether it's enabled
- Effect and text property names and structure; text copy, fonts and styled runs

Doesn't read (yet):

- Masks, markers, shape layer contents
- Time stretch, paragraph justification and box text layout
- Separated dimensions (`Position_0`/`Position_1`) as a combined value
- Linear keyframes on bent spatial paths follow a straight line (as py-aep does)

To find where an unparsed field lives in the binary, use `aepdump`.

## aepdump

CLI for inspecting files:

```bash
dotnet run --project src/AepSharp.Tools -- tree template.aep          # comps, layers, text copy
dotnet run --project src/AepSharp.Tools -- scene template.aep         # JSON: timing, transforms, keyframes, text
dotnet run --project src/AepSharp.Tools -- scene template.aep --bake  # plus per-frame animated values
dotnet run --project src/AepSharp.Tools -- rifx template.aep --format json
```

`rifx` prints each chunk with its absolute offset, FourCC, declared vs. actual size, a
hex/ascii preview, and a flag if it looks truncated or overflowing. `scene` is meant as
input for renderers and template tooling.

## Tested against

A set of real `.aep` fixtures (After Effects around 2022), plus a corpus of 181
production templates. Keyframe decoding and interpolation match py-aep on every
animated transform property in that corpus, and keyframed positions match frames
rendered by After Effects. If a file reads wrong, open an issue with the `aepdump`
output.

## Credits

Port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser)
(MIT). The format reverse-engineering is theirs; their README has the research notes
and a Kaitai definition.

Keyframe record layouts and interpolation follow [py-aep](https://github.com/forticheprod/py-aep)
(MIT), whose interpolation is ported from [lottie-web](https://github.com/airbnb/lottie-web) (MIT).

## License

[MIT](LICENSE). © 2026 Dave Owen, © 2020 Boltframe.
