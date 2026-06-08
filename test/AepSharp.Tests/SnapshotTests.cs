using AepSharp.Rifx;
using static VerifyXunit.Verifier;

namespace AepSharp.Tests;

/// <summary>
/// Snapshot regression tests over every fixture. Two snapshots per file:
///   * the raw RIFX chunk tree (offsets, sizes, anomalies) — catches any change in how
///     the binary is walked;
///   * the parsed semantic model — catches any change in what we expose to consumers.
/// Together they are the parity oracle: a regression shows up as a readable git diff in
/// the committed .verified files.
/// </summary>
public class SnapshotTests
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return ["BPC-8.aep"];
        yield return ["BPC-16.aep"];
        yield return ["BPC-32.aep"];
        yield return ["ExEn-es.aep"];
        yield return ["ExEn-js.aep"];
        yield return ["Item-01.aep"];
        yield return ["Layer-01.aep"];
        yield return ["Property-01.aep"];
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public Task RifxTree(string fixture)
    {
        using var stream = File.OpenRead(Path.Combine("data", fixture));
        var root = RifxReader.FromStream(stream);
        return Verify(RifxDump.ToText(root))
            .UseDirectory("Snapshots")
            .UseFileName($"rifx-{fixture}");
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public Task ParsedModel(string fixture)
    {
        var project = AepProject.Open(Path.Combine("data", fixture));
        return Verify(project)
            .UseDirectory("Snapshots")
            .UseFileName($"model-{fixture}");
    }
}
