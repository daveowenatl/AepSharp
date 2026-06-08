# AepSharp

[![CI](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AepSharp.svg)](https://www.nuget.org/packages/AepSharp/)

Reads Adobe After Effects `.aep` files in C#: compositions, footage, folders, layers,
and their effect and text properties. No After Effects install or ExtendScript needed.

Port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser) (Go).

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
the rest), and its `Effects` and `Text` property trees.

## What it reads

Scope matches the upstream Go library.

Reads:

- Project metadata (expression engine, bit depth)
- The folder and item tree
- Compositions: dimensions, frame rate, duration, background color
- Footage: dimensions, frame rate, duration, solid or placeholder
- Layers: flags, quality, sampling and frame-blend modes, source
- Effect and text property names and structure

Doesn't read (yet):

- Keyframes and animated property values
- Transform values, masks, markers

To find where an unparsed field lives in the binary, use `aepdump`.

## aepdump

CLI that dumps the raw RIFX chunk tree:

```bash
dotnet run --project src/AepSharp.Tools -- template.aep
dotnet run --project src/AepSharp.Tools -- template.aep --format json
```

Each chunk prints with its absolute offset, FourCC, declared vs. actual size, a
hex/ascii preview, and a flag if it looks truncated or overflowing.

## Tested against

The fixtures from the upstream project (After Effects around 2022), plus current
templates in production. If a file reads wrong, open an issue with the `aepdump`
output.

## Credits

Port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser)
(MIT). The format reverse-engineering is theirs; their README has the research notes
and a Kaitai definition.

## License

[MIT](LICENSE). © 2026 Dave Owen, © 2020 Boltframe.
