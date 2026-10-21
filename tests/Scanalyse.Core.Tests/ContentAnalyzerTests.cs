using System.IO.Compression;
using System.Text;
using Scanalyse.Core.Content;
using Scanalyse.Core.Symbologies;

namespace Scanalyse.Core.Tests;

public class ContentAnalyzerTests
{
    private readonly ContentAnalyzer _analyzer = new(FixedClock.October2026);

    private ContentAnalysis Analyze(string text, Symbology symbology = Symbology.Unknown, string? aim = null) =>
        _analyzer.Analyze(new ScanInput(text, Symbology: symbology, SymbologyIdentifier: aim));

    private static string? Value(ContentAnalysis a, string label) => a.Fields.FirstOrDefault(f => f.Label == label)?.Value;

    [Fact]
    public void Wifi_IsDecodedWithEscapes()
    {
        var a = Analyze(@"WIFI:T:WPA;S:Bureau\;5G;P:mot\:de\;passe;H:true;;");

        Assert.Equal(ContentKind.Wifi, a.Kind);
        Assert.Equal("Bureau;5G", Value(a, "Nom du réseau (SSID)"));
        Assert.Equal("mot:de;passe", Value(a, "Mot de passe"));
        Assert.True(a.Fields.Single(f => f.Label == "Mot de passe").Sensitive);
        Assert.Equal("oui", Value(a, "Réseau masqué"));
    }

    [Fact]
    public void Wifi_OpenNetworkRaisesWarning()
    {
        var a = Analyze("WIFI:T:nopass;S:Invités;;");

        Assert.Contains(a.Alerts, x => x.Severity == Severity.Warning && x.Message.Contains("sans mot de passe"));
    }

    [Fact]
    public void Url_DetectsRiskySigns()
    {
        var a = Analyze("http://user@192.168.1.10/app.apk");

        Assert.Equal(ContentKind.WebLink, a.Kind);
        Assert.Contains(a.Alerts, x => x.Message.Contains("non chiffré"));
        Assert.Contains(a.Alerts, x => x.Message.Contains("adresse IP"));
        Assert.Contains(a.Alerts, x => x.Message.Contains("« @ »"));
        Assert.Contains(a.Alerts, x => x.Message.Contains("exécutable"));
    }

    [Fact]
    public void Url_GoodHttpsLinkHasNoWarnings()
    {
        var a = Analyze("https://www.example.com/concert");

        Assert.DoesNotContain(a.Alerts, x => x.Severity >= Severity.Warning);
        Assert.Equal("www.example.com", Value(a, "Domaine"));
    }

    [Fact]
    public void DigitalLink_IsRecognized()
    {
        var a = Analyze("https://id.example.com/01/09506000134352/10/LOT1");

        Assert.Equal(ContentKind.Gs1DigitalLink, a.Kind);
        Assert.NotNull(a.Gs1);
    }

    [Fact]
    public void Gs1_FlaggedByAim()
    {
        var a = Analyze("0103453120000011\u001D10LOT", Symbology.DataMatrix, "]d2");

        Assert.Equal(ContentKind.Gs1, a.Kind);
        Assert.DoesNotContain(a.Alerts, x => x.Message.Contains("pas marqué GS1"));
    }

    [Fact]
    public void Gs1_UnflaggedStructureIsReportedAsWarning()
    {
        var a = Analyze("<GS>0103453120000011172712311010ABC<GS>21SN7788", Symbology.DataMatrix, "]d1");

        Assert.Equal(ContentKind.Gs1, a.Kind);
        Assert.Contains(a.Alerts, x => x.Severity == Severity.Warning && x.Message.Contains("pas marqué GS1"));
    }

    [Fact]
    public void Gs1_ExpiredDateIsAnError()
    {
        var a = Analyze("(01)03453120000011(17)250101", aim: "]C1");

        Assert.Contains(a.Alerts, x => x.Severity == Severity.Error && x.Message.Contains("dépassée"));
    }

    [Fact]
    public void Retail_Ean13()
    {
        var a = Analyze("3001234567892", Symbology.Ean13);

        Assert.Equal(ContentKind.RetailGtin, a.Kind);
        Assert.Contains("France", Value(a, "Préfixe GS1"));
    }

    [Fact]
    public void Retail_IsbnGetsIsbn10()
    {
        var a = Analyze("9782070612758", Symbology.Ean13);

        Assert.Equal(ContentKind.Book, a.Kind);
        Assert.Equal("2070612759", Value(a, "ISBN-10"));
    }

    [Fact]
    public void Sepa_ValidatesIban()
    {
        var a = Analyze("BCD\n002\n1\nSCT\nBNPAFRPP\nAtelier Martin\nFR7630006000011234567890189\nEUR42.50\n\n\nFacture 118");

        Assert.Equal(ContentKind.SepaPayment, a.Kind);
        Assert.Equal("42,50 EUR", Value(a, "Montant"));
        Assert.DoesNotContain(a.Alerts, x => x.Severity == Severity.Error);
    }

    [Theory]
    [InlineData("FR7630006000011234567890189", true)]
    [InlineData("FR7630006000011234567890188", false)]
    [InlineData("DE89370400440532013000", true)]
    public void Iban_Validation(string iban, bool valid) => Assert.Equal(valid, Iban.IsValid(iban));

    [Fact]
    public void VCard_IsParsed()
    {
        var a = Analyze("BEGIN:VCARD\r\nVERSION:3.0\r\nFN:Camille Martin\r\nTEL;TYPE=CELL:+33612345678\r\nEMAIL:camille@example.com\r\nEND:VCARD");

        Assert.Equal(ContentKind.Contact, a.Kind);
        Assert.Equal("Camille Martin", Value(a, "Nom"));
        Assert.Equal("cell", a.Fields.Single(f => f.Label == "Téléphone").Hint);
    }

    [Fact]
    public void OtpAuth_IsSensitive()
    {
        var a = Analyze("otpauth://totp/Exemple:alice?secret=JBSWY3DPEHPK3PXP&issuer=Exemple");

        Assert.Equal(ContentKind.OneTimePassword, a.Kind);
        Assert.True(a.Fields.Single(f => f.Label == "Clé secrète").Sensitive);
        Assert.Contains(a.Alerts, x => x.Severity == Severity.Error);
    }

    [Fact]
    public void StageNow_ProfileIsDecompressed()
    {
        const string script = "download(\"http://tech:Secret1@serveur.local/app.apk\",\"/sdcard/app.apk\");" +
                              "zh(\"Wi-Fi\",'{\"zd\":{\"48\":\"Add\",\"0\":{\"49\":\"ATELIER\",\"176\":{\"82\":\"Passphrase\",\"88\":\"exemple123\"}}}}');";
        var bytes = BuildStageNow(script);

        var a = _analyzer.Analyze(new ScanInput(Encoding.Latin1.GetString(bytes), bytes, Symbology.Pdf417));

        Assert.Equal(ContentKind.ZebraStageNow, a.Kind);
        Assert.NotNull(a.StageNow);
        Assert.Equal(2, a.StageNow.Calls.Count);
        Assert.Contains("•••@serveur.local", Value(a, "Étape 1"));
        Assert.DoesNotContain("Secret1", Value(a, "Étape 1"));
        Assert.Contains("réseau ATELIER", Value(a, "Étape 2"));
        Assert.Contains(a.Alerts, x => x.Message.Contains("identifiant (tech)"));
        Assert.Contains(a.Alerts, x => x.Message.Contains("mot de passe Wi-Fi"));
    }

    private static byte[] BuildStageNow(string script)
    {
        using var ms = new MemoryStream();
        ms.Write([0x58, 0x8E, 0x74, 0x0A]);
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
        {
            gz.Write(Encoding.UTF8.GetBytes(script));
        }

        var header = new byte[] { (byte)'S', (byte)'N', (byte)'?', (byte)'<', 1, 1 };
        return [.. header, .. Encoding.ASCII.GetBytes(Convert.ToBase64String(ms.ToArray()))];
    }
}
