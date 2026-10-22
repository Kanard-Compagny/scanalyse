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
