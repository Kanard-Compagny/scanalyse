using Scanalyse.Core.Gs1;

namespace Scanalyse.Core.Tests;

public class Gs1Tests
{
    [Theory]
    [InlineData("4006381333931", true)]
    [InlineData("4006381333932", false)]
    [InlineData("03453120000011", true)]
    [InlineData("12345678", false)]
    [InlineData("12345670", true)]
    [InlineData("ABC", false)]
    public void CheckDigit_Validates(string number, bool valid) => Assert.Equal(valid, CheckDigit.IsValid(number));

    [Fact]
    public void CheckDigit_Computes() => Assert.Equal(1, CheckDigit.Compute("400638133393"));

    [Fact]
    public void Parse_RawWithGroupSeparators()
    {
        var msg = Gs1Parser.Parse("0103453120000011172712311010ABC123\u001D21SN7788", FixedClock.October2026);

        Assert.NotNull(msg);
        Assert.Equal(Gs1Notation.Raw, msg.Notation);
        Assert.Equal(new[] { "01", "17", "10", "21" }, msg.Elements.Select(e => e.Ai));
        Assert.Equal("10ABC123", msg.Get("10")!.Value);
        Assert.Equal("SN7788", msg.Get("21")!.Value);
        Assert.Equal(new DateOnly(2027, 12, 31), msg.Get("17")!.Date);
        Assert.True(msg.IsValid);
    }

    [Fact]
    public void Parse_AcceptsAimPrefixAndVisualNotation()
    {
        var msg = Gs1Parser.Parse("]d20109506000134352<GS>10LOT42");

        Assert.NotNull(msg);
        Assert.Equal("09506000134352", msg.Get("01")!.Value);
        Assert.Equal("LOT42", msg.Get("10")!.Value);
    }

    [Fact]
    public void Parse_Parenthesized()
    {
        var msg = Gs1Parser.Parse("(01)09506000134352(3103)001250(10)ABC", FixedClock.October2026);

        Assert.NotNull(msg);
        Assert.Equal(Gs1Notation.Parenthesized, msg.Notation);
        Assert.Equal("1,250 kg", msg.Get("3103")!.Display);
    }

    [Fact]
    public void Parse_FlagsInvalidGtin()
    {
        var msg = Gs1Parser.Parse("(01)09506000134353");

        Assert.NotNull(msg);
        Assert.False(msg.Get("01")!.IsValid);
    }

    [Fact]
    public void Parse_ReturnsNullForPlainText() => Assert.Null(Gs1Parser.Parse("Bonjour"));

    [Theory]
    [InlineData("271231", 2027, 12, 31, false)]
    [InlineData("270200", 2027, 2, 28, true)]
    [InlineData("990101", 1999, 1, 1, false)]
    [InlineData("760101", 2076, 1, 1, false)]
    public void Date_UsesGs1SlidingWindow(string value, int y, int m, int d, bool endOfMonth)
    {
        Assert.True(Gs1Parser.TryParseDate(value, FixedClock.October2026, out var date, out var eom));
        Assert.Equal(new DateOnly(y, m, d), date);
        Assert.Equal(endOfMonth, eom);
    }

    [Fact]
    public void Date_RejectsInvalidMonth() => Assert.False(Gs1Parser.TryParseDate("271301", FixedClock.October2026, out _, out _));

    [Fact]
    public void DigitalLink_ExtractsPathAndQuery()
    {
        var msg = DigitalLink.TryParse(new Uri("https://id.example.com/01/09506000134352/10/AB%2012?17=271231"), FixedClock.October2026);

        Assert.NotNull(msg);
        Assert.Equal(new[] { "01", "10", "17" }, msg.Elements.Select(e => e.Ai));
        Assert.Equal("AB 12", msg.Get("10")!.Value);
    }

    [Fact]
    public void DigitalLink_IgnoresOrdinaryLinks() => Assert.Null(DigitalLink.TryParse(new Uri("https://www.example.com/produits/01")));

    [Theory]
    [InlineData("3001234567890", "France et Monaco")]
    [InlineData("4006381333931", "Allemagne")]
    [InlineData("9781234567897", "ISBN (livres) / ISMN (partitions)")]
    [InlineData("2001234000004", "usage interne (poids ou prix variable, codes magasin)")]
    public void Prefix_Lookup(string gtin, string expected) => Assert.Equal(expected, Gs1Prefixes.Lookup(gtin));

    [Fact]
    public void ToTransmitted_AddsSeparatorsOnlyAfterVariableFields()
    {
        var msg = Gs1Parser.Parse("(01)03453120000011(17)271231(10)LOT(21)SN", FixedClock.October2026)!;

        Assert.Equal("01034531200000111727123110LOT\u001D21SN", msg.ToTransmitted());
    }
}
