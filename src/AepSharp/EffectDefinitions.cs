using System.Text;
using AepSharp.Rifx;

namespace AepSharp;

/// <summary>
/// Effect parameter definitions (<c>parT</c> lists) by effect match name. After Effects
/// stores a complete definition for every effect type used in a project in the root
/// <c>EfdG</c> list, and writes an empty <c>parT</c> on repeat instances of an effect;
/// those instances resolve their parameters from here. Behaviour follows py-aep (MIT):
/// project definitions first, then the first non-empty instance seen.
/// </summary>
internal sealed class EffectDefinitions
{
    private readonly Dictionary<string, RifxList> _byMatchName = new();

    internal static EffectDefinitions FromProject(RifxList root)
    {
        var definitions = new EffectDefinitions();
        var efdg = root.SublistFind("EfdG");
        if (efdg is null)
            return definitions;

        foreach (var efdf in efdg.SublistFilter("EfDf"))
        {
            var tdmn = efdf.FindByType("tdmn");
            var parT = efdf.SublistFind("sspc")?.SublistMerge("parT");
            if (tdmn?.Data is not byte[] nameBytes || parT is null || !HasParameters(parT))
                continue;
            var matchName = Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
            definitions._byMatchName.TryAdd(matchName, parT);
        }
        return definitions;
    }

    // An empty parT may still hold its parn (parameter count) block.
    private static bool HasParameters(RifxList parT) => parT.Blocks.Any(b => b.Type == "tdmn");

    /// <summary>
    /// Returns the instance's own <paramref name="parT"/> when it has one (remembering it
    /// for later instances), otherwise the known definition for the effect, if any.
    /// </summary>
    internal RifxList Resolve(string effectMatchName, RifxList parT)
    {
        if (HasParameters(parT))
        {
            _byMatchName.TryAdd(effectMatchName, parT);
            return parT;
        }
        return _byMatchName.TryGetValue(effectMatchName, out var known) ? known : parT;
    }
}
