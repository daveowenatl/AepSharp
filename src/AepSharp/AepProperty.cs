namespace AepSharp;

public class AepProperty
{
    public string MatchName { get; internal set; } = "";
    public string Name { get; internal set; } = "";
    public string Label { get; internal set; } = "";
    public uint Index { get; internal set; }
    public PropertyType PropertyType { get; internal set; } = PropertyType.Custom;
    public List<AepProperty> Properties { get; internal set; } = new();
    public List<string> SelectOptions { get; internal set; } = new();
}
