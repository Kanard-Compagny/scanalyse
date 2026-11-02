using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;

namespace Scanalyse.Core.Tests;

/// <summary>Analyse des fichiers de configuration d'imprimantes (onglet « Conf imprimante »).</summary>
public class PrinterConfigTests
{
    private static bool Has(ConfigReport r, int line, Severity severity, string part) =>
        r.Findings.Any(f => f.Line == line && f.Severity == severity && f.Message.Contains(part, StringComparison.Ordinal));

    [Fact]
    public void Sgd_zebra_valeurs_fautes_et_guillemets()
    {
        const string file = "! U1 setvar \"print.tone\" \"20\"\r\n"
            + "! U1 setvar \"media.speed\" \"6,5\"\r\n"
            + "! U1 setvar \"print.tonne\" \"15\"\r\n"
            + "! U1 setvar “media.type” “gap/notch”\r\n"
            + "! U1 setvar \"ip.addr\" \"192.168.1.300\"\r\n"
            + "! U1 setvar \"ip.dhcp.enable\" \"yes\"\r\n"
            + "! U1 setvar \"wlan.wpa.psk\" \"motdepasse\"\r\n"
            + "!U1 setvar \"media.printmode\" \"tear off\"\r\n"
            + "! U1 setvar \"media.printmode\" \"Tear Off\"\r\n";
        var r = PrinterConfig.Analyze(file);
        Assert.Contains("SGD", r.Languages);
        Assert.DoesNotContain(r.Findings, f => f.Line == 1);
        Assert.True(Has(r, 2, Severity.Error, "séparateur décimal"));
        Assert.True(Has(r, 3, Severity.Warning, "print.tone"));
        Assert.True(Has(r, 4, Severity.Error, "Guillemets typographiques"));
        Assert.Equal("gap/notch", r.Settings.Single(s => s.Key == "media.type").Value);
        Assert.True(Has(r, 5, Severity.Error, "IPv4"));
        Assert.True(Has(r, 6, Severity.Error, "« on » ou « off »"));
        Assert.True(Has(r, 7, Severity.Warning, "en clair"));
        Assert.Equal("••••••", r.Settings.Single(s => s.Key == "wlan.wpa.psk").Value);
        Assert.True(Has(r, 8, Severity.Error, "espace après « ! »"));
        Assert.True(Has(r, 9, Severity.Error, "« tear off »"));
        Assert.True(r.Risky);
    }

    [Fact]
    public void Sgd_retour_a_la_ligne_final_et_redemarrage_avant_la_fin()
    {
        var r = PrinterConfig.Analyze("! U1 do \"device.reset\" \"\"\r\n! U1 setvar \"print.tone\" \"18\"");
        Assert.True(Has(r, 1, Severity.Warning, "ignore ce qui suit"));
        Assert.True(Has(r, 2, Severity.Error, "retour à la ligne"));
        Assert.True(r.Risky);
    }

    [Fact]
    public void Json_sgd()
    {
        var r = PrinterConfig.Analyze("{}{\"print.tone\":\"25\",\"media.speed\":\"40\",\"appl.name\":null}\r\n");
        Assert.Contains("JSON SGD", r.Languages);
        Assert.True(Has(r, 1, Severity.Error, "hors limites"));
        Assert.Contains(r.Items, i => i.Category == "Lecture");
        Assert.True(Has(PrinterConfig.Analyze("{}{\"print.tone\":\"25\"\r\n"), 1, Severity.Error, "JSON SGD invalide"));
    }

    [Fact]
    public void Zpl_reglages_sans_jus_puis_avec()
    {
        var r = PrinterConfig.Analyze("^XA\r\n^MD10\r\n^PR4,4\r\n^MNY\r\n^MTD\r\n^MMP\r\n^PW812\r\n^LL1218\r\n^XZ\r\n");
        Assert.True(Has(r, 2, Severity.Warning, "^JUS"));
        Assert.Equal("4", r.Settings.Single(s => s.Key == "media.speed").Value);
        Assert.Equal("gap/notch", r.Settings.Single(s => s.Key == "ezpl.media_type").Value);
        Assert.Equal("direct thermal", r.Settings.Single(s => s.Key == "ezpl.print_method").Value);
        Assert.Equal("peel off", r.Settings.Single(s => s.Key == "media.printmode").Value);
        Assert.Contains(r.Findings, f => f.Line == 6 && f.Message.Contains("option", StringComparison.Ordinal));

        var saved = PrinterConfig.Analyze("^XA^MD10^PR4^JUS^XZ");
        Assert.DoesNotContain(saved.Findings, f => f.Message.Contains("^JUS", StringComparison.Ordinal));
        var badOrder = PrinterConfig.Analyze("^XA^JUS^MD10^XZ");
        Assert.Contains(badOrder.Findings, f => f.Message.Contains("avant certains réglages", StringComparison.Ordinal));
    }

    [Fact]
    public void Zpl_valeurs_hors_limites_reseau_et_retour_usine()
    {
        var r = PrinterConfig.Analyze("~SD40\r\n^XA^MNX^MTQ^NSP,10.0.0.300,255.255.255.0,10.0.0.1^JUS^XZ\r\n^XA^JUN^XZ\r\n");
        Assert.True(Has(r, 1, Severity.Error, "hors limites"));
        Assert.True(Has(r, 2, Severity.Error, "^MNX"));
        Assert.True(Has(r, 2, Severity.Error, "^MTQ"));
        Assert.True(Has(r, 2, Severity.Error, "10.0.0.300"));
        Assert.True(Has(r, 3, Severity.Warning, "injoignable"));
        Assert.True(r.Risky);
    }

    [Fact]
    public void Tspl_tsc()
    {
        const string file = "SIZE 100 mm, 50 mm\r\nGAP 3 mm, 0 mm\r\nSPEED 4\r\nDENSITY 20\r\nDIRECTION 2\r\nSET PEEL MAYBE\r\nSET CUTTER OFF\r\nSPEED 4,5\r\nINITIALPRINTER\r\n";
        var r = PrinterConfig.Analyze(file);
        Assert.Contains("TSPL", r.Languages);
        Assert.True(Has(r, 4, Severity.Error, "hors limites"));
        Assert.True(Has(r, 5, Severity.Error, "DIRECTION"));
        Assert.True(Has(r, 6, Severity.Error, "ON ou OFF"));
        Assert.DoesNotContain(r.Findings, f => f.Line == 7 && f.Severity == Severity.Error);
        Assert.True(Has(r, 8, Severity.Error, "point"));
        Assert.True(Has(r, 9, Severity.Warning, "usine"));
    }

    [Fact]
    public void Dpl_honeywell_datamax()
    {
        var r = PrinterConfig.Analyze("<STX>e\r\n<STX>M0500\r\n<STX>KcPA0\r\n<STX>L\r\nD11\r\nH35\r\nPG\r\n1911A1000100010Test\r\nE\r\n<SOH>#\r\n");
        Assert.Contains("DPL", r.Languages);
        Assert.Equal("espace", r.Settings.Single(s => s.Key == "DPL:SENSOR").Value);
        Assert.True(Has(r, 3, Severity.Info, "Kc"));
        Assert.True(Has(r, 6, Severity.Error, "hors limites"));
        Assert.True(r.Risky);
    }

    [Fact]
    public void Fingerprint_setup()
    {
        var r = PrinterConfig.Analyze("SETUP \"MEDIA\",\"MEDIA TYPE\",\"LABEL (W GAPS)\"\r\nSETUP \"PRINT DEFS\",\"PRINT SPEED\",\"150\"\r\nSETUP \"MEDIA\",\"CONTRAST,\"+0%\"\r\n");
        Assert.Contains("Fingerprint", r.Languages);
        Assert.Equal("LABEL (W GAPS)", r.Settings.Single(s => s.Key == "FP:MEDIA / MEDIA TYPE").Value);
        Assert.Contains(r.Items, i => i.Line == 2 && i.Category == "Impression");
        Assert.True(Has(r, 3, Severity.Error, "guillemet"));
    }

    [Fact]
    public void Sbpl_sato()
    {
        var r = PrinterConfig.Analyze("<ESC>A\r\n<ESC>CS3\r\n<ESC>#E3\r\n<ESC>IG2\r\n<ESC>Z\r\n<ESC>A<ESC>#E9<ESC>Z\r\n");
        Assert.Contains("SBPL", r.Languages);
        Assert.Equal("3", r.Settings.Single(s => s.Key == "SBPL:CS").Value);
        Assert.Contains(r.Items, i => i.Meaning.Contains("transmission", StringComparison.Ordinal));
        Assert.True(Has(r, 6, Severity.Error, "#E9"));
    }

    [Fact]
    public void Epl2()
    {
        var r = PrinterConfig.Analyze("q812\r\nQ1218,24\r\nS3\r\nD20\r\nZT\r\nOD\r\n");
        Assert.Contains("EPL2", r.Languages);
        Assert.True(Has(r, 4, Severity.Error, "hors limites"));
        Assert.Contains(r.Items, i => i.Meaning.Contains("thermique direct", StringComparison.Ordinal));
    }

    [Fact]
    public void Lecture_allcv_et_comparaison_avec_l_imprimante()
    {
        const string allcv = "\"print.\r\nprint.tone : 20.0 , Choices: 0.0-30.0\r\nmedia.speed : 4.0 , Choices: 2.0-6.0\r\n"
            + "media.printmode : tear off , Choices: tear off,peel off,rewind,applicator,cutter\r\nbluetooth.bluetooth_pin : * \r\nappl.name : V92.21.39Z \r\n\"";
        var printer = PrinterConfig.ParseAllcv(allcv);
        Assert.Equal("20.0", printer["print.tone"].Value);
        Assert.Equal("0.0-30.0", printer["print.tone"].Choices);
        Assert.Null(printer["appl.name"].Choices);

        var report = PrinterConfig.Analyze("! U1 setvar \"print.tone\" \"20\"\r\n! U1 setvar \"media.speed\" \"8\"\r\n! U1 setvar \"media.printmode\" \"cutter\"\r\n! U1 setvar \"rfid.enable\" \"on\"\r\n");
        var c = PrinterConfig.Compare(report, printer);
        Assert.Contains(c, f => f.Line == 1 && f.Severity == Severity.Ok);
        Assert.Contains(c, f => f.Line == 2 && f.Severity == Severity.Error && f.Message.Contains("2.0-6.0", StringComparison.Ordinal));
        Assert.Contains(c, f => f.Line == 3 && f.Severity == Severity.Info && f.Message.Contains("tear off", StringComparison.Ordinal));
        Assert.Contains(c, f => f.Line == 4 && f.Severity == Severity.Warning);
        Assert.True(PrinterConfig.Accepts("on,off", "on"));
        Assert.False(PrinterConfig.Accepts("0.0-30.0", "31"));
    }

    [Fact]
    public void Deux_imprimantes_comparees_et_copiees()
    {
        var a = PrinterConfig.ParseAllcv("print.tone : 25.0 , Choices: 0.0-30.0\r\nmedia.speed : 6.0 , Choices: 2.0-6.0\r\nmedia.type : continuous , Choices: continuous,gap/notch,mark\r\n"
            + "ip.addr : 10.0.0.5 , Choices: \r\nappl.name : V1 \r\nrfid.enable : on , Choices: on,off\r\nmedia.printmode : tear off , Choices: tear off,peel off\r\n");
        var b = PrinterConfig.ParseAllcv("print.tone : 20.0 , Choices: 0.0-30.0\r\nmedia.speed : 4.0 , Choices: 2.0-4.0\r\nmedia.type : gap/notch , Choices: continuous,gap/notch,mark\r\n"
            + "ip.addr : 10.0.0.6 , Choices: \r\nappl.name : V2 \r\nmedia.printmode : tear off , Choices: tear off,peel off\r\n");
        var diff = PrinterConfig.Diff(a, b);
        Assert.DoesNotContain(diff, d => d.Key == "appl.name");
        Assert.Contains(diff, d => d.Key == "rfid.enable" && d.Target == null);
        Assert.Contains(diff, d => d.Key == "media.printmode" && d.Same);
        Assert.Contains(diff, d => d.Key == "ip.addr" && d.Use == SettingUse.Specific && !d.Same);

        var (text, written, refused) = PrinterConfig.CopyFile(a, b);
        Assert.Equal(2, written);
        Assert.Equal("! U1 setvar \"media.type\" \"continuous\"\r\n! U1 setvar \"print.tone\" \"25.0\"\r\n", text);
        Assert.Equal(["media.speed", "rfid.enable"], refused);

        var saved = PrinterConfig.SaveState(a, "Zebra ZT230 : atelier", new DateTimeOffset(2026, 11, 2, 14, 34, 0, TimeSpan.Zero));
        var back = PrinterConfig.ParseAllcv(saved);
        Assert.Equal(a.Count, back.Count);
        Assert.Equal("25.0", back["print.tone"].Value);
        Assert.Equal("Zebra ZT230 - atelier - 2026-11-02 14:34", PrinterConfig.StateName(saved));

        Assert.Equal("Wi-Fi", PrinterConfig.FamilyOf("wlan.ssid"));
        Assert.Equal("Support", PrinterConfig.FamilyOf("media.speed"));
        Assert.Equal(2.0, PrinterConfig.ChoicesOf(a["media.speed"])!.Min);
        Assert.Equal(["tear off", "peel off"], PrinterConfig.ChoicesOf(a["media.printmode"])!.Options);
    }

    [Fact]
    public void Reglages_lus_reecrits_en_fichier()
    {
        var printer = PrinterConfig.ParseAllcv("print.tone : 20.0 , Choices: 0.0-30.0\r\nmedia.printmode : tear off , Choices: tear off,peel off\r\n"
            + "ip.addr : 172.16.10.32 , Choices: \r\ninternal_wired.ip.addr : 172.16.10.32 , Choices: 0.0.0.0-255.255.255.255\r\n"
            + "device.friendly_name : ZT230 , Choices: \r\ndevice.restore_defaults :  , Choices: ip,wlan,display\r\nwlan.private_key_password : * , Choices: \r\n"
            + "appl.name : V92.21.39Z \r\nodometer.total_label_count : 1234 , Choices: 0-4294967295\r\n");
        var (text, written, skipped) = PrinterConfig.SettingsFile(printer);
        Assert.Equal(2, written);
        Assert.Equal(7, skipped);
        Assert.Equal("! U1 setvar \"media.printmode\" \"tear off\"\r\n! U1 setvar \"print.tone\" \"20.0\"\r\n", text);
        Assert.DoesNotContain(PrinterConfig.Analyze(text, printer).Findings, f => f.Severity == Severity.Error);
        Assert.Equal("abc", PrinterConfig.Unquote(" \"abc\" "));

        // Commande ZPL dont l'équivalent SGD n'a pas été lu : rien à signaler (la commande existe sur tout modèle).
        var zpl = PrinterConfig.Analyze("^XA^MNY^PW336^XZ\r\n", printer);
        Assert.DoesNotContain(PrinterConfig.Compare(zpl, printer), f => f.Severity is Severity.Error or Severity.Warning);
        Assert.NotNull(PrinterConfig.SettingLabel("print.tone"));
        Assert.Equal(SettingUse.Specific, PrinterConfig.Classify("ip.addr", printer["ip.addr"]));
        Assert.Equal(SettingUse.Action, PrinterConfig.Classify("device.restore_defaults", printer["device.restore_defaults"]));
        Assert.Equal(SettingUse.ReadOnly, PrinterConfig.Classify("appl.name", printer["appl.name"]));
        Assert.Equal(SettingUse.Portable, PrinterConfig.Classify("print.tone", printer["print.tone"]));

        // Liste de valeurs (canaux Wi-Fi) et valeur identique à celle de l'imprimante : pas d'erreur.
        Assert.True(PrinterConfig.Accepts("1-14", "1,2,3,11"));
        Assert.False(PrinterConfig.Accepts("1-14", "1,2,15"));
        var wifi = PrinterConfig.ParseAllcv("wlan.user_channel_list : 1,2,3,4,5,6,7,8,9,10,11 , Choices: all\r\n");
        var same = PrinterConfig.Compare(PrinterConfig.Analyze("! U1 setvar \"wlan.user_channel_list\" \"1,2,3,4,5,6,7,8,9,10,11\"\r\n", wifi), wifi);
        Assert.DoesNotContain(same, f => f.Severity == Severity.Error);
    }

    [Fact]
    public void Fichier_vide_ou_etranger()
    {
        Assert.Contains(PrinterConfig.Analyze("Bonjour, voici le fichier.").Findings, f => f.Severity == Severity.Warning);
    }

    [Fact]
    public void Propositions_a_la_saisie()
    {
        var name = PrinterConfig.Suggest("! U1 setvar \"print.to");
        Assert.Equal(8, name.ReplaceLength);
        Assert.Equal("print.tone", name.Items[0].Label);
        Assert.Equal("print.tone\" \"\"", name.Items[0].Text);
        Assert.Equal(name.Items[0].Text.Length - 1, name.Items[0].CaretOffset);
        Assert.True(name.Items[0].Description.Contains("0 à 30", StringComparison.Ordinal));
        Assert.DoesNotContain(name.Items, i => i.Label == "appl.name");

        var value = PrinterConfig.Suggest("! U1 setvar \"media.printmode\" \"pe");
        Assert.Equal("peel off", value.Items[0].Label);
        Assert.Equal(2, value.ReplaceLength);

        Assert.Contains(PrinterConfig.Suggest("! U1 do \"").Items, i => i.Label == "device.reset");
        Assert.DoesNotContain(PrinterConfig.Suggest("! U1 do \"").Items, i => i.Label == "print.tone");

        var zpl = PrinterConfig.Suggest("^XA^M");
        Assert.Equal(2, zpl.ReplaceLength);
        Assert.Contains(zpl.Items, i => i.Label == "^MD");
        Assert.DoesNotContain(zpl.Items, i => i.Language != "ZPL");
        Assert.Empty(PrinterConfig.Suggest("^XA").Items);

        var tspl = PrinterConfig.Suggest("SET PE");
        Assert.Equal("SET PEEL", tspl.Items[0].Label);
        Assert.Equal("SET PEEL ".Length, tspl.Items[0].CaretOffset);

        Assert.Contains(PrinterConfig.Suggest("<ESC>I").Items, i => i.Label == "<ESC>IG");
        Assert.Contains(PrinterConfig.Suggest("noirceur").Items, i => i.Label == "~SD");
        Assert.Empty(PrinterConfig.Suggest("").Items);
        Assert.NotEmpty(PrinterConfig.Suggest("", force: true).Items);
    }

    [Fact]
    public void Zpl_wifi_commandes_reseau_et_commandes_inconnues()
    {
        const string file = "^XA\r\n^WIP,10.178.188.2,255.255.255.0,10.178.188.254\r\n^NC2\r\n^NPP\r\n^KC0,0,,\r\n^NNEXTSTPTS21\r\n^WAD,D\r\n"
            + "^WSRE_LOG01,I,L,,,\r\n^NBS\r\n^WLOFF,,\r\n^WKOFF,,,,\r\n^WX09,motdepassewifi\r\n^XZ\r\n^XA\r\n^JUS\r\n^XZ\r\n"
            + "^XA^TD^XZ\r\n^XA^NC5^WIQ,10.0.0.1^MX^XZ\r\n~XA\r\n";
        var r = PrinterConfig.Analyze(file);
        Assert.DoesNotContain(r.Findings, f => f.Line <= 16 && f.Severity == Severity.Error);
        Assert.Equal("RE_LOG01", r.Settings.Single(s => s.Key == "wlan.essid").Value);
        Assert.Contains(r.Items, i => i.Line == 2 && i.Meaning.Contains("IP 10.178.188.2", StringComparison.Ordinal));
        Assert.True(Has(r, 12, Severity.Warning, "en clair"));
        Assert.True(Has(r, 17, Severity.Error, "^TD"));
        Assert.True(Has(r, 18, Severity.Error, "^NC5"));
        Assert.True(Has(r, 18, Severity.Error, "attribution « Q »"));
        Assert.True(Has(r, 18, Severity.Error, "^MX"));
        Assert.True(Has(r, 19, Severity.Error, "^XA"));
        Assert.True(r.Risky);

        Assert.Contains(PrinterConfig.Suggest("^XA^W").Items, i => i.Label == "^WS");
        var country = PrinterConfig.Suggest("! U1 setvar \"wlan.country_code\" \"eu");
        Assert.Equal("europe", country.Items[0].Label);
        Assert.Equal(2, country.ReplaceLength);
        Assert.DoesNotContain(PrinterConfig.Analyze("! U1 setvar \"wlan.country_code\" \"france\"\r\n").Findings, f => f.Severity == Severity.Error);
        Assert.True(Has(PrinterConfig.Analyze("! U1 setvar \"wlan.inconnu.truc\" \"1\"\r\n"), 1, Severity.Warning, "inconnu"));
    }

    [Fact]
    public void Cle_wifi_calculee_et_coloration()
    {
        var r = PrinterConfig.Analyze("^XA^WX09,80E22B4187B7B02E7D090CC2AE4D0F484876922400DAD6D0F0D8B2F528DFFB93^XZ\r\n");
        Assert.True(Has(r, 1, Severity.Warning, "Clé Wi-Fi calculée"));
        Assert.True(Has(r, 1, Severity.Warning, "Bonnes pratiques"));

        const string text = "^XA^MD10^TD^FDBonjour^FS\r\n! U1 setvar \"print.tone\" \"20\"\r\n!U1 getvar \"x\"\nSET PEEL ON\r\n{}{\"media.speed\":\"4\"}\r\n<ESC>A<ESC>CS3\n";
        var tokens = PrinterConfig.Tokenize(text);
        var pos = 0;
        foreach (var t in tokens)
        {
            Assert.Equal(pos, t.Start);
            pos += t.Length;
        }

        Assert.Equal(text.Length, pos);
        string Of(ConfigTokenKind k) => string.Join("|", tokens.Where(t => t.Kind == k).Select(t => text.Substring(t.Start, t.Length)));
        Assert.True(Of(ConfigTokenKind.Unknown).Contains("^TD", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Unknown).Contains("!U1", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Command).Contains("^MD", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Value).Contains("Bonjour", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Name).Contains("\"print.tone\"", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Name).Contains("\"media.speed\"", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Verb).Contains("PEEL", StringComparison.Ordinal));
        Assert.True(Of(ConfigTokenKind.Command).Contains("<ESC>", StringComparison.Ordinal));
    }

    [Fact]
    public void Imprimante_lue_fait_reference()
    {
        var printer = PrinterConfig.ParseAllcv("ip.https.enable : on , Choices: on,off\r\nzbi.secret_option : off , Choices: on,off\r\nprint.tone : 20.0 , Choices: 0.0-30.0\r\n"
            + "device.languages : zpl , Choices: zpl,epl_zpl,epl\r\ndevice.product_name : ZT411 \r\nappl.name : V92.21.39Z \r\nmedia.printmode : tear off , Choices: tear off,peel off\r\n");
        const string file = "! U1 setvar \"zbi.secret_option\" \"on\"\r\n! U1 setvar \"ip.https.enable\" \"off\"\r\n! U1 setvar \"wlan.inexistant\" \"1\"\r\n";
        var alone = PrinterConfig.Analyze(file);
        Assert.True(Has(alone, 1, Severity.Warning, "inconnu"));
        Assert.DoesNotContain(alone.Findings, f => f.Line == 2);

        var withPrinter = PrinterConfig.Analyze(file, printer);
        Assert.DoesNotContain(withPrinter.Findings, f => f.Line is 1 or 3);
        var c = PrinterConfig.Compare(withPrinter, printer);
        Assert.Contains(c, f => f.Line == 3 && f.Severity == Severity.Warning);
        Assert.Contains(c, f => f.Line == 1 && f.Severity == Severity.Info);

        Assert.Contains(PrinterConfig.Suggest("! U1 setvar \"zbi.s", printer: printer).Items, i => i.Label == "zbi.secret_option");
        Assert.Equal(["tear off", "peel off"], PrinterConfig.Suggest("! U1 setvar \"media.printmode\" \"", printer: printer).Items.Select(i => i.Label));

        var profile = PrinterConfig.ZebraProfile(printer, "réseau");
        Assert.Equal("Zebra ZT411", profile.DisplayName);
        Assert.Equal(["ZPL"], profile.Languages);
        var epl = PrinterConfig.Analyze("q812\r\nQ1218,24\r\nS3\r\nD10\r\nZT\r\nOD\r\n");
        Assert.Contains(PrinterConfig.Compatibility(epl, profile), f => f.Severity == Severity.Error && f.Message.Contains("EPL2", StringComparison.Ordinal));
    }

    [Fact]
    public void Compatibilite_toutes_marques()
    {
        Assert.Equal(PrinterBrand.Zebra, PrinterConfig.DetectBrand("ZDesigner ZT411-203dpi ZPL"));
        Assert.Equal(PrinterBrand.Tsc, PrinterConfig.DetectBrand("TSC TE210"));
        Assert.Equal(PrinterBrand.Honeywell, PrinterConfig.DetectBrand("Honeywell PC42t"));
        Assert.Equal(PrinterBrand.Datamax, PrinterConfig.DetectBrand("Datamax-O'Neil I-4212e"));
        Assert.Equal(PrinterBrand.Sato, PrinterConfig.DetectBrand("SATO CL4NX 305dpi"));
        Assert.Equal(PrinterBrand.Unknown, PrinterConfig.DetectBrand("Microsoft Print to PDF"));

        var empty = new Dictionary<string, PrinterValue>();
        var tsc = new PrinterProfile(PrinterBrand.Tsc, "TE210", null, [], empty, "pilote");
        var sgd = PrinterConfig.Analyze("! U1 setvar \"print.tone\" \"20\"\r\n");
        Assert.Contains(PrinterConfig.Compatibility(sgd, tsc), f => f.Severity == Severity.Error && f.Message.Contains("propres aux Zebra", StringComparison.Ordinal));
        var tspl = PrinterConfig.Analyze("SIZE 100 mm, 50 mm\r\nSPEED 4\r\n");
        Assert.Empty(PrinterConfig.Compatibility(tspl, tsc));
        var zebra = new PrinterProfile(PrinterBrand.Zebra, "ZD421", null, [], empty, "pilote");
        Assert.Contains(PrinterConfig.Compatibility(tspl, zebra), f => f.Severity == Severity.Error);
        var zpl = PrinterConfig.Analyze("^XA^MD10^JUS^XZ\r\n");
        Assert.Contains(PrinterConfig.Compatibility(zpl, tsc), f => f.Severity == Severity.Warning && f.Message.Contains("émulation", StringComparison.Ordinal));
        var tscWithZpl = tsc with { Languages = PrinterConfig.LanguagesFrom(["TSPL2", "ZPL2 emulation"]) };
        Assert.Contains(PrinterConfig.Compatibility(zpl, tscWithZpl), f => f.Severity == Severity.Info);
        var sato = new PrinterProfile(PrinterBrand.Sato, "CL4NX", null, [], empty, "SNMP");
        Assert.Contains(PrinterConfig.Compatibility(PrinterConfig.Analyze("<STX>e\r\n<STX>M0500\r\n"), sato), f => f.Severity == Severity.Warning);
        Assert.Contains(PrinterConfig.Compatibility(PrinterConfig.Analyze("SETUP \"MEDIA\",\"MEDIA TYPE\",\"LABEL (W GAPS)\"\r\n"), sato), f => f.Severity == Severity.Error);
    }

    [Fact]
    public void Snmp_requete_et_reponse()
    {
        var request = Snmp.BuildGet(Snmp.SysDescr, 7);
        var oid = new byte[] { 0x06, 0x08, 0x2B, 0x06, 0x01, 0x02, 0x01, 0x01, 0x01, 0x00 };
        Assert.True(request.AsSpan().IndexOf(oid) > 0);
        Assert.Equal(0x30, request[0]);

        static byte[] Tlv(byte tag, params byte[][] parts)
        {
            var content = parts.SelectMany(p => p).ToArray();
            return [tag, (byte)content.Length, .. content];
        }

        var value = System.Text.Encoding.ASCII.GetBytes("Zebra Technologies ZTC ZT411-203dpi ZPL");
        var response = Tlv(0x30, Tlv(0x02, [0]), Tlv(0x04, System.Text.Encoding.ASCII.GetBytes("public")),
            Tlv(0xA2, Tlv(0x02, [7]), Tlv(0x02, [0]), Tlv(0x02, [0]), Tlv(0x30, Tlv(0x30, oid, Tlv(0x04, value)))));
        var parsed = Snmp.ParseResponse(response);
        Assert.NotNull(parsed);
        Assert.Equal(7, parsed.Value.RequestId);
        Assert.Equal(0, parsed.Value.Error);
        Assert.Equal(Snmp.SysDescr, parsed.Value.Values[0].Oid);
        Assert.Equal("Zebra Technologies ZTC ZT411-203dpi ZPL", parsed.Value.Values[0].Value);
        Assert.Null(Snmp.ParseResponse([1, 2, 3]));
    }
}
