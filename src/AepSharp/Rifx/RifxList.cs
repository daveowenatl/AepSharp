namespace AepSharp.Rifx;

internal class RifxList
{
    public string Identifier { get; set; } = "";
    public List<RifxBlock> Blocks { get; set; } = new();

    /// <summary>Absolute byte offset of this list's 4-byte identifier within the stream.</summary>
    public long Offset { get; set; }

    public RifxBlock? FindByType(string type)
    {
        return Blocks.FirstOrDefault(b => b.Type == type);
    }

    public RifxBlock? Find(Func<RifxBlock, bool> predicate)
    {
        return Blocks.FirstOrDefault(predicate);
    }

    public RifxList? SublistFind(string identifier)
    {
        foreach (var block in Blocks)
        {
            if (block.Type == "LIST" && block.Data is RifxList list && list.Identifier == identifier)
                return list;
        }
        return null;
    }

    public List<RifxList> SublistFilter(string identifier)
    {
        var result = new List<RifxList>();
        foreach (var block in Blocks)
        {
            if (block.Type == "LIST" && block.Data is RifxList list && list.Identifier == identifier)
                result.Add(list);
        }
        return result;
    }

    public RifxList SublistMerge(string identifier)
    {
        var merged = new RifxList { Identifier = identifier };
        foreach (var sublist in SublistFilter(identifier))
        {
            merged.Blocks.AddRange(sublist.Blocks);
        }
        return merged;
    }
}
