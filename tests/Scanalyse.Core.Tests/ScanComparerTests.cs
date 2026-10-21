using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;

namespace Scanalyse.Core.Tests;

public class ScanComparerTests
{
    private static string MainCode(string expected, string received) => ScanComparer.Compare(expected, received).Findings[0].Code;

    [Fact]
    public void Identical()
    {
        var r = ScanComparer.Compare("ABC123", "ABC123");

        Assert.True(r.Identical);
        Assert.Equal(Severity.Ok, r.WorstSeverity);
    }

    [Fact]
    public void NothingReceived() => Assert.Equal("NOTHING_RECEIVED", MainCode("ABC", string.Empty));

    [Fact]
    public void EnterSuffixIsOnlyInformative()
    {
        var r = ScanComparer.Compare("ABC123", "ABC123\r\n");

        Assert.True(r.DataMatches);
        Assert.Equal("SUFFIX", r.Findings[0].Code);
        Assert.Equal(Severity.Info, r.WorstSeverity);
    }

    [Fact]
    public void AimPrefixAndSuffix()
    {
        var r = ScanComparer.Compare("0109506000134352", "]C10109506000134352<CR>");

        Assert.True(r.DataMatches);
        Assert.Equal(new[] { "AIM_PREFIX", "SUFFIX" }, r.Findings.Select(f => f.Code));
    }

    [Theory]
    [InlineData("3001234567892", "\"àà&é\"'(-è_çé")]
    [InlineData("LOT-A12", "LOT)Q&é")]
    [InlineData("MAZ", "?QW")]
    public void ScannerUsOnFrenchPc(string expected, string received) => Assert.Equal("KEYBOARD_US_ON_FR", MainCode(expected, received));

    [Fact]
    public void ScannerFrOnUsPc() => Assert.Equal("KEYBOARD_FR_ON_US", MainCode("123", "!@#"));

    [Fact]
    public void GroupSeparatorLost() =>
        Assert.Equal("GS_LOST", MainCode("0103453120000011<GS>10LOT<GS>21SN", "010345312000001110LOT21SN"));

    [Fact]
    public void GroupSeparatorReplaced()
    {
        var r = ScanComparer.Compare("0103453120000011\u001D10LOT\u001D21SN", "0103453120000011~10LOT~21SN");

        Assert.Equal("GS_REPLACED", r.Findings[0].Code);
        Assert.Contains("« ~ »", r.Findings[0].Title);
        Assert.True(r.DataMatches);
    }

    [Fact]
    public void CheckDigitDropped() => Assert.Equal("CHECK_DIGIT_DROPPED", MainCode("3001234567892", "300123456789"));

    [Fact]
    public void UpcAToEan13() => Assert.Equal("UPCA_TO_EAN13", MainCode("036000291452", "0036000291452"));

    [Fact]
    public void Ean13ToUpcA() => Assert.Equal("EAN13_TO_UPCA", MainCode("0036000291452", "036000291452"));

    [Fact]
    public void LeadingZerosRemoved() => Assert.Equal("LEADING_ZEROS_REMOVED", MainCode("000123", "123"));

    [Fact]
    public void Code39StartStop() => Assert.Equal("CODE39_START_STOP", MainCode("ABC-1", "*ABC-1*"));

    [Fact]
    public void DoubleRead() => Assert.Equal("DOUBLE_READ", MainCode("ABC", "ABCABC"));

    [Fact]
    public void Truncated()
    {
        var f = ScanComparer.Compare("ABCDEFGHIJ", "ABCDEF").Findings[0];

        Assert.Equal("TRUNCATED", f.Code);
        Assert.Contains("6 caractères reçus sur 10", f.Title);
    }

    [Fact]
    public void CaseChanged() => Assert.Equal("CASE_CHANGED", MainCode("Lot-a1", "LOT-A1"));

    [Fact]
    public void PrefixAndSuffixAroundData() => Assert.Equal("PREFIX_SUFFIX", MainCode("12345", "#12345#"));

    [Fact]
    public void Encoding() => Assert.Equal("ENCODING", MainCode("Crème", "Cr?me"));

    [Fact]
    public void GenericDifferenceGivesPosition()
    {
        var f = ScanComparer.Compare("ABCDEF", "ABXDEF").Findings[0];

        Assert.Equal("DIFFERENT", f.Code);
        Assert.Contains("position 3", f.Explanation);
    }

    [Fact]
    public void ReadableGs1IsComparedWithTransmittedForm()
    {
        var r = ScanComparer.Compare("(01)03453120000011(10)LOT(21)SN", "0103453120000011" + "10LOT\u001D21SN");

        Assert.True(r.Identical);
    }

    [Fact]
    public void ReadableGs1DetectsLostSeparator() =>
        Assert.Equal("GS_LOST", MainCode("(01)03453120000011(10)LOT(21)SN", "010345312000001110LOT21SN"));
}
