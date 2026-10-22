using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

public class ForeignLabelTests
{
    [Fact]
    public void Tspl_lit_taille_textes_codes_et_cadres()
    {
        const string program = "SIZE 100 mm,50 mm\r\nGAP 2 mm,0 mm\r\nCLS\r\nTEXT 40,20,\"3\",0,1,1,\"Colis 12\"\r\n"
            + "BARCODE 40,80,\"128\",120,1,0,3,3,\"SCANALYSE-0001\"\r\nQRCODE 500,40,M,6,A,0,\"https://example.com\"\r\nBOX 10,10,790,390,3\r\nPRINT 1,1\r\n";
        var label = ForeignLabelReader.Read(program, ProgramKind.Tspl, 203)!.Label;
        Assert.Equal(799, label.WidthDots);
        Assert.Equal(400, label.LengthDots); // 50 mm à 8 points/mm
        var text = (ZplText)label.Elements[0];
        Assert.Equal("Colis 12", text.Text);
        Assert.Equal(24, text.Height);
        var codes = label.Barcodes.ToList();
        Assert.Equal(2, codes.Count);
        Assert.Equal("Code128", codes[0].Format);
        Assert.Equal(120, codes[0].Height);
        Assert.Equal(3, codes[0].ModuleWidth);
        Assert.True(codes[0].Interpretation);
        Assert.Equal(5, codes[0].Line);
        Assert.Equal("QRCode", codes[1].Format);
        Assert.Equal(6, codes[1].ModuleWidth);
        var box = (ZplBox)label.Elements[3];
        Assert.Equal(780, box.Width);
        Assert.True(ForeignLabelReader.Read(program, ProgramKind.Tspl, 203)!.RenderJson().Contains("\"pv\":\"tl\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Tspl_gs1_128_contenu_transmis()
    {
        var label = ForeignLabelReader.Read("SIZE 4,2\nBARCODE 10,10,\"EAN128\",100,1,0,2,4,\"(01)03453120000011(10)LOT1\"\n", ProgramKind.Tspl, 203)!.Label;
        Assert.Equal(812, label.WidthDots);
        var c = label.Barcodes.Single();
        Assert.True(c.Gs1);
        Assert.Equal("01034531200000111" + "0LOT1", c.Content);
    }

    [Fact]
    public void Epl2_lit_textes_codes_et_traits()
    {
        const string program = "N\r\nq812\r\nQ406,24\r\nA50,20,0,4,1,1,N,\"Bonjour\"\r\nB50,80,0,1,2,6,100,B,\"ABC123\"\r\nb400,40,Q,s5,\"QR\"\r\nLO10,300,700,4\r\nX5,5,2,800,400\r\nP1\r\n";
        var label = ForeignLabelReader.Read(program, ProgramKind.Epl2, 203)!.Label;
        Assert.Equal(812, label.WidthDots);
        Assert.Equal(406, label.LengthDots);
        Assert.Equal("Bonjour", label.Texts.Single().Text);
        var codes = label.Barcodes.ToList();
        Assert.Equal("Code128", codes[0].Format);
        Assert.Equal(100, codes[0].Height);
        Assert.Equal("QRCode", codes[1].Format);
        Assert.Equal(5, codes[1].ModuleWidth);
        Assert.Equal(2, label.Elements.OfType<ZplBox>().Count());
    }

    [Fact]
    public void Dpl_origine_en_bas_et_unites_metriques()
    {
        const string program = "\u0002L\r\nm\r\nD11\r\n1911A1000500010Texte\r\n1E1105000200010SCAN01\r\n1X1100000100010l01000005\r\nE\r\n";
        var label = ForeignLabelReader.Read(program, ProgramKind.Dpl, 203)!.Label;
        var text = label.Texts.Single();
        var code = label.Barcodes.Single();
        Assert.Equal("Texte", text.Text);
        Assert.Equal("Code128", code.Format);
        Assert.Equal("SCAN01", code.Content);
        // Le texte (5 mm du bas) est plus haut sur l'étiquette que le code (2 mm du bas) : y plus petit.
        Assert.True(text.Y < code.Y);
        Assert.Equal(8, text.X); // 10 × 0,1 mm = 1 mm
        Assert.Equal(1, label.Elements.OfType<ZplBox>().Count());
    }

    [Fact]
    public void Autres_langages_non_lus()
    {
        Assert.Null(ForeignLabelReader.Read("^XA^XZ", ProgramKind.Zpl, 203));
    }
}

public class LabelConverterTests
{
    [Theory]
    [InlineData(PrinterLanguage.Tspl, ProgramKind.Tspl)]
    [InlineData(PrinterLanguage.Epl2, ProgramKind.Epl2)]
    [InlineData(PrinterLanguage.Dpl, ProgramKind.Dpl)]
    public void Modeles_convertis_et_relus_avec_les_memes_codes(PrinterLanguage language, ProgramKind kind)
    {
        foreach (var template in LabelTemplates.All)
        {
            var zpl = LabelTemplates.Build(template, LabelTemplates.Defaults(template), 203).Zpl;
            var source = ZplReader.Read(zpl);
            var converted = LabelConverter.Convert(zpl, language, 203, 2);
            Assert.Equal(kind, ProgramFile.Detect(converted.Program));
            var back = ForeignLabelReader.Read(converted.Program, kind, 203)!.Label;
            var expected = source.Barcodes.Where(b => b.Format.Length > 0).Select(b => b.Format).OrderBy(f => f).ToList();
            var actual = back.Barcodes.Select(b => b.Format).OrderBy(f => f).ToList();
            Assert.Equal(expected, actual);
            Assert.True(back.Texts.Count() >= source.Texts.Count(), $"{template.Id} {language}: textes {back.Texts.Count()} < {source.Texts.Count()}");
            foreach (var code in source.Barcodes.Where(b => !b.Gs1 && b.Format is "Code128" or "QRCode"))
            {
                Assert.True(back.Barcodes.Any(b => b.Content == code.Content), $"{template.Id} {language}: {code.Content}");
            }
        }
    }

    [Fact]
    public void Zpl_reste_inchange()
    {
        Assert.Equal("^XA^XZ", LabelConverter.Convert("^XA^XZ", PrinterLanguage.Zpl, 203).Program);
    }
}
