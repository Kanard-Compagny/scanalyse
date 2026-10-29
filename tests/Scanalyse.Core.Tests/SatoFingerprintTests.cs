using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

/// <summary>Lecture des programmes SBPL (SATO) et Fingerprint / Direct Protocol (Intermec, Honeywell).</summary>
public class SatoFingerprintTests
{
    private const char Esc = '\u001B';

    [Fact]
    public void Sbpl_exemple_du_guide_rotation_antihoraire()
    {
        // Exemple du guide SBPL (commande ESC %) : texte à 180°, code 39 à 90° dans le sens inverse des aiguilles d'une montre.
        var program = $"{Esc}A\r\n{Esc}%2\r\n{Esc}V100{Esc}H400{Esc}P3{Esc}L0403{Esc}XMABCD\r\n{Esc}%1\r\n{Esc}V400{Esc}H200{Esc}BD103160*123*\r\n{Esc}Q2\r\n{Esc}Z\r\n";
        Assert.Equal(ProgramKind.Sbpl, ProgramFile.Detect(program));
        var read = ForeignLabelReader.Read(program, ProgramKind.Sbpl, 203)!;
        Assert.Equal("tl", read.Pivot);
        var label = read.Label;
        Assert.Equal(2, label.Copies);
        var text = label.Texts.Single();
        Assert.Equal("ABCD", text.Text);
        Assert.Equal(ZplOrientation.I, text.Orientation);
        Assert.Equal((400, 100), (text.X, text.Y));
        Assert.Equal(72, text.Height); // XM 24 points × 3 (L0403)
        Assert.Equal(96, text.Width); // 24 × 4
        var code = label.Barcodes.Single();
        Assert.Equal("Code39", code.Format);
        Assert.Equal("123", code.Content); // astérisques de début et de fin non transmis
        Assert.Equal(ZplOrientation.B, code.Orientation);
        Assert.Equal(160, code.Height);
        Assert.Equal(3, code.ModuleWidth);
        Assert.Equal(5, code.Line);
    }

    [Fact]
    public void Sbpl_etiquette_logistique_codes_cadres_et_taille()
    {
        var program = "\u0002" + $"{Esc}A{Esc}A108000832\r\n{Esc}V0020{Esc}H0020{Esc}FW0303V0760H0790\r\n{Esc}V0200{Esc}H0020{Esc}FW02H0790\r\n"
            + $"{Esc}V0100{Esc}H0050{Esc}D304100590123412345\r\n{Esc}V0250{Esc}H0050{Esc}BG03120>I>F0103453120000011>F10LOT1\r\n"
            + $"{Esc}V0400{Esc}H0050{Esc}BI0312021234567890123456 7\r\n{Esc}V0450{Esc}H0050{Esc}BI03120212345678901234567\r\n"
            + $"{Esc}V0050{Esc}H0600{Esc}2D30,M,06,0,000{Esc}DN0019,https://example.com\r\n{Esc}V0300{Esc}H0600{Esc}2D50,08,08,000,000{Esc}DS2,ABC-123\r\n"
            + $"{Esc}V0700{Esc}H0050{Esc}(0200,0060\r\n{Esc}Q3{Esc}Z" + "\u0003";

        // Encadré par STX / ETX : c'est bien du SBPL, pas du DPL.
        Assert.Equal(ProgramKind.Sbpl, ProgramFile.Detect(program));
        var label = ForeignLabelReader.Read(program, ProgramKind.Sbpl, 203)!.Label;
        Assert.Equal(832, label.WidthDots);
        Assert.Equal(800, label.LengthDots);
        Assert.Equal(3, label.Copies);

        var boxes = label.Elements.OfType<ZplBox>().ToList();
        Assert.Equal((790, 760, 3), (boxes[0].Width, boxes[0].Height, boxes[0].Thickness));
        Assert.Equal((790, 2), (boxes[1].Width, boxes[1].Height));
        Assert.True(boxes[2].Reverse);

        var codes = label.Barcodes.ToList();
        Assert.Equal("EAN13", codes[0].Format);
        Assert.True(codes[0].Interpretation); // ESC D type 3 : texte lisible

        // Code 128 : départ >I retiré, >F = FNC1 → données GS1.
        Assert.True(codes[1].Gs1);
        Assert.Equal("(01)03453120000011(10)LOT1", codes[1].Readable);

        // ESC BI : nombre de chiffres faux → signalé, pas interprété comme du GS1.
        Assert.False(codes[2].Gs1);
        Assert.Contains(codes[2].Notes, n => n.Severity == Severity.Warning);

        // ESC BI : 17 chiffres, l'imprimante ajoute (00) et la clé.
        Assert.True(codes[3].Gs1);
        Assert.Equal("(00)123456789012345675", codes[3].Readable);

        Assert.Equal(("QRCode", "https://example.com"), (codes[4].Format, codes[4].Content));
        Assert.Equal(6, codes[4].ModuleWidth);
        Assert.Equal(("DataMatrix", "ABC-123"), (codes[5].Format, codes[5].Content));

        var list = ProgramFile.Barcodes(program, ProgramKind.Sbpl);
        Assert.Equal(6, list.Count);
        Assert.Equal((50, 250), (list[1].X, list[1].Y));
        Assert.Equal("01034531200000111" + "0LOT1", ProgramFile.ScannerContent(list[1]));
        Assert.Equal("00123456789012345675", ProgramFile.ScannerContent(list[3]));
    }

    [Fact]
    public void Sbpl_notation_texte_esc()
    {
        const string program = "<ESC>A<ESC>V100<ESC>H100<ESC>XMBONJOUR<ESC>V200<ESC>H100<ESC>BG02080ABC123<ESC>Q1<ESC>Z";
        Assert.Equal(ProgramKind.Sbpl, ProgramFile.Detect(program));
        var label = ForeignLabelReader.Read(program, ProgramKind.Sbpl, 203)!.Label;
        Assert.Equal("BONJOUR", label.Texts.Single().Text);
        Assert.Equal("ABC123", label.Barcodes.Single().Content);
    }

    [Fact]
    public void Sbpl_plusieurs_etiquettes_toutes_listees()
    {
        var program = $"{Esc}A{Esc}V10{Esc}H10{Esc}BG02080UN{Esc}Z{Esc}A{Esc}V10{Esc}H10{Esc}BG02080DEUX{Esc}Z";
        Assert.Equal("UN", ForeignLabelReader.Read(program, ProgramKind.Sbpl, 203)!.Label.Barcodes.Single().Content);
        Assert.Equal(["UN", "DEUX"], ProgramFile.Barcodes(program, ProgramKind.Sbpl).Select(c => c.Data));
    }

    [Fact]
    public void Sbpl_coloration_des_commandes()
    {
        var program = $"{Esc}A{Esc}V0100{Esc}XSCOLIS{Esc}BG03120ABC{Esc}Z";
        var tokens = ProgramSyntax.Tokenize(program);
        Assert.Equal(program.Length, tokens.Sum(t => t.Length));
        Assert.Equal(SyntaxKind.Structure, tokens[0].Kind);
        Assert.Contains(tokens, t => t.Kind == SyntaxKind.Barcode && program.Substring(t.Start, t.Length) == $"{Esc}BG");
        Assert.Contains(tokens, t => t.Kind == SyntaxKind.Data && program.Substring(t.Start, t.Length) == "COLIS");
    }

    [Fact]
    public void Fingerprint_abrege_origine_en_bas_et_ancrage()
    {
        const string program = "PP 30,400:AN 7:FT \"Swiss 721 BT\",10:PT \"Colis 12\"\n"
            + "PP 30,250:AN 1:BT \"CODE128\":BH 120:BM 3:BF ON:PB \"SCANALYSE-01\"\n"
            + "PP 400,250:BT \"EAN128\":BF OFF:PB \"0103453120000011\"+CHR$(128)+\"10LOT1\"\n"
            + "PP 20,20:PX 450,780,3\nPP 600,300:BARSET \"QRCODE\",1,4:PB \"https://example.com\"\nPF 2\n";
        Assert.Equal(ProgramKind.Fingerprint, ProgramFile.Detect(program));
        var read = ForeignLabelReader.Read(program, ProgramKind.Fingerprint, 203)!;
        Assert.Equal("bl", read.Pivot);
        var label = read.Label;
        Assert.Equal(2, label.Copies);

        // Y compté depuis le bas : le texte (y 400, ancré par le haut) est au-dessus du code (y 250).
        var text = label.Texts.Single();
        Assert.Equal("Colis 12", text.Text);
        var codes = label.Barcodes.ToList();
        Assert.True(text.Y < codes[0].Y);
        Assert.Equal(28, text.Height); // 10 points à 203 dpi

        Assert.Equal(("Code128", "SCANALYSE-01", 120, 3), (codes[0].Format, codes[0].Content, codes[0].Height, codes[0].ModuleWidth));
        Assert.True(codes[0].Interpretation);
        Assert.True(codes[1].Gs1); // CHR$(128) = FNC1
        Assert.Equal("(01)03453120000011(10)LOT1", codes[1].Readable);
        Assert.False(codes[1].Interpretation);
        Assert.Equal(("QRCode", 4), (codes[2].Format, codes[2].ModuleWidth));

        var box = label.Elements.OfType<ZplBox>().Single();
        Assert.Equal((780, 450, 3), (box.Width, box.Height, box.Thickness));

        // Liste : positions telles qu'écrites dans le programme.
        var list = ProgramFile.Barcodes(program, ProgramKind.Fingerprint);
        Assert.Equal((30, 250), (list[0].X, list[0].Y));
    }

    [Fact]
    public void Fingerprint_mots_cles_complets_numeros_de_ligne_et_plusieurs_etiquettes()
    {
        const string program = "10 PRPOS 50,100\n20 DIR 1\n30 FONT \"Univers\",12\n40 PRTXT \"Hello\"\n50 PRPOS 50,300\n60 BARTYPE \"CODE39\"\n70 PRBAR \"ABC\"\n"
            + "80 PRINTFEED\n90 PRPOS 10,10\n100 PRBAR \"SECOND\"\n110 PRINTFEED\n";
        Assert.Equal(ProgramKind.Fingerprint, ProgramFile.Detect(program));
        var label = ForeignLabelReader.Read(program, ProgramKind.Fingerprint, 203)!.Label;
        Assert.Equal("Hello", label.Texts.Single().Text);
        Assert.Equal("ABC", label.Barcodes.Single().Content);
        Assert.Equal(7, label.Barcodes.Single().Line);
        Assert.Equal(["ABC", "SECOND"], ProgramFile.Barcodes(program, ProgramKind.Fingerprint).Select(c => c.Data));
    }

    [Theory]
    [InlineData("Bonjour, ceci est un message : PP pas un programme.\nPF")]
    [InlineData("SIZE 100 mm,50 mm\nCLS\nTEXT 40,20,\"3\",0,1,1,\"Colis\"\nPRINT 1\n")]
    [InlineData("N\nq812\nA50,20,0,4,1,1,N,\"Bonjour\"\nP1\n")]
    public void Detection_inchangee_pour_les_autres_textes(string text)
    {
        Assert.NotEqual(ProgramKind.Fingerprint, ProgramFile.Detect(text));
        Assert.NotEqual(ProgramKind.Sbpl, ProgramFile.Detect(text));
    }
}
