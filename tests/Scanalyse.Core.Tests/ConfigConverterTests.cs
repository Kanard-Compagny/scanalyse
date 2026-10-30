using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

/// <summary>Conversion des fichiers de configuration d'un langage à l'autre, et assistant « Configurer ».</summary>
public class ConfigConverterTests
{
    private const string Zpl = "~SD20\r\n^XA\r\n^PR4\r\n^MNY\r\n^MTD\r\n^MMP\r\n^PW812\r\n^LL1218\r\n^LS10\r\n~TA012\r\n^NSP,192.168.1.50,255.255.255.0,192.168.1.1\r\n^WIA\r\n^JUS\r\n^XZ\r\n";

    [Fact]
    public void Lecture_zpl_en_reglages_communs()
    {
        var (s, notes, sources) = ConfigConverter.Extract(Zpl);
        Assert.Contains(ConfigLanguage.Zpl, sources);
        Assert.Equal(20.0 / 30, s.Darkness!.Value, 3);
        Assert.Equal(4, s.SpeedIps);
        Assert.Equal(MediaKind.Gap, s.Media);
        Assert.Equal(PrintMethod.DirectThermal, s.Method);
        Assert.Equal(OutputMode.PeelOff, s.Mode);
        Assert.Equal(101.6, s.WidthMm!.Value, 1);
        Assert.Equal(152.4, s.LengthMm!.Value, 1);
        Assert.True(s.Dhcp == false);
        Assert.Equal("192.168.1.50", s.IpAddress);
        Assert.True(s.WifiDhcp == true);
        Assert.DoesNotContain(notes, n => n.Line == 12 && n.Status == ConversionStatus.NotConverted);
    }

    [Fact]
    public void Cle_wpa_calculee_comme_la_borne()
    {
        // Vecteur de test IEEE 802.11i (annexe H.4) : mot de passe « password », réseau « IEEE ».
        Assert.Equal("f42c6fc52df0ebef9ebb4b90b38a5f902e83fe1b135a70e23aed762e9710a12e", ConfigConverter.WpaPsk("password", "IEEE"));
        Assert.True(ConfigConverter.IsValidPassphrase("motdepasse"));
        Assert.False(ConfigConverter.IsValidPassphrase("court"));
    }

    private static PrinterSettings Wifi() => new()
    {
        WifiSsid = "Entrepot",
        WifiSecurity = WifiSecurity.WpaPersonal,
        WifiPassphrase = "motdepasse123",
        WifiCountry = "europe",
        WifiDhcp = false,
        WifiIpAddress = "192.168.10.60",
        WifiNetmask = "255.255.255.0",
        WifiGateway = "192.168.10.1",
        Restart = true,
    };

    [Fact]
    public void Wifi_zebra_en_sgd_sans_mot_de_passe_en_clair()
    {
        var r = ConfigConverter.Write(Wifi(), ConfigLanguage.Zpl);
        Assert.StartsWith("! U1 setvar \"wlan.country_code\" \"europe\"", r.Text, StringComparison.Ordinal);
        var mixed = Wifi();
        mixed.Darkness = 0.5;
        Assert.Contains("^XZ\r\n! U1 setvar \"wlan.country_code\"", ConfigConverter.Write(mixed, ConfigLanguage.Zpl).Text, StringComparison.Ordinal);
        Assert.Contains("! U1 setvar \"wlan.essid\" \"Entrepot\"", r.Text, StringComparison.Ordinal);
        Assert.Contains("! U1 setvar \"wlan.security\" \"wpa psk\"", r.Text, StringComparison.Ordinal);
        Assert.Contains($"! U1 setvar \"wlan.wpa.psk\" \"{ConfigConverter.WpaPsk("motdepasse123", "Entrepot")}\"", r.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("motdepasse123", r.Text, StringComparison.Ordinal);
        Assert.Contains("! U1 setvar \"wlan.ip.protocol\" \"permanent\"", r.Text, StringComparison.Ordinal);
        Assert.EndsWith("! U1 do \"device.reset\" \"\"\r\n", r.Text, StringComparison.Ordinal);

        // Relu : mêmes réglages, et l'analyse ne trouve pas d'erreur.
        var (back, _, _) = ConfigConverter.Extract(r.Text);
        Assert.Equal("Entrepot", back.WifiSsid);
        Assert.Equal(WifiSecurity.WpaPersonal, back.WifiSecurity);
        Assert.True(ConfigConverter.IsHexKey(back.WifiPassphrase));
        Assert.Equal("192.168.10.60", back.WifiIpAddress);
        Assert.Equal("europe", back.WifiCountry);
        var analysed = PrinterConfig.Analyze(r.Text);
        Assert.DoesNotContain(analysed.Findings, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void Wifi_tsc_en_commandes_wlan()
    {
        var r = ConfigConverter.Write(Wifi(), ConfigLanguage.Tspl);
        Assert.Contains("WLAN SSID \"Entrepot\"", r.Text, StringComparison.Ordinal);
        Assert.Contains("WLAN WPA \"motdepasse123\"", r.Text, StringComparison.Ordinal);
        Assert.Contains("WLAN IP \"192.168.10.60\",\"255.255.255.0\",\"192.168.10.1\"", r.Text, StringComparison.Ordinal);
        Assert.Contains(r.Notes, n => n.Status == ConversionStatus.NotConverted && n.Message.StartsWith("Redémarrage", StringComparison.Ordinal));
        var (back, _, _) = ConfigConverter.Extract("SIZE 100 mm, 50 mm\r\n" + r.Text);
        Assert.Equal("Entrepot", back.WifiSsid);
        Assert.Equal("motdepasse123", back.WifiPassphrase);
        Assert.True(back.WifiDhcp == false);
        var analysed = PrinterConfig.Analyze("SIZE 100 mm, 50 mm\r\n" + r.Text);
        Assert.DoesNotContain(analysed.Findings, f => f.Severity == Severity.Error);
        Assert.Contains(analysed.Findings, f => f.Message.Contains("en clair", StringComparison.Ordinal));
    }

    [Fact]
    public void Wifi_autres_marques_explique()
    {
        var r = ConfigConverter.Write(new PrinterSettings { WifiSsid = "Entrepot", WifiSecurity = WifiSecurity.Open }, ConfigLanguage.Sbpl);
        Assert.Contains(r.Notes, n => n.Status == ConversionStatus.NotConverted && n.Message.Contains("SATO All-In-One", StringComparison.Ordinal));
    }

    [Fact]
    public void Zpl_vers_tspl_et_retour()
    {
        var r = ConfigConverter.Convert(Zpl, ConfigLanguage.Tspl);
        Assert.Contains("SIZE 101.6 mm, 152.4 mm", r.Text, StringComparison.Ordinal);
        Assert.Contains("GAP 3 mm, 0 mm", r.Text, StringComparison.Ordinal);
        Assert.Contains("SPEED 4", r.Text, StringComparison.Ordinal);
        Assert.Contains("DENSITY 10", r.Text, StringComparison.Ordinal);
        Assert.Contains("SET RIBBON OFF", r.Text, StringComparison.Ordinal);
        Assert.Contains("SET PEEL ON", r.Text, StringComparison.Ordinal);
        Assert.Contains("NET IP \"192.168.1.50\"", r.Text, StringComparison.Ordinal);
        Assert.Contains(r.Notes, n => n.Status == ConversionStatus.Approximated && n.Message.Contains("Noirceur", StringComparison.Ordinal));
        var analysed = PrinterConfig.Analyze(r.Text);
        Assert.Contains("TSPL", analysed.Languages);
        Assert.DoesNotContain(analysed.Findings, f => f.Severity == Scanalyse.Core.Content.Severity.Error);

        var back = ConfigConverter.Convert(r.Text, ConfigLanguage.Zpl);
        Assert.Contains("^MTD", back.Text, StringComparison.Ordinal);
        Assert.Contains("^MMP", back.Text, StringComparison.Ordinal);
        Assert.Contains("^PW812", back.Text, StringComparison.Ordinal);
        Assert.Contains("~SD20", back.Text, StringComparison.Ordinal);
        Assert.Contains("^JUS", back.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Toutes_les_cibles_sont_lisibles()
    {
        foreach (var target in Enum.GetValues<ConfigLanguage>())
        {
            var r = ConfigConverter.Convert(Zpl, target);
            Assert.False(string.IsNullOrWhiteSpace(r.Text));
            var analysed = PrinterConfig.Analyze(r.Text);
            Assert.DoesNotContain(analysed.Findings, f => f.Severity == Scanalyse.Core.Content.Severity.Error && !f.Message.Contains("retour à la ligne", StringComparison.Ordinal));
            var again = ConfigConverter.Extract(r.Text);
            Assert.False(again.Settings.IsEmpty);
        }
    }

    [Fact]
    public void Sgd_fingerprint_sbpl_dpl_epl()
    {
        var sgd = ConfigConverter.Convert("! U1 setvar \"print.tone\" \"15\"\r\n! U1 setvar \"media.printmode\" \"cutter\"\r\n! U1 setvar \"wlan.essid\" \"X\"\r\n", ConfigLanguage.Fingerprint);
        Assert.Contains("SETUP \"MEDIA\",\"CONTRAST\",\"+0%\"", sgd.Text, StringComparison.Ordinal);
        Assert.Contains(sgd.Notes, n => n.Status == ConversionStatus.NotConverted && n.Message.StartsWith("Wi-Fi", StringComparison.Ordinal));
        Assert.Contains(sgd.Notes, n => n.Status == ConversionStatus.NotConverted && n.Message.Contains("Sortie", StringComparison.Ordinal));

        var fp = ConfigConverter.Extract("SETUP \"MEDIA\",\"MEDIA TYPE\",\"TICKET (W MARK)\"\r\nSETUP \"PRINT DEFS\",\"PRINT SPEED\",150\r\n").Settings;
        Assert.Equal(MediaKind.Mark, fp.Media);
        Assert.Equal(150 / 25.4, fp.SpeedIps!.Value, 2);

        var sbpl = ConfigConverter.Extract("<ESC>A\r\n<ESC>CS4\r\n<ESC>#E3\r\n<ESC>IG2\r\n<ESC>Z\r\n").Settings;
        Assert.Equal(0.5, sbpl.Darkness!.Value, 3);
        Assert.Equal(MediaKind.Gap, sbpl.Media);

        var dpl = ConfigConverter.Extract("<STX>r\r\n<STX>L\r\nH15\r\nPG\r\nE\r\n").Settings;
        Assert.Equal(MediaKind.Mark, dpl.Media);
        Assert.Equal(4, dpl.SpeedIps);
        Assert.Equal(0.5, dpl.Darkness!.Value, 3);

        var epl = ConfigConverter.Extract("q812\r\nQ1218,B24\r\nS3\r\nD10\r\nOD\r\n").Settings;
        Assert.Equal(MediaKind.Mark, epl.Media);
        Assert.Equal(PrintMethod.DirectThermal, epl.Method);
    }

    [Fact]
    public void Assistant_ecrit_sans_fichier()
    {
        var s = new PrinterSettings { Darkness = 0.5, SpeedIps = 5, Media = MediaKind.Continuous, WidthMm = 100, LengthMm = 50, Dpi = 300, Dhcp = true };
        var r = ConfigConverter.Write(s, ConfigLanguage.Sgd);
        Assert.Contains("! U1 setvar \"print.tone\" \"15\"", r.Text, StringComparison.Ordinal);
        Assert.Contains("! U1 setvar \"ezpl.print_width\" \"1181\"", r.Text, StringComparison.Ordinal);
        Assert.Contains("! U1 setvar \"ip.protocol\" \"all\"", r.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(r.Notes, n => n.Status == ConversionStatus.Approximated && !n.Message.Contains("redémarrage", StringComparison.Ordinal));
        Assert.Contains(r.Notes, n => n.Message.Contains("redémarrage", StringComparison.Ordinal));
        Assert.Contains(ConfigConverter.Write(new PrinterSettings(), ConfigLanguage.Zpl).Notes, n => n.Status == ConversionStatus.NotConverted);
    }

    [Fact]
    public void Toshiba_tpcl()
    {
        const string tpcl = "{D0530,1000,0500|}\r\n{AX;+010,-005,+00|}\r\n{AY;+05,1|}\r\n{C|}{T20E40|}\r\n{XS;I,0001,0002C3000|}\r\n{AY;+15,0|}\r\n{D05,1000|}\r\n{T20C3|\r\n";
        var r = PrinterConfig.Analyze(tpcl);
        Assert.Contains("TPCL", r.Languages);
        Assert.DoesNotContain(r.Findings, f => f.Line <= 5 && f.Severity == Scanalyse.Core.Content.Severity.Error);
        Assert.Contains(r.Findings, f => f.Line == 6 && f.Severity == Scanalyse.Core.Content.Severity.Error);
        Assert.Contains(r.Findings, f => f.Line == 7 && f.Severity == Scanalyse.Core.Content.Severity.Error);
        Assert.Contains(r.Findings, f => f.Line == 8 && f.Severity == Scanalyse.Core.Content.Severity.Error);

        var (s, _, sources) = ConfigConverter.Extract(tpcl);
        Assert.Contains(ConfigLanguage.Tpcl, sources);
        Assert.Equal(100, s.WidthMm);
        Assert.Equal(50, s.LengthMm);
        Assert.Equal(3, s.GapMm!.Value, 2);
        Assert.Equal(MediaKind.Gap, s.Media);

        var zpl = ConfigConverter.Convert("~SD20\r\n^XA^PR4^MNY^MTD^MMP^PW800^LL400^JUS^XZ\r\n", ConfigLanguage.Tpcl);
        Assert.Contains("{D0530,1001,0500|}", zpl.Text, StringComparison.Ordinal);
        Assert.Contains("{T20E40|}", zpl.Text, StringComparison.Ordinal);
        Assert.Contains("{AY;+03,1|}", zpl.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(PrinterConfig.Analyze(zpl.Text).Findings, f => f.Severity == Scanalyse.Core.Content.Severity.Error);

        Assert.Equal(PrinterBrand.Toshiba, PrinterConfig.DetectBrand("TOSHIBA B-EX4T1"));
        Assert.Equal(ConfigLanguage.Tpcl, ConfigConverter.ForBrand(PrinterBrand.Toshiba));
        var toshiba = new PrinterProfile(PrinterBrand.Toshiba, "B-EX4T1", null, [], new Dictionary<string, PrinterValue>(), "pilote");
        Assert.Empty(PrinterConfig.Compatibility(r, toshiba));
        Assert.Contains(PrinterConfig.Compatibility(PrinterConfig.Analyze("! U1 setvar \"print.tone\" \"20\"\r\n"), toshiba), f => f.Severity == Scanalyse.Core.Content.Severity.Error);
        Assert.Contains(PrinterConfig.Tokenize("{D0530,1000|}{X"), t => t.Kind == ConfigTokenKind.Unknown);
        Assert.Contains(PrinterConfig.Suggest("{A").Items, i => i.Language == "TPCL");
    }
}
