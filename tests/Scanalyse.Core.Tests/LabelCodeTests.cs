using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

public class LabelCodeTests
{
    [Fact]
    public void Zpl_code128_with_escaped_reserved_characters()
    {
        var o = LabelCode.Generate(PrinterLanguage.Zpl, new LabelJob("AB^C~1", LabelSymbology.Code128, Dpi: 203, XMm: 5, YMm: 5));
        Assert.True(o.Supported);
        Assert.Contains("^BCN,", o.Code);
        Assert.Contains("^FH\\^FDAB\\5EC\\7E1^FS", o.Code);
        Assert.Contains("^FO40,40", o.Code); // 5 mm à 203 dpi = 40 points
        Assert.True(o.Code.EndsWith("^XZ\r\n", StringComparison.Ordinal));
    }

    [Fact]
    public void Zpl_gs1_datamatrix_uses_fnc1_escapes()
    {
        var o = LabelCode.Generate(PrinterLanguage.Zpl, new LabelJob("0103453120000011<GS>10LOT42<GS>17271231", LabelSymbology.Gs1DataMatrix));
        Assert.Contains("^BXN,2,200,,,,_", o.Code);
        Assert.Contains("^FD_1010345312000001110LOT42_117271231^FS", o.Code);
    }

    [Fact]
    public void Zpl_gs1_128_uses_mode_d_with_parentheses()
    {
        var o = LabelCode.Generate(PrinterLanguage.Zpl, new LabelJob("(01)03453120000011(10)LOT42", LabelSymbology.Gs1_128));
        Assert.Contains(",N,N,D", o.Code);
        Assert.Contains("^FD(01)03453120000011(10)LOT42^FS", o.Code);
    }

    [Fact]
    public void Ean13_check_digit_is_left_to_the_printer()
    {
        var o = LabelCode.Generate(PrinterLanguage.Tspl, new LabelJob("3453120000011", LabelSymbology.Ean13));
        Assert.Contains("\"EAN13\"", o.Code);
        Assert.Contains("\"345312000001\"", o.Code);
        var bad = LabelCode.Generate(PrinterLanguage.Zpl, new LabelJob("3453120000012", LabelSymbology.Ean13));
        Assert.Contains(bad.Warnings, w => w.Contains("Clé de contrôle fausse"));
    }

    [Fact]
    public void Unsupported_combinations_explain_why()
    {
        var o = LabelCode.Generate(PrinterLanguage.Epl2, new LabelJob("ABC", LabelSymbology.QrCode));
        Assert.False(o.Supported);
        Assert.Contains("ZPL", o.Warnings[0]);
        var t = LabelCode.Generate(PrinterLanguage.Tspl, new LabelJob("A<GS>B", LabelSymbology.DataMatrix));
        Assert.False(t.Supported);
    }

    [Fact]
    public void Epl_and_tspl_quote_data()
    {
        Assert.Contains("\"A\\\"B\"", LabelCode.Generate(PrinterLanguage.Epl2, new LabelJob("A\"B", LabelSymbology.Code128)).Code);
        Assert.Contains("\"A\\[\"]B\"", LabelCode.Generate(PrinterLanguage.Tspl, new LabelJob("A\"B", LabelSymbology.Code128)).Code);
    }

    [Fact]
    public void Symbology_is_guessed_from_read_format()
    {
        Assert.Equal(LabelSymbology.Gs1DataMatrix, LabelCode.FromFormat("Data Matrix", "0103453120000011<GS>10LOT42"));
        Assert.Equal(LabelSymbology.Ean13, LabelCode.FromFormat("EAN-13", "3453120000011"));
        Assert.Equal(LabelSymbology.Code128, LabelCode.FromFormat("Code 128", "XXRAN261701571"));
        Assert.Equal(LabelSymbology.DataMatrix, LabelCode.FromFormat(null, "[)><RS>01<GS>02<RS><EOT>"));
    }
}

public class DplTests
{
    [Fact]
    public void Generates_and_reads_back_dpl()
    {
        var o = LabelCode.Generate(PrinterLanguage.Dpl, new LabelJob("ABC-123", LabelSymbology.Code128, WidthMm: 100, HeightMm: 50, XMm: 5, YMm: 5, ModuleDots: 2, HeightMmBars: 15));
        Assert.True(o.Supported);
        Assert.True(o.Code.StartsWith("\u0002L", StringComparison.Ordinal));
        // 50 - 5 - 15 - 3 (texte lisible) = 27 mm depuis le bas ; 5 mm depuis la gauche ; hauteur 15 mm
        Assert.Contains("1E221500270" + "0050ABC-123", o.Code);
        Assert.Contains("Q0001\r\nE", o.Code);
        Assert.Equal(ProgramKind.Dpl, ProgramFile.Detect(o.Code));
        var code = ProgramFile.Barcodes(o.Code, ProgramKind.Dpl).Single();
        Assert.Equal("Code 128", code.Symbology);
        Assert.Equal("ABC-123", code.Data);
        Assert.Equal(50, code.X);

        var qr = LabelCode.Generate(PrinterLanguage.Dpl, new LabelJob("https://a.b", LabelSymbology.QrCode));
        Assert.Contains("1W1d", qr.Code);
        Assert.Equal("QR Code", ProgramFile.Barcodes(qr.Code, ProgramKind.Dpl).Single().Symbology);
        Assert.False(LabelCode.Generate(PrinterLanguage.Dpl, new LabelJob("(01)03453120000011", LabelSymbology.Gs1_128)).Supported);
    }
}

public class LabelTemplateTests
{
    [Fact]
    public void Every_template_builds_a_readable_label()
    {
        foreach (var t in LabelTemplates.All)
        {
            var o = LabelTemplates.Build(t, LabelTemplates.Defaults(t));
            var label = ZplReader.Read(o.Zpl);
            Assert.DoesNotContain(label.Issues, i => i.Severity == Scanalyse.Core.Content.Severity.Error);
            Assert.True(label.Barcodes.Any());
            foreach (var c in label.Barcodes)
            {
                Assert.DoesNotContain(c.Notes, n => n.Severity == Scanalyse.Core.Content.Severity.Error);
            }
        }
    }

    [Fact]
    public void Pallet_sscc_key_is_computed_and_gs1_is_valid()
    {
        var t = LabelTemplates.Get("pallet")!;
        var o = LabelTemplates.Build(t, LabelTemplates.Defaults(t));
        var codes = ZplReader.Read(o.Zpl).Barcodes.ToList();
        Assert.Equal(3, codes.Count);
        Assert.Equal("00331093001690418057", codes[2].Content);
        Assert.Contains("10LOT42", codes[1].Content);
        Assert.Contains("3102051240", codes[1].Content);
    }
}
