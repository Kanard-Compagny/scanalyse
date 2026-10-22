using System.Text;
using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

public class PrinterCommandsTests
{
    [Fact]
    public void Commandes_par_langage()
    {
        Assert.Equal("~HS", Encoding.ASCII.GetString(PrinterCommands.Command(PrinterLanguage.Zpl, PrinterAction.Status)!));
        Assert.Equal("<ESC>!?", PrinterCommands.Display(PrinterLanguage.Tspl, PrinterAction.Status));
        Assert.Null(PrinterCommands.Command(PrinterLanguage.Dpl, PrinterAction.Calibrate));
    }

    [Fact]
    public void Etat_zpl_papier_et_tete()
    {
        const string reply = "\u0002030,1,0,1245,000,0,0,0,000,0,0,0\u0003\r\n\u0002001,0,1,0,1,2,6,0,00000000,1,000\u0003\r\n\u00021234,0\u0003\r\n";
        var alerts = PrinterCommands.ParseStatus(PrinterLanguage.Zpl, Encoding.ASCII.GetBytes(reply));
        Assert.True(alerts.Any(a => a.Severity == Severity.Error && a.Message.Contains("papier", StringComparison.Ordinal)));
        Assert.True(alerts.Any(a => a.Message.Contains("Tête", StringComparison.Ordinal)));
        Assert.True(alerts.Any(a => a.Message.Contains("transfert thermique", StringComparison.Ordinal)));
        Assert.True(alerts.Any(a => a.Message.Contains("1245", StringComparison.Ordinal)));
    }

    [Fact]
    public void Etat_zpl_pret()
    {
        const string reply = "\u0002030,0,0,0812,000,0,0,0,000,0,0,0\u0003\r\n\u0002001,0,0,0,0,2,6,0,00000000,1,000\u0003\r\n\u00021234,0\u0003\r\n";
        var alerts = PrinterCommands.ParseStatus(PrinterLanguage.Zpl, Encoding.ASCII.GetBytes(reply));
        Assert.Equal(Severity.Ok, alerts[0].Severity);
    }

    [Fact]
    public void Etat_tspl_et_dpl()
    {
        var t = PrinterCommands.ParseStatus(PrinterLanguage.Tspl, [0x05]);
        Assert.Equal(2, t.Count(a => a.Severity == Severity.Error));
        Assert.Equal(Severity.Ok, PrinterCommands.ParseStatus(PrinterLanguage.Tspl, [0x00])[0].Severity);
        var d = PrinterCommands.ParseStatus(PrinterLanguage.Dpl, Encoding.ASCII.GetBytes("NYNNNNNN\r"));
        Assert.True(d.Any(a => a.Severity == Severity.Error));
        Assert.Equal(Severity.Warning, PrinterCommands.ParseStatus(PrinterLanguage.Zpl, [])[0].Severity);
    }
}
