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
        Assert.Contains(notes, n => n.Line == 12 && n.Status == ConversionStatus.NotConverted);
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
        Assert.Contains(sgd.Notes, n => n.Line == 3 && n.Status == ConversionStatus.NotConverted);
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
        Assert.DoesNotContain(r.Notes, n => n.Status == ConversionStatus.Approximated);
        Assert.Contains(ConfigConverter.Write(new PrinterSettings(), ConfigLanguage.Zpl).Notes, n => n.Status == ConversionStatus.NotConverted);
    }
}
