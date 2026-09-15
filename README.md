# AepSharp

[![CI](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AepSharp.svg)](https://www.nuget.org/packages/AepSharp/)

AepSharp reads Adobe After Effects `.aep` project files from C#. You get the item tree,
compositions, footage, layers, transforms and keyframes, effects, shape contents and text,
without After Effects installed and without ExtendScript.

## Install

```bash
dotnet add package AepSharp
```

The package targets .NET 8, 9 and 10.

## Quick start

```csharp
using AepSharp;

var project = AepProject.Open("template.aep");   // or AepProject.FromStream(stream)

foreach (var comp in project.Items.Values.Where(i => i.ItemType == ItemType.Composition))
{
    Console.WriteLine($"{comp.Name}  {comp.Width}x{comp.Height} @ {comp.Framerate}fps, {comp.DurationSeconds}s");

    foreach (var layer in comp.CompositionLayers)
        Console.WriteLine($"  {layer.Index} {layer.Name} ({layer.LayerType}) {layer.CompositionInPoint}s–{layer.CompositionOutPoint}s");
}
```

## What it reads

- Project metadata (expression engine, bit depth) and the folder and item tree
- Compositions: dimensions, frame rate, duration, background colour
- Footage: dimensions, frame rate, duration, type (image, audio/video, vector, Photoshop, solid,
  placeholder), source file path, solid colour
- Layers: type, null, switches, quality, sampling and frame blend modes, source, parent, timing,
  time stretch, blending mode, track matte and matte layer
- Transforms, separated position included, with keyframes interpolated the way After Effects does it
- Expression source and whether each expression is enabled, Source Text expressions included
- Effects: names, parameter values or keyframes, and layer references. This includes repeat
  instances of an effect that share the project's effect definition.
- Shape layer contents as stored (groups, rectangles, ellipses, fills, strokes, transforms and so
  on), with values and each group's enabled switch
- Text: copy, fonts, styled runs (size, colours, tracking, leading, caps), paragraph
  justification, and point or box text with the box size and position

It doesn't read these yet:

- Masks beyond their count, markers, and the vertices of shape paths
- Values of mask reference, 3D point and curve effect parameters
- Time stretch when sampling keyframes, so `TransformValueAt` assumes 100%
- Justification for any paragraph after the first
- Linear keyframes on a curved spatial path. These follow a straight line, same as py-aep.

`UnclampedCompositionOutPoint` also comes with a caveat: it follows the clamp rule, but I haven't
compared it with a real render yet.

## Accuracy

The tests run against the `.aep` fixtures in this repo, saved from After Effects around 2022. I
also check it against a private set of 181 production templates. On that set, keyframe decoding
and interpolation agree with py-aep for every animated transform property, and keyframed
positions line up with frames rendered by After Effects. Footage paths, layer timing and
compositing, text layout and effect parameter values are compared with py-aep on the same
templates.

If a file comes out wrong, please [open an issue](https://github.com/daveowenatl/AepSharp/issues)
and include the `aepdump` output.

## aepdump

`aepdump` is a command-line tool for poking at files. It lives in this repo and isn't part of the
NuGet package.

```bash
dotnet run --project src/AepSharp.Tools -- tree template.aep          # comps, layers, text copy
dotnet run --project src/AepSharp.Tools -- scene template.aep         # JSON scene description
dotnet run --project src/AepSharp.Tools -- scene template.aep --bake  # plus per-frame animated values
dotnet run --project src/AepSharp.Tools -- rifx template.aep --format json
```

`tree` prints the item tree, with each composition's layers and their text.

`scene` writes JSON for renderers and template tooling to consume. It has footage source paths,
and for each layer its type, switches, timing, compositing, transforms and keyframes, effect
parameters, shape contents and text layout. Every expression is listed by its match-name path.

`rifx` lists the raw binary chunks: offset, FourCC, declared and actual size, and a hex preview.
It flags chunks that look truncated or overflow their parent. When a field isn't parsed yet, this
is how you find where it lives in the file.

## Building and releasing

```bash
dotnet build
dotnet test
```

To publish a release, set `<Version>` in `src/AepSharp/AepSharp.csproj` and push a tag with the
same version (`git tag v0.2.0 && git push origin v0.2.0`). The `release` workflow runs the tests,
packs, and pushes to nuget.org with Trusted Publishing. The nuget.org policy is tied to the file
name `.github/workflows/release.yml`, so don't rename it.

## Credits

AepSharp started as a port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser)
(MIT). Boltframe did the original reverse engineering of the format, and their README has the
research notes and a Kaitai definition.

The record layouts for keyframes, layers (`ldta`), effect parameters (`pard`), footage aliases and
text layout come from [py-aep](https://github.com/forticheprod/py-aep) (MIT), and so does keyframe
interpolation. py-aep ported its interpolation from [lottie-web](https://github.com/airbnb/lottie-web) (MIT).

## License

[MIT](https://github.com/daveowenatl/AepSharp/blob/main/LICENSE). © 2026 Dave Owen, © 2020
Boltframe. Code ported from py-aep (© 2023 Fortiche production) and lottie-web (© 2015 Bodymovin)
is listed in [THIRD-PARTY-NOTICES.md](https://github.com/daveowenatl/AepSharp/blob/main/THIRD-PARTY-NOTICES.md).
