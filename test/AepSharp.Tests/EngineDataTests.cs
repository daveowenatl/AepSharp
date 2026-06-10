using System.Text;
using Xunit;

namespace AepSharp.Tests;

/// <summary>
/// Unit tests for the EngineData display-text extractor. These synthesize the
/// "( FE FF &lt;UTF-16BE&gt; )" string segments directly, so they exercise the
/// decoder logic without needing a real .aep fixture. End-to-end validation
/// against a real file's 28 text layers happens in Phase 3 (TestConsole) and
/// the snapshot tests.
/// </summary>
public class EngineDataTests
{
    /// <summary>A "( FE FF &lt;UTF-16BE&gt; )" string segment — the real format.</summary>
    private static byte[] Seg(string s)
    {
        var bytes = new List<byte> { (byte)'(', 0xFE, 0xFF };
        bytes.AddRange(Encoding.BigEndianUnicode.GetBytes(s));
        bytes.Add((byte)')');
        return bytes.ToArray();
    }

    /// <summary>A parenthesized string WITHOUT the BOM — should be ignored.</summary>
    private static byte[] NoBomSeg(string s)
    {
        var bytes = new List<byte> { (byte)'(' };
        bytes.AddRange(Encoding.BigEndianUnicode.GetBytes(s));
        bytes.Add((byte)')');
        return bytes.ToArray();
    }

    /// <summary>Joins segments with dict-like filler, mimicking a real blob.</summary>
    private static string Extract(params byte[][] segments)
    {
        var blob = new List<byte>();
        foreach (var s in segments)
        {
            blob.AddRange(Encoding.ASCII.GetBytes(" /0 "));
            blob.AddRange(s);
        }
        return EngineData.ExtractDisplayText(blob.ToArray());
    }

    [Fact]
    public void ExtractsASingleString() =>
        Assert.Equal("Hello World", Extract(Seg("Hello World")));

    [Fact]
    public void ReturnsTheLastMeaningfulString() =>
        Assert.Equal("Second", Extract(Seg("First"), Seg("Second")));

    [Fact]
    public void IgnoresTrailingPunctuationTokens() =>
        // real docs have the run text followed by stray kinsoku punctuation
        Assert.Equal("Real Copy", Extract(Seg("Real Copy"), Seg("—‥…"), Seg("’")));

    [Fact]
    public void RequiresTheUtf16Bom() =>
        Assert.Equal("Yes", Extract(NoBomSeg("no bom here"), Seg("Yes")));

    [Fact]
    public void NonBomStringsAloneYieldEmpty() =>
        Assert.Equal("", Extract(NoBomSeg("ignored")));

    [Fact]
    public void FiltersPostScriptFontNames() =>
        Assert.Equal("", Extract(Seg("Heebo-ExtraBold"), Seg("MyriadPro-Regular")));

    [Fact]
    public void FiltersFontVersionStrings() =>
        Assert.Equal("", Extract(Seg("Version 2.002; hotconv 1.0.81; makeotf.lib")));

    [Fact]
    public void FiltersCjkKinsokuTables() =>
        Assert.Equal("", Extract(Seg("、。，．・")));

    [Fact]
    public void FiltersLoneDigitArtifacts() =>
        Assert.Equal("", Extract(Seg("3")));

    [Fact]
    public void FiltersOptionNames() =>
        Assert.Equal("", Extract(Seg("Hard"), Seg("Soft"), Seg("Normal RGB")));

    [Fact]
    public void KeepsDollarSign() =>
        Assert.Equal("$", Extract(Seg("$")));

    [Fact]
    public void KeepsPercentSign() =>
        Assert.Equal("%", Extract(Seg("%")));

    [Fact]
    public void KeepsMultiDigitNumbers() =>
        Assert.Equal("409", Extract(Seg("409")));

    [Fact]
    public void EmptyRunYieldsEmptyString() =>
        Assert.Equal("", Extract(Seg("")));

    [Fact]
    public void CarriageReturnOnlyRunYieldsEmptyString() =>
        Assert.Equal("", Extract(Seg("\r")));

    [Fact]
    public void StripsTrailingCarriageReturn() =>
        Assert.Equal("2026 RAM", Extract(Seg("2026 RAM\r")));

    [Fact]
    public void EmptyInputYieldsEmptyString() =>
        Assert.Equal("", EngineData.ExtractDisplayText(Array.Empty<byte>()));

    [Fact]
    public void HonorsBackslashEscapedCloseParen()
    {
        // "( FE FF 00 41 00 5C 29 00 42 )" => UTF-16BE "A)B" — the escaped ')'
        // must not terminate the string early, and the byte un-escape re-aligns
        // the UTF-16 pairs.
        byte[] seg = { (byte)'(', 0xFE, 0xFF, 0x00, 0x41, 0x00, 0x5C, 0x29, 0x00, 0x42, (byte)')' };
        Assert.Equal("A)B", EngineData.ExtractDisplayText(seg));
    }

    [Fact]
    public void RealisticDocumentReturnsRunText() =>
        // font table + version + options, then the real run text, then a stray quote
        Assert.Equal("Lease For", Extract(
            Seg("Heebo-Bold"), Seg("Version 2.0; hotconv"), Seg("Hard"), Seg("Soft"),
            Seg("Normal RGB"), Seg("Lease For"), Seg("’")));

    [Fact]
    public void EmptyFieldAmongNoiseReturnsEmpty() =>
        Assert.Equal("", Extract(
            Seg("Heebo-Bold"), Seg("Normal RGB"), Seg("3"), Seg("’")));

    // --- corpus-surfaced leak filters (N1/N2) ---

    [Fact]
    public void FiltersNonHyphenatedFontTokens() =>
        Assert.Equal("", Extract(Seg("ArialMT"), Seg("TimesNewRomanPSMT"), Seg("GrotaSansBook_91628")));

    [Fact]
    public void FiltersHexColorCodes() =>
        Assert.Equal("", Extract(Seg("000000"), Seg("ffffff"), Seg("00FFF6"), Seg("24FF00")));

    [Fact]
    public void DoesNotFilterAllLetterHexWords() =>
        // "FACADE"/"DECADE" are all hex letters but real words — must survive
        Assert.Equal("DECADE", Extract(Seg("FACADE"), Seg("DECADE")));

    [Fact]
    public void KeepsRealSingleTokenCopy() =>
        // dealer URLs / model names the corpus showed must NOT be filtered
        Assert.Equal("BMWofPeoria.com", Extract(Seg("EQUINOX"), Seg("BMWofPeoria.com")));
}
