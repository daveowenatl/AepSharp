using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

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

    internal static AepProperty ParseFromList(RifxList propHead, string matchName)
    {
        var prop = new AepProperty
        {
            MatchName = matchName,
            Name = matchName == "ADBE Effect Parade" ? "Effects" : matchName
        };

        // Parse sub-properties from tdgp groups
        var (tdgpMap, orderedNames) = IndexedGroupToMap(propHead);
        for (int idx = 0; idx < orderedNames.Count; idx++)
        {
            var mn = orderedNames[idx];
            if (tdgpMap.TryGetValue(mn, out var subData))
            {
                var subProp = ParseFromList(subData, mn);
                subProp.Index = (uint)(idx + 1);
                prop.Properties.Add(subProp);
            }
        }

        // Handle effect sub-properties (sspc identifier)
        if (propHead.Identifier == "sspc")
        {
            prop.PropertyType = PropertyType.Group;

            // User-defined effect name
            var fnamBlock = propHead.FindByType("fnam");
            if (fnamBlock != null)
                prop.Name = fnamBlock.ToAsciiString();

            // User-defined label from tdsn
            var tdgpBlock = propHead.SublistFind("tdgp");
            if (tdgpBlock != null)
            {
                var tdsnBlock = tdgpBlock.FindByType("tdsn");
                if (tdsnBlock != null)
                {
                    var label = tdsnBlock.ToAsciiString();
                    if (label != "-_0_/-")
                        prop.Label = label;
                }
            }

            // Parse parT sub-properties
            var parTList = propHead.SublistMerge("parT");
            var (subMatchNames, subPards) = PairMatchNames(parTList);
            for (int idx = 0; idx < subMatchNames.Count; idx++)
            {
                // Skip first pard entry (describes parent)
                if (idx == 0) continue;
                var subProp = ParseFromBlocks(subPards[idx], subMatchNames[idx]);
                subProp.Index = (uint)idx;
                prop.Properties.Add(subProp);
            }
        }

        return prop;
    }

    internal static AepProperty ParseFromBlocks(List<object> entries, string matchName)
    {
        var prop = new AepProperty
        {
            MatchName = matchName,
            Name = matchName
        };

        foreach (var entry in entries)
        {
            if (entry is RifxBlock block)
            {
                switch (block.Type)
                {
                    case "pdnm":
                    {
                        var strContent = block.ToAsciiString();
                        if (prop.PropertyType == PropertyType.Select)
                        {
                            prop.SelectOptions = new List<string>(strContent.Split('|'));
                        }
                        else if (!string.IsNullOrEmpty(strContent))
                        {
                            prop.Name = strContent;
                        }
                        break;
                    }
                    case "pard":
                    {
                        var data = block.GetBytes();
                        var typeValue = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(14));
                        prop.PropertyType = typeValue == 0x0a
                            ? PropertyType.OneD
                            : (PropertyType)typeValue;

                        var pardName = Encoding.UTF8.GetString(data, 16, 32).TrimEnd('\0');
                        if (!string.IsNullOrEmpty(pardName))
                            prop.Name = pardName;
                        break;
                    }
                }
            }
        }

        return prop;
    }

    internal static (List<string> matchNames, List<List<object>> data) PairMatchNames(RifxList head)
    {
        var matchNames = new List<string>();
        var datum = new List<List<object>>();

        if (head == null) return (matchNames, datum);

        int groupIdx = -1;
        bool skip = false;

        foreach (var block in head.Blocks)
        {
            if (block.Type == "tdmn")
            {
                var mn = Encoding.UTF8.GetString(((byte[])block.Data)).TrimEnd('\0');
                if (mn is "ADBE Group End" or "ADBE Effect Built In Params")
                {
                    skip = true;
                    continue;
                }
                matchNames.Add(mn);
                skip = false;
                groupIdx++;
            }
            else if (groupIdx >= 0 && !skip)
            {
                while (datum.Count <= groupIdx)
                    datum.Add(new List<object>());

                if (block.Data is RifxList list)
                    datum[groupIdx].Add(list);
                else
                    datum[groupIdx].Add(block);
            }
        }

        return (matchNames, datum);
    }

    internal static (Dictionary<string, RifxList> map, List<string> orderedNames) IndexedGroupToMap(RifxList tdgpHead)
    {
        var map = new Dictionary<string, RifxList>();
        var (matchNames, contents) = PairMatchNames(tdgpHead);
        var orderedNames = new List<string>();

        for (int i = 0; i < matchNames.Count; i++)
        {
            if (i < contents.Count && contents[i].Count > 0 && contents[i][0] is RifxList list)
            {
                map[matchNames[i]] = list;
                orderedNames.Add(matchNames[i]);
            }
        }

        return (map, orderedNames);
    }
}
