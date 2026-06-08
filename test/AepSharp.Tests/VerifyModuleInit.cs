using System.Runtime.CompilerServices;
using VerifyTests;

namespace AepSharp.Tests;

internal static class VerifyModuleInit
{
    [ModuleInitializer]
    public static void Init()
    {
        // Fixtures are fixed bytes, so their content is already deterministic. Keep raw
        // fidelity in snapshots rather than letting Verify rewrite guid-like / date-like
        // substrings it finds in dumped payloads.
        VerifierSettings.DontScrubGuids();
        VerifierSettings.DontScrubDateTimes();

        // AepProject.Items is a flat id->item lookup that re-serializes the entire tree
        // already captured under RootFolder. Ignore it so the model snapshot has one
        // source of truth and a single change produces a single diff.
        VerifierSettings.IgnoreMember<AepProject>(x => x.Items);
    }
}
