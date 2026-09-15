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

## Building

```bash
dotnet build
dotnet test
```

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
