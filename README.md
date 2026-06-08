# AepSharp

[![CI](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AepSharp.svg)](https://www.nuget.org/packages/AepSharp/)

Reads Adobe After Effects `.aep` files in C#. You get the project's compositions,
footage, folders, layers, and their effect and text properties without opening After
Effects or running ExtendScript.

It's a port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser),
a Go library. They did the work of figuring out the format.

## Why this exists

`.aep` files are RIFX, which is just big-endian RIFF. Normally the only way to read
one in code is to script a running copy of After Effects, which is Windows/macOS only
and slow. AepSharp reads the bytes directly. If you need to know what compositions are
in a template and their size and frame rate, say to list them in a UI or hand a name
off to [nexrender](https://github.com/inlife/nexrender), this does that from any .NET
app.

## Install

```bash
dotnet add package AepSharp
```

Targets .NET 10.

## Quick start

```csharp
using AepSharp;

var project = AepProject.Open("template.aep");

// List the compositions, e.g. to populate a dropdown.
foreach (var item in project.Items.Values)
{
    if (item.ItemType == ItemType.Composition)
        Console.WriteLine($"{item.Name}  {item.Width}x{item.Height} @ {item.Framerate}fps  ({item.DurationSeconds}s)");
}
```

`AepProject.Open(path)` (or `AepProject.FromStream(stream)`) gives you:

- `ExpressionEngine`, e.g. `javascript-1.0`
- `Depth`, the bit depth (`Bpc8`, `Bpc16`, `Bpc32`)
- `RootFolder`, the project's item tree (`AepItem` with `FolderContents`)
- `Items`, every item keyed by id (`Dictionary<uint, AepItem>`)

An `AepItem` has `Name`, `Id`, `ItemType` (`Folder`, `Composition`, or `Footage`),
and for comps and footage, `Width`, `Height`, `Framerate`, `DurationSeconds`,
`FootageType`, and `CompositionLayers`. An `AepLayer` carries its index, name, source
id, quality and blend modes, the layer flags (3D, solo, shy, locked, adjustment, and
the rest), and its `Effects` and `Text` property trees.

## What it reads, and what it doesn't

The scope matches the upstream Go library. It's not the whole format.

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

If you need a field that isn't here, run `aepdump` (below) to find where it sits in
the file, and it can be added.

## aepdump

A small CLI for looking at the raw RIFX chunk tree. Handy for debugging and for
working out where an unparsed field lives.

```bash
dotnet run --project src/AepSharp.Tools -- template.aep
dotnet run --project src/AepSharp.Tools -- template.aep --format json
```

Every chunk prints with its absolute offset, FourCC, declared vs. actual size, a
hex/ascii preview, and a flag if it looks truncated or overflowing.

## Versions tested

Parsing is snapshot-tested against the fixtures from the upstream project (After
Effects around 2022), and it runs against current templates in production. If you hit
a file that reads wrong, open an issue with the `aepdump` output. Redact the previews
if you need to.

## Credits

Port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser)
(MIT). The hard part, reverse-engineering the format, is theirs. Their README has the
research notes and a Kaitai definition if you want to dig into the binary yourself.

## License

[MIT](LICENSE). © 2026 Dave Owen, © 2020 Boltframe.
