using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

public class ZplReaderTests
{
    private const string Palette = """
        ^XA
        ^POI
        ^FT165,1162^A0B,26,18^FDExpediteur :^FS
        ^FT342,693^A0B,26,18^FDNø EMB:^FS
        ^FT342,622^A0B,26,18^FD            ^FS
        ^FT113,693^GB52,680,52^FS^FT158,693^A0B,52,45^FR^FDTIMCOD                ^FS
        ^FT754,1059^BY6,0,208^BCB,,Y,N,N,D^FD(00) 3 3109300 169041805 0^FS
        ^FT20,832^A0B,19,21,^FDOutq:^FS
        ^FT348,1168^GB0,1158,4^FS
        ^PQ3
        ^XZ
        """;

    [Fact]
    public void Reads_a_customer_pallet_label()
    {
        var l = ZplReader.Read(Palette);
        Assert.True(l.Inverted);
        Assert.Equal(3, l.Copies);
        Assert.False(l.Utf8);
        Assert.Equal(2, l.Elements.OfType<ZplBox>().Count());
        var bc = l.Barcodes.Single();
        Assert.Equal("GS1-128", bc.Symbology);
        Assert.Equal(ZplOrientation.B, bc.Orientation);
        Assert.Equal(6, bc.ModuleWidth);
        Assert.Equal(208, bc.Height);
        Assert.Equal("00331093001690418050", bc.Content);
        Assert.True(bc.Typeset);
        Assert.Equal(754, bc.X);

        var reversed = l.Texts.Single(t => t.Reverse);
        Assert.Equal(52, reversed.Height);
        Assert.Contains(l.Texts, t => t.Text == "N° EMB:"); // octet 0xF8 en page 850
        Assert.Contains(l.Issues, i => i.Severity == Severity.Warning && i.Message.Contains("^CI28"));
        Assert.Contains(l.Issues, i => i.Message.Contains("^PW"));
    }

    [Fact]
    public void Sscc_check_digit_is_verified()
    {
        var l = ZplReader.Read("^XA^FO10,10^BCN,100,Y,N,N,D^FD(00)331093001690418051^FS^XZ");
        Assert.Contains(l.Barcodes.Single().Notes, n => n.Severity == Severity.Error);
    }

    [Fact]
    public void Decodes_hex_fnc1_and_qr_prefix()
    {
        var l = ZplReader.Read("^XA^CI28^FO10,10^BXN,5,200,,,,_^FD_10103453120000011_110LOT42^FS^FO200,10^BQN,2,4^FDQA,https://example.com^FS^FO10,300^A0N,30^FH^FDA_1DB^FS^XZ");
        var dm = l.Barcodes.First();
        Assert.Equal("GS1 DataMatrix", dm.Symbology);
        Assert.Equal("0103453120000011" + "10LOT42", dm.Content.Replace("\u001D", string.Empty));
        Assert.Equal("https://example.com", l.Barcodes.Last().Content);
        Assert.Equal("A\u001DB", l.Texts.Single().Text);
        Assert.True(l.Utf8);
    }

    [Fact]
    public void Code128_invocations_and_missing_end()
    {
        var l = ZplReader.Read("^XA^FO10,10^BCN,80^FD>;>80012345678>6AB^FS");
        var bc = l.Barcodes.Single();
        Assert.True(bc.Gs1);
        Assert.Equal("0012345678AB", bc.Content);
        Assert.Contains(l.Issues, i => i.Severity == Severity.Error && i.Message.Contains("^XZ"));
    }

    [Fact]
    public void Generated_programs_read_back()
    {
        var o = LabelCode.Generate(PrinterLanguage.Zpl, new LabelJob("0103453120000011<GS>10LOT42", LabelSymbology.Gs1DataMatrix));
        var bc = ZplReader.Read(o.Code).Barcodes.Single();
        Assert.Equal("010345312000001110LOT42", bc.Content);
    }
}
