# AepSharp

[![CI](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml/badge.svg)](https://github.com/daveowenatl/AepSharp/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/AepSharp.svg)](https://www.nuget.org/packages/AepSharp/)

A native C# parser for Adobe After Effects **`.aep`** project files. Read project
structure, compositions, footage, layers, and effect/text properties directly from
the binary file — no After Effects install and no ExtendScript engine required.

It's a port of the Go library
[boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser).

## Why

After Effects projects are stored as RIFX (big-endian RIFF). Traditionally the only
way to inspect one programmatically is to drive the ExtendScript engine inside a
running copy of After Effects (Windows/macOS only, slow, heavyweight). AepSharp reads
the file statically, so you can answer questions like "what compositions are in this
template, and at what resolution and frame rate?" from any .NET app — handy for
render pipelines (e.g. feeding composition names to [nexrender](https://github.com/inlife/nexrender)),
asset catalogs, and tooling.

## Install

```bash
dotnet add package AepSharp
```

Targets .NET 10.

## Quick start

```csharp
using AepSharp;

var project = AepProject.Open("template.aep");

// Enumerate compositions — e.g. to populate a dropdown.
foreach (var item in project.Items.Values)
{
    if (item.ItemType == ItemType.Composition)
        Console.WriteLine($"{item.Name}  {item.Width}x{item.Height} @ {item.Framerate}fps  ({item.DurationSeconds}s)");
}
```

`AepProject.Open(path)` (or `AepProject.FromStream(stream)`) returns:

- `ExpressionEngine` — e.g. `javascript-1.0`
- `Depth` — bits per channel (`Bpc8` / `Bpc16` / `Bpc32`)
- `RootFolder` — the project's item tree (`AepItem` with `FolderContents`)
- `Items` — every item by id (`Dictionary<uint, AepItem>`)

Each `AepItem` exposes `Name`, `Id`, `ItemType` (`Folder` / `Composition` / `Footage`),
plus `Width`, `Height`, `Framerate`, `DurationSeconds`, `FootageType`, and
`CompositionLayers`. Each `AepLayer` exposes its index, name, source id, quality and
blend modes, the full set of layer flags (3D, solo, shy, locked, adjustment, …), and
its `Effects` / `Text` property trees.

## What it parses (and what it doesn't)

This is a deliberately bounded, honest scope — it matches the upstream Go library.

**Parsed:**
- Project metadata (expression engine, bit depth)
- The folder / item tree
- Compositions (dimensions, frame rate, duration, background color)
- Footage items (dimensions, frame rate, duration, solid/placeholder)
- Layers (flags, quality, sampling/frame-blend modes, source)
- Effect and text **property names and structure**

**Not parsed (yet):**
- Keyframes and animated property **values**
- Transform values, masks, markers

If you need a field that isn't exposed, the `aepdump` tool (below) shows you exactly
where it lives in the binary so it can be added.

## aepdump

A small CLI for inspecting the RIFX chunk tree — useful for debugging and for
reverse-engineering chunks not yet parsed:

```bash
dotnet run --project src/AepSharp.Tools -- template.aep
dotnet run --project src/AepSharp.Tools -- template.aep --format json
```

It prints every chunk with its absolute offset, FourCC, declared vs. consumed size,
a hex+ascii preview, and flags any anomalous (truncated / size-overflowing) chunks.

## Verified versions

Parsing is regression-tested with snapshot baselines over the fixture set inherited
from the upstream project (After Effects circa 2022). It is used against current
After Effects templates in production. If you hit a file that misreads, please open an
issue with `aepdump` output (you can redact payload previews).

## Credits

A port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser)
(MIT). The hard reverse-engineering of the AEP format is theirs; this project brings it
to native .NET. See [their README](https://github.com/boltframe/aftereffects-aep-parser)
for the research notes and Kaitai struct definition.

## License

[MIT](LICENSE) — © 2026 Dave Owen, © 2020 Boltframe.
