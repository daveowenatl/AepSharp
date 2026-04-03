# AepSharp

C# parser for Adobe After Effects .aep project files. Port of [boltframe/aftereffects-aep-parser](https://github.com/boltframe/aftereffects-aep-parser).

## Build & Test

- Build: `dotnet build`
- Test: `dotnet test`
- Format: `dotnet format --no-restore`

## Architecture

- `src/AepSharp/Rifx/` — internal RIFX (big-endian RIFF) binary reader
- `src/AepSharp/` — public AEP model classes and parsing logic
- `test/AepSharp.Tests/data/` — .aep test fixtures from the Go repo
