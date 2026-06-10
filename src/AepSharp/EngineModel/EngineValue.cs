namespace AepSharp.EngineModel;

/// <summary>
/// A node in a parsed EngineData (Adobe CoolType / PostScript-COS) document.
/// EngineData is the serialization After Effects and Photoshop use for text
/// layers: nested dictionaries (<c>&lt;&lt; &gt;&gt;</c>), arrays (<c>[ ]</c>),
/// names (<c>/key</c>), strings (<c>( … )</c>), numbers, and booleans.
/// </summary>
internal abstract class EngineValue
{
}

/// <summary>An ordered dictionary. Keys are name tokens without the leading slash.</summary>
internal sealed class EngineDict : EngineValue
{
    public List<KeyValuePair<string, EngineValue>> Entries { get; } = new();

    public EngineValue? Get(string key)
    {
        foreach (var entry in Entries)
            if (entry.Key == key)
                return entry.Value;
        return null;
    }

    public bool TryGet(string key, out EngineValue value)
    {
        var found = Get(key);
        value = found!;
        return found is not null;
    }
}

internal sealed class EngineArray : EngineValue
{
    public List<EngineValue> Items { get; } = new();
}

internal sealed class EngineString : EngineValue
{
    public string Value { get; init; } = "";
}

internal sealed class EngineNumber : EngineValue
{
    public double Value { get; init; }
}

internal sealed class EngineBoolean : EngineValue
{
    public bool Value { get; init; }
}

/// <summary>A name used as a value, e.g. <c>/CoolTypeFont</c> or <c>/nil</c> (null).</summary>
internal sealed class EngineName : EngineValue
{
    public string Value { get; init; } = "";

    public bool IsNil => Value == "nil";
}
