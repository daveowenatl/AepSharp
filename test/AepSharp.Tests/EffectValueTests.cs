using System.Buffers.Binary;
using System.Text;
using AepSharp.Rifx;

namespace AepSharp.Tests;

/// <summary>
/// Effect parameter values. An effect's sspc list carries its parameter definitions
/// (parT: tdmn + pard per parameter) and its current values (tdgp: tdmn + tdbs per
/// parameter that differs from the definition). Parameters without a tdbs keep the
/// last value recorded in their pard. Repeat instances of an effect may carry an empty
/// parT and resolve definitions from the project's EfdG list. Layouts per py-aep.
/// </summary>
public class EffectValueTests
{
    private const uint TimeBase = 1000;

    // ---- builders -------------------------------------------------------------

    private static RifxBlock Block(string type, byte[] data) => new() { Type = type, Size = (uint)data.Length, Data = data };

    private static RifxBlock ListBlock(RifxList list) => new() { Type = "LIST", Data = list };

    private static RifxList List(string identifier, params RifxBlock[] blocks)
    {
        var list = new RifxList { Identifier = identifier };
        list.Blocks.AddRange(blocks);
        return list;
    }

    private static RifxBlock Tdmn(string matchName)
    {
        var bytes = new byte[40];
        Encoding.ASCII.GetBytes(matchName).CopyTo(bytes, 0);
        return Block("tdmn", bytes);
    }

    private static RifxBlock Utf8Block(string type, string text)
    {
        var payload = Encoding.UTF8.GetBytes(text);
        var bytes = new byte[8 + payload.Length];
        Encoding.ASCII.GetBytes("Utf8").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(bytes, 8);
        return Block(type, bytes);
    }

    /// <summary>A 148-byte pard with the control type at 15, name at 16 and body at 56.</summary>
    private static RifxBlock Pard(byte controlType, string name, Action<Span<byte>>? body = null)
    {
        var pard = new byte[148];
        pard[15] = controlType;
        Encoding.UTF8.GetBytes(name).CopyTo(pard, 16);
        body?.Invoke(pard.AsSpan(56));
        return Block("pard", pard);
    }

    private static RifxBlock SliderPard(string name, double lastValue) =>
        Pard(10, name, b => BinaryPrimitives.WriteDoubleBigEndian(b, lastValue));

    private static RifxList StaticTdbs(params double[] value)
    {
        var tdb4 = new byte[124];
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), (ushort)value.Length);
        var cdat = new byte[Math.Max(5, value.Length) * 8];
        for (var i = 0; i < value.Length; i++)
            BinaryPrimitives.WriteDoubleBigEndian(cdat.AsSpan(i * 8), value[i]);
        return List("tdbs", Block("tdb4", tdb4), Block("cdat", cdat));
    }

    /// <summary>A 1D linear-keyframed tdbs: (seconds, value) pairs.</summary>
    private static RifxList AnimatedTdbs(params (double time, double value)[] keys)
    {
        var tdb4 = new byte[124];
        BinaryPrimitives.WriteUInt16BigEndian(tdb4.AsSpan(2), 1);
        const int itemSize = 8 + 5 * 8;
        var ldat = new byte[keys.Length * itemSize];
        for (var i = 0; i < keys.Length; i++)
        {
            var item = ldat.AsSpan(i * itemSize, itemSize);
            BinaryPrimitives.WriteInt32BigEndian(item, (int)Math.Round(keys[i].time * TimeBase));
            item[4] = 1;
            item[5] = 1;
            BinaryPrimitives.WriteDoubleBigEndian(item[8..], keys[i].value);
            BinaryPrimitives.WriteDoubleBigEndian(item[24..], 1.0 / 6);
            BinaryPrimitives.WriteDoubleBigEndian(item[40..], 1.0 / 6);
        }
        var lhd3 = new byte[52];
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(10), (ushort)keys.Length);
        BinaryPrimitives.WriteUInt16BigEndian(lhd3.AsSpan(18), itemSize);
        var keyList = List("list", Block("lhd3", lhd3), Block("ldat", ldat));
        return List("tdbs", Block("tdb4", tdb4), ListBlock(keyList));
    }

    private sealed record Param(string MatchName, RifxBlock Pard, RifxList? Tdbs = null);

    private static RifxList Sspc(string effectMatchName, string displayName, bool withParT, params Param[] parameters) =>
        Sspc(effectMatchName, displayName, displayName, withParT, parameters);

    private static RifxList Sspc(string effectMatchName, string fnam, string displayName, bool withParT, params Param[] parameters)
    {
        var sspc = List("sspc", Utf8Block("fnam", fnam));

        var parT = List("parT", Block("parn", [0, 0, 0, (byte)parameters.Length]));
        if (withParT)
        {
            parT.Blocks.Add(Tdmn(effectMatchName));
            parT.Blocks.Add(Pard(0, ""));
            foreach (var p in parameters)
            {
                parT.Blocks.Add(Tdmn(p.MatchName));
                parT.Blocks.Add(p.Pard);
            }
        }
        sspc.Blocks.Add(ListBlock(parT));

        var tdgp = List("tdgp", Utf8Block("tdsn", displayName));
        foreach (var p in parameters.Where(p => p.Tdbs is not null))
        {
            tdgp.Blocks.Add(Tdmn(p.MatchName));
            tdgp.Blocks.Add(ListBlock(p.Tdbs!));
        }
        tdgp.Blocks.Add(Tdmn("ADBE Group End"));
        sspc.Blocks.Add(ListBlock(tdgp));
        return sspc;
    }

    private static RifxList EffectParade(params (string matchName, RifxList sspc)[] effects)
    {
        var parade = List("tdgp");
        foreach (var (matchName, sspc) in effects)
        {
            parade.Blocks.Add(Tdmn(matchName));
            parade.Blocks.Add(ListBlock(sspc));
        }
        return parade;
    }

    private static AepLayer Layer(RifxList parade)
    {
        var ldta = new byte[164];
        BinaryPrimitives.WriteUInt32BigEndian(ldta.AsSpan(32), TimeBase);
        var root = List("tdgp", Tdmn("ADBE Effect Parade"), ListBlock(parade));
        var layer = List("Layr", Block("ldta", ldta), ListBlock(root));
        return AepLayer.Parse(layer, null!, TimeBase);
    }

    private const string Resize = "Pseudo/0.9214685757178813";

    // ---- tests ----------------------------------------------------------------

    [Fact]
    public void ReadsStoredValueAndFallsBackToThePardLastValue()
    {
        var layer = Layer(EffectParade((Resize, Sspc(Resize, "Single Line Resize", true,
            new Param($"{Resize}-0001", SliderPard("Maximum width", 470), StaticTdbs(1650)),
            new Param($"{Resize}-0002", SliderPard("Apply at (second)", 0.25))))));

        var effect = Assert.Single(layer.Effects);
        Assert.Equal("Single Line Resize", effect.Name);
        Assert.Equal(new[] { 1650.0 }, effect.Properties.First(p => p.Name == "Maximum width").Value);
        Assert.Equal(new[] { 0.25 }, effect.Properties.First(p => p.Name == "Apply at (second)").Value);
    }

    [Fact]
    public void PseudoEffectIsNamedByItsLabelAndParameterNamesStopAtNul()
    {
        // Pseudo effects have an empty fnam; the panel name is the tdsn label. Pard names
        // can carry stale bytes after the terminator.
        var layer = Layer(EffectParade((Resize, Sspc(Resize, fnam: "", displayName: "Single Line Resize", withParT: true,
            new Param($"{Resize}-0001", SliderPard("Opacity\0olor", 0))))));

        Assert.Equal("Single Line Resize", layer.Effects[0].Name);
        Assert.Equal("Opacity", layer.Effects[0].Properties[0].Name);
    }

    [Fact]
    public void RepeatedEffectInstancesKeepTheirOwnValues()
    {
        // Regression: instances were looked up by match name, so every "ADBE Slider
        // Control" on a layer resolved to the last one's list.
        const string slider = "ADBE Slider Control";
        var layer = Layer(EffectParade(
            (slider, Sspc(slider, "Speed", true, new Param($"{slider}-0001", SliderPard("Slider", 0), StaticTdbs(88)))),
            (slider, Sspc(slider, "Delay", true, new Param($"{slider}-0001", SliderPard("Slider", 0), StaticTdbs(14)))),
            (slider, Sspc(slider, "Width", true, new Param($"{slider}-0001", SliderPard("Slider", 0), StaticTdbs(1524))))));

        Assert.Equal(new[] { "Speed", "Delay", "Width" }, layer.Effects.Select(e => e.Name));
        Assert.Equal(new uint[] { 1, 2, 3 }, layer.Effects.Select(e => e.Index));
        Assert.Equal(new[] { 88.0, 14.0, 1524.0 }, layer.Effects.Select(e => e.Properties[0].Value![0]));
    }

    [Fact]
    public void AnimatedParameterExposesKeyframesInLayerTime()
    {
        const string slider = "ADBE Slider Control";
        var layer = Layer(EffectParade((slider, Sspc(slider, "Slider Control", true,
            new Param($"{slider}-0001", SliderPard("Slider", 0), AnimatedTdbs((1.8, 258), (2.1, 0)))))));

        var parameter = layer.Effects[0].Properties[0];
        Assert.True(parameter.IsAnimated);
        Assert.Null(parameter.Value);
        Assert.Equal(new[] { 1.8, 2.1 }, parameter.Keyframes.Select(k => Math.Round(k.Time, 9)));
        Assert.Equal(129.0, parameter.ValueAtTime(1.95)![0], 6);
    }

    [Fact]
    public void RepeatInstanceWithEmptyParTUsesProjectDefinitions()
    {
        const string slider = "ADBE Slider Control";
        var definition = Sspc(slider, "", true, new Param($"{slider}-0001", SliderPard("Slider", 7)));
        var root = List("RIFX", ListBlock(List("EfdG", ListBlock(List("EfDf", Tdmn(slider), ListBlock(definition))))));
        var definitions = EffectDefinitions.FromProject(root);

        var parade = EffectParade(
            (slider, Sspc(slider, "Stored", false, new Param($"{slider}-0001", SliderPard("unused", 0), StaticTdbs(42)))),
            (slider, Sspc(slider, "Default", false, new Param($"{slider}-0001", SliderPard("unused", 0)))));
        var effects = AepProperty.ParseFromList(parade, "ADBE Effect Parade", definitions).Properties;

        Assert.All(effects, e => Assert.Equal("Slider", Assert.Single(e.Properties).Name));
        Assert.Equal(new[] { 42.0 }, effects[0].Properties[0].Value);
        Assert.Equal(new[] { 7.0 }, effects[1].Properties[0].Value);
    }

    [Fact]
    public void EmptyParTWithoutDefinitionsHasNoParameters()
    {
        const string slider = "ADBE Slider Control";
        var effects = AepProperty.ParseFromList(
            EffectParade((slider, Sspc(slider, "Lonely", false, new Param($"{slider}-0001", SliderPard("x", 0), StaticTdbs(1))))),
            "ADBE Effect Parade", new EffectDefinitions()).Properties;

        Assert.Empty(Assert.Single(effects).Properties);
    }

    [Fact]
    public void LayerSelectExposesTheReferencedLayerId()
    {
        const string control = "ADBE Layer Control";
        var tdbs = StaticTdbs(0);
        tdbs.Blocks.Add(Block("tdpi", [0, 0, 0x38, 0x49]));
        var layer = Layer(EffectParade((control, Sspc(control, "Layer Control", true,
            new Param($"{control}-0001", Pard(0, "Layer"), tdbs)))));

        var parameter = layer.Effects[0].Properties[0];
        Assert.Equal(PropertyType.LayerSelect, parameter.PropertyType);
        Assert.Equal(14409u, parameter.LayerReferenceId);
        Assert.Null(parameter.Value);
    }

    [Fact]
    public void DecodesPardLastValuesInStoredUnits()
    {
        static double[]? Decode(byte type, Action<Span<byte>> body) =>
            AepProperty.DecodePardValue((byte[])Pard(type, "p", body).Data)?.ToArray();

        Assert.Equal(new[] { -3.0 }, Decode(1, b => BinaryPrimitives.WriteInt32BigEndian(b, -3)));
        Assert.Equal(new[] { 1.5 }, Decode(2, b => BinaryPrimitives.WriteInt32BigEndian(b, 0x18000)));
        Assert.Equal(new[] { -90.0 }, Decode(3, b => BinaryPrimitives.WriteInt32BigEndian(b, -90 * 65536)));
        Assert.Equal(new[] { 1.0 }, Decode(4, b => b[4] = 1));
        Assert.Equal(new[] { 255.0, 194, 0, 0 }, Decode(5, b => { b[0] = 255; b[1] = 194; }));
        Assert.Equal(new[] { 0.5, 0.25 }, Decode(6, b =>
        {
            BinaryPrimitives.WriteInt32BigEndian(b, 256 * 128);
            BinaryPrimitives.WriteInt32BigEndian(b[4..], 128 * 128);
        }));
        Assert.Equal(new[] { 2.0 }, Decode(7, b => BinaryPrimitives.WriteUInt32BigEndian(b, 2)));
        Assert.Equal(new[] { 1650.0 }, Decode(10, b => BinaryPrimitives.WriteDoubleBigEndian(b, 1650)));
        Assert.Null(Decode(0, _ => { }));   // layer
        Assert.Null(Decode(12, _ => { }));  // mask/path
        Assert.Null(Decode(18, _ => { }));  // 3D point
        Assert.Null(AepProperty.DecodePardValue(new byte[20]));
    }

    [Fact]
    public void FixtureExpressionControlsExposeTheirValues()
    {
        // Values cross-checked with py-aep 0.16.0 (which reports colours as RGBA 0-1 and
        // points in pixels: [960, 540] for this 1920x1080 comp).
        var project = AepProject.Open("data/Property-01.aep");
        var controls = project.RootFolder.FolderContents[0].CompositionLayers[0];

        Assert.Equal(new[] { 0.0 }, controls.Effects[0].Properties[0].Value);        // checkbox
        Assert.Equal(new[] { 0.0 }, controls.Effects[1].Properties[0].Value);        // slider
        Assert.Equal(new[] { 0.5, 0.5 }, controls.Effects[2].Properties[0].Value);   // point (fractions)
        Assert.Null(controls.Effects[3].Properties[0].Value);                         // 3D point: not decoded
        Assert.Equal(new[] { 255.0, 255, 0, 0 }, controls.Effects[4].Properties[0].Value); // ARGB red
        Assert.Equal(new[] { 0.0 }, controls.Effects[5].Properties[0].Value);        // angle
        Assert.Equal(controls.Id, controls.Effects[6].Properties[0].LayerReferenceId);
    }
}
