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

public class ProgramSyntaxTests
{
    [Fact]
    public void Zpl_tokens_cover_the_whole_text_and_are_classified()
    {
        const string zpl = "^XA\r\n^FO10,20^A0N,30,30^FDHello ~ world^FS\r\n^BY2^BCN,80^FD123^FS^FXnote\r\n^XZ";
        var t = ProgramSyntax.Tokenize(zpl);
        Assert.Equal(zpl.Length, t.Sum(x => x.Length));
        string Of(SyntaxKind k) => string.Join("|", t.Where(x => x.Kind == k).Select(x => zpl.Substring(x.Start, x.Length)));
        Assert.Equal("^FO", Of(SyntaxKind.Position));
        Assert.Equal("^A0", Of(SyntaxKind.Text));
        Assert.Contains("Hello ~ world", Of(SyntaxKind.Data));
        Assert.Equal("^BY|^BC", Of(SyntaxKind.Barcode));
        Assert.Contains("note", Of(SyntaxKind.Comment));
    }

    [Fact]
    public void Tspl_lines_are_tokenized()
    {
        const string tspl = "SIZE 100 mm,50 mm\r\nBARCODE 40,40,\"128\",120,1,0,3,3,\"A\\[\"]B\"\r\nPRINT 1,1\r\n";
        var t = ProgramSyntax.Tokenize(tspl);
        Assert.Equal(tspl.Length, t.Sum(x => x.Length));
        Assert.Equal(3, t.Count(x => x.Kind == SyntaxKind.Structure));
    }
}

public class ProgramFileTests
{
    [Fact]
    public void Detects_languages()
    {
        Assert.Equal(ProgramKind.Zpl, ProgramFile.Detect("^XA^FDx^FS^XZ"));
        Assert.Equal(ProgramKind.Tspl, ProgramFile.Detect("SIZE 100 mm,50 mm\r\nGAP 2 mm,0\r\nCLS\r\nPRINT 1\r\n"));
        Assert.Equal(ProgramKind.Epl2, ProgramFile.Detect("\r\nN\r\nq812\r\nB40,40,0,1,2,4,100,B,\"ABC\"\r\nP1\r\n"));
        Assert.Equal(ProgramKind.Dpl, ProgramFile.Detect("\u0002L\r\nD11\r\nE\r\n"));
        Assert.Equal(ProgramKind.Unknown, ProgramFile.Detect("bonjour"));
    }

    [Fact]
    public void Lists_tspl_and_epl_barcodes()
    {
        var t = ProgramFile.Barcodes("CLS\r\nBARCODE 40,40,\"EAN128\",120,1,0,3,3,\"(01)03453120000011(10)LOT42\"\r\nQRCODE 300,40,M,4,A,0,\"https://a.b\"\r\nPRINT 1\r\n", ProgramKind.Tspl);
        Assert.Equal(2, t.Count);
        Assert.Equal("GS1-128", t[0].Symbology);
        Assert.Equal("010345312000001110LOT42", ProgramFile.ScannerContent(t[0]));
        Assert.Equal("https://a.b", t[1].Data);
        Assert.Equal(3, t[1].Line);

        var e = ProgramFile.Barcodes("N\r\nB40,40,0,E30,2,4,100,B,\"345312000001\"\r\nP1\r\n", ProgramKind.Epl2);
        Assert.Equal("EAN-13", e.Single().Symbology);
        Assert.Equal(40, e.Single().X);
    }
}

public class ProgramFileDecodeTests
{
    [Fact]
    public void Decodes_notepad_unicode_and_latin1()
    {
        const string zpl = "^XA\r\n^FDN° EMB^FS\r\n^XZ";
        var utf16 = new byte[] { 0xFF, 0xFE }.Concat(System.Text.Encoding.Unicode.GetBytes(zpl)).ToArray();
        var (t1, e1) = ProgramFile.Decode(utf16);
        Assert.Equal(zpl, t1);
        Assert.True(e1.StartsWith("UTF-16", StringComparison.Ordinal));
        Assert.Equal(zpl, ProgramFile.Decode(System.Text.Encoding.Unicode.GetBytes(zpl)).Text);
        Assert.Equal(zpl, ProgramFile.Decode(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(System.Text.Encoding.UTF8.GetBytes(zpl)).ToArray()).Text);
        Assert.Equal(zpl, ProgramFile.Decode(System.Text.Encoding.Latin1.GetBytes(zpl)).Text);
        Assert.Equal(ProgramKind.Zpl, ProgramFile.Detect(t1));
    }
}
