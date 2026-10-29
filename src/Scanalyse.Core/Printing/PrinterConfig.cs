using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

/// <summary>Constat sur une ligne d'un fichier de configuration d'imprimante.</summary>
/// <param name="Line">Ligne (à partir de 1) ; 0 pour un constat sur tout le fichier.</param>
/// <param name="Severity">Gravité.</param>
/// <param name="Message">Explication, et quoi faire.</param>
public sealed record ConfigFinding(int Line, Severity Severity, string Message);

/// <summary>Commande reconnue, expliquée.</summary>
/// <param name="Line">Ligne (à partir de 1).</param>
/// <param name="Language">Langage (« ZPL », « SGD », « TSPL »…).</param>
/// <param name="Command">Commande telle qu'écrite (raccourcie).</param>
/// <param name="Meaning">Ce qu'elle fait, en clair.</param>
/// <param name="Category">Rubrique : Impression, Support, Réseau, Système, Communication, Étiquette, Lecture.</param>
public sealed record ConfigItem(int Line, string Language, string Command, string Meaning, string Category);

/// <summary>Réglage que le fichier donne à l'imprimante.</summary>
/// <param name="Key">Nom du réglage (nom SGD Zebra quand il existe, sinon nom de la commande).</param>
/// <param name="Label">Libellé en clair.</param>
/// <param name="Value">Valeur envoyée.</param>
/// <param name="Line">Ligne.</param>
/// <param name="Comparable">La valeur peut être comparée à celle lue sur une imprimante Zebra (getvar).</param>
public sealed record ConfigSetting(string Key, string Label, string Value, int Line, bool Comparable);

/// <summary>Analyse d'un fichier de configuration d'imprimante.</summary>
/// <param name="Languages">Langages trouvés.</param>
/// <param name="Items">Commandes, dans l'ordre du fichier.</param>
/// <param name="Findings">Constats, dans l'ordre du fichier.</param>
/// <param name="Settings">Réglages appliqués (la dernière valeur l'emporte).</param>
public sealed record ConfigReport(IReadOnlyList<string> Languages, IReadOnlyList<ConfigItem> Items, IReadOnlyList<ConfigFinding> Findings, IReadOnlyList<ConfigSetting> Settings)
{
    /// <summary>Gravité la plus élevée.</summary>
    public Severity Worst => Findings.Count == 0 ? Severity.Ok : Findings.Max(f => f.Severity);

    /// <summary>Le fichier redémarre l'imprimante, la remet en usine ou change ses réglages réseau : confirmation avant envoi.</summary>
    public bool Risky { get; init; }
}

/// <summary>Réglage lu sur l'imprimante (« ! U1 getvar "allcv" » d'une Zebra).</summary>
/// <param name="Value">Valeur actuelle.</param>
/// <param name="Choices">Valeurs acceptées annoncées par l'imprimante (« on,off », « 0.0-30.0 »…), si elle les donne.</param>
public sealed record PrinterValue(string Value, string? Choices);

/// <summary>
/// Analyse des fichiers de configuration envoyés aux imprimantes d'étiquettes : Zebra (ZPL et SGD « ! U1 setvar / getvar /
/// do », JSON SGD), TSC (TSPL), Honeywell / Datamax (DPL), Honeywell / Intermec (Fingerprint, Direct Protocol), SATO (SBPL)
/// et EPL2. Chaque ligne est expliquée ; syntaxe, valeurs, ordre des commandes et commandes risquées sont contrôlés.
/// Les plages de valeurs générales dépendent du modèle : lues sur l'imprimante (Zebra), ses propres choix font foi.
/// </summary>
public static partial class PrinterConfig
{
    // ------------------------------------------------------------------ Réglages SGD Zebra connus

    private enum Kind
    {
        Text,
        Number,
        Integer,
        Choice,
        OnOff,
        Ip,
        Do,
        Secret,
        ReadOnly,
    }

    private sealed record SgdDef(string Label, string Category, Kind Kind, double Min = 0, double Max = 0, string[]? Choices = null, string? Note = null);

    private static readonly string[] MediaTypes = ["continuous", "gap/notch", "mark", "variable length continuous"];
    private static readonly string[] PowerActions = ["feed", "calibrate", "length", "no motion", "short cal"];
    private static readonly string[] PrintModes = ["tear off", "peel off", "rewind", "applicator", "cutter", "delayed cut", "linerless peel", "linerless rewind", "partial cutter", "kiosk", "linerless tear", "rfid", "stream"];

    private static readonly Dictionary<string, SgdDef> SgdTable = new(StringComparer.Ordinal)
    {
        ["print.tone"] = new("Noirceur (contraste) de l'impression", "Impression", Kind.Number, 0, 30),
        ["media.speed"] = new("Vitesse d'impression (pouces par seconde)", "Impression", Kind.Number, 1, 14, Note: "la vitesse maximale dépend du modèle"),
        ["media.printmode"] = new("Mode de sortie des étiquettes (détachage, prédécollage, coupe…)", "Impression", Kind.Choice, Choices: PrintModes),
        ["media.type"] = new("Type de support (continu, étiquettes séparées, marque noire)", "Support", Kind.Choice, Choices: MediaTypes),
        ["ezpl.media_type"] = new("Type de support (continu, étiquettes séparées, marque noire)", "Support", Kind.Choice, Choices: MediaTypes),
        ["ezpl.print_method"] = new("Méthode d'impression (transfert thermique avec ruban, ou thermique direct)", "Support", Kind.Choice, Choices: ["thermal trans", "direct thermal"]),
        ["ezpl.print_width"] = new("Largeur d'impression (points)", "Support", Kind.Integer, 2, 5000, Note: "ne doit pas dépasser la largeur de la tête"),
        ["ezpl.label_length_max"] = new("Longueur maximale d'étiquette (pouces)", "Support", Kind.Number, 1, 39),
        ["zpl.label_length"] = new("Longueur d'étiquette (points)", "Support", Kind.Integer, 1, 32000),
        ["zpl.left_position"] = new("Décalage horizontal de l'étiquette (points)", "Support", Kind.Integer, -9999, 9999),
        ["ezpl.tear_off"] = new("Position de détachage (points)", "Support", Kind.Integer, -120, 120),
        ["media.sense_mode"] = new("Capteur de support (espace entre étiquettes ou marque noire)", "Support", Kind.Choice, Choices: ["gap", "bar"]),
        ["ezpl.power_up_action"] = new("Action à la mise sous tension", "Support", Kind.Choice, Choices: PowerActions),
        ["ezpl.head_close_action"] = new("Action à la fermeture de la tête", "Support", Kind.Choice, Choices: PowerActions),
        ["device.languages"] = new("Langage(s) de l'imprimante", "Système", Kind.Choice, Choices: ["zpl", "epl", "epl_zpl", "hybrid_xml_zpl", "line_print", "apl-d", "apl-i", "apl-e", "apl-l", "apl-m", "apl-o"],
            Note: "un langage qui ne correspond pas aux programmes envoyés empêche toute impression"),
        ["device.friendly_name"] = new("Nom de l'imprimante", "Système", Kind.Text, 0, 17),
        ["device.location"] = new("Emplacement de l'imprimante", "Système", Kind.Text),
        ["device.company_contact"] = new("Contact de l'entreprise", "Système", Kind.Text),
        ["device.reset"] = new("Redémarrage de l'imprimante", "Système", Kind.Do),
        ["device.restore_defaults"] = new("Retour aux réglages d'usine", "Système", Kind.Do),
        ["device.pnp_option"] = new("Identification Plug and Play", "Système", Kind.Text),
        ["display.language"] = new("Langue de l'écran", "Système", Kind.Text),
        ["zpl.zpl_mode"] = new("Mode ZPL", "Système", Kind.Choice, Choices: ["zpl", "zpl II"]),
        ["zpl.caret"] = new("Caractère de commande ZPL (^ par défaut)", "Système", Kind.Text, 1, 1, Note: "le changer rend illisibles les programmes qui utilisent ^"),
        ["zpl.control_character"] = new("Caractère de contrôle ZPL (~ par défaut)", "Système", Kind.Text, 1, 1, Note: "le changer rend illisibles les programmes qui utilisent ~"),
        ["zpl.delimiter"] = new("Séparateur de paramètres ZPL (, par défaut)", "Système", Kind.Text, 1, 1),
        ["power.sleep.enable"] = new("Mise en veille", "Système", Kind.OnOff),
        ["power.sleep.timeout"] = new("Délai avant la mise en veille (secondes)", "Système", Kind.Integer, 0, 86400),
        ["ip.addr"] = new("Adresse IP (filaire)", "Réseau", Kind.Ip),
        ["ip.netmask"] = new("Masque de sous-réseau (filaire)", "Réseau", Kind.Ip),
        ["ip.gateway"] = new("Passerelle (filaire)", "Réseau", Kind.Ip),
        ["ip.dhcp.enable"] = new("Adresse automatique (DHCP)", "Réseau", Kind.OnOff),
        ["ip.protocol"] = new("Attribution de l'adresse IP", "Réseau", Kind.Choice, Choices: ["all", "bootp", "dhcp", "dhcp and bootp", "gleaning only", "rarp", "permanent"]),
        ["ip.port"] = new("Port d'impression (9100 par défaut)", "Réseau", Kind.Integer, 1, 65535),
        ["ip.port_alternate"] = new("Second port d'impression", "Réseau", Kind.Integer, 1, 65535),
        ["ip.telnet.enable"] = new("Accès Telnet", "Réseau", Kind.OnOff),
        ["ip.ftp.enable"] = new("Accès FTP", "Réseau", Kind.OnOff),
        ["ip.http.enable"] = new("Page web de l'imprimante", "Réseau", Kind.OnOff),
        ["ip.snmp.enable"] = new("SNMP", "Réseau", Kind.OnOff),
        ["wlan.enable"] = new("Wi-Fi", "Réseau", Kind.OnOff),
        ["wlan.essid"] = new("Nom du réseau Wi-Fi (SSID)", "Réseau", Kind.Text, 0, 32),
        ["wlan.security"] = new("Sécurité Wi-Fi", "Réseau", Kind.Text, Choices: ["none", "wep 40-bit", "wep 128-bit", "wpa psk", "wpa eap-tls", "wpa eap-ttls", "wpa eap-fast", "wpa peap", "wpa leap", "eap-tls", "eap-ttls", "eap-fast", "peap", "leap", "kerberos"]),
        ["wlan.wpa.psk"] = new("Mot de passe Wi-Fi (WPA)", "Réseau", Kind.Secret),
        ["wlan.ip.addr"] = new("Adresse IP (Wi-Fi)", "Réseau", Kind.Ip),
        ["wlan.ip.netmask"] = new("Masque de sous-réseau (Wi-Fi)", "Réseau", Kind.Ip),
        ["wlan.ip.gateway"] = new("Passerelle (Wi-Fi)", "Réseau", Kind.Ip),
        ["wlan.ip.protocol"] = new("Attribution de l'adresse IP (Wi-Fi)", "Réseau", Kind.Choice, Choices: ["all", "bootp", "dhcp", "dhcp and bootp", "gleaning only", "rarp", "permanent"]),
        ["wlan.country_code"] = new("Pays du Wi-Fi (canaux autorisés)", "Réseau", Kind.Text, Choices: ["europe", "usa/canada", "japan", "australia/nz", "korea", "taiwan", "china", "international"]),
        ["wlan.international_mode"] = new("Mode international du Wi-Fi (pays donné par la borne)", "Réseau", Kind.OnOff),
        ["wlan.allowed_band"] = new("Bandes Wi-Fi autorisées (2,4 GHz, 5 GHz ou toutes)", "Réseau", Kind.Text, Choices: ["all", "2.4", "5"]),
        ["wlan.band_preference"] = new("Bande Wi-Fi préférée", "Réseau", Kind.Text, Choices: ["none", "2.4", "5"]),
        ["wlan.channel_mask"] = new("Canaux Wi-Fi autorisés (masque)", "Réseau", Kind.Text),
        ["wlan.encryption_mode"] = new("Chiffrement Wi-Fi", "Réseau", Kind.Text),
        ["wlan.wpa.enable"] = new("WPA (Wi-Fi)", "Réseau", Kind.OnOff),
        ["wlan.ip.dhcp.enable"] = new("Adresse automatique Wi-Fi (DHCP)", "Réseau", Kind.OnOff),
        ["wlan.active_channel"] = new("Canal Wi-Fi utilisé", "Réseau", Kind.ReadOnly),
        ["wlan.signal_strength"] = new("Force du signal Wi-Fi", "Réseau", Kind.ReadOnly),
        ["wlan.associated"] = new("Wi-Fi connecté à la borne", "Réseau", Kind.ReadOnly),
        ["wlan.mac_addr"] = new("Adresse MAC Wi-Fi", "Réseau", Kind.ReadOnly),
        ["ip.dhcp.cid_type"] = new("Identifiant client DHCP (type)", "Réseau", Kind.Text),
        ["ip.dhcp.cid_value"] = new("Identifiant client DHCP (valeur)", "Réseau", Kind.Text),
        ["internal_wired.ip.addr"] = new("Adresse IP (filaire interne)", "Réseau", Kind.Ip),
        ["internal_wired.ip.netmask"] = new("Masque de sous-réseau (filaire interne)", "Réseau", Kind.Ip),
        ["internal_wired.ip.gateway"] = new("Passerelle (filaire interne)", "Réseau", Kind.Ip),
        ["internal_wired.ip.protocol"] = new("Attribution de l'adresse IP (filaire interne)", "Réseau", Kind.Choice, Choices: ["all", "bootp", "dhcp", "dhcp and bootp", "gleaning only", "rarp", "permanent"]),
        ["media.darkness_mode"] = new("Mode de réglage de la noirceur", "Impression", Kind.Text),
        ["print.width"] = new("Largeur d'impression (points)", "Support", Kind.Integer, 2, 5000),
        ["media.cut_now"] = new("Coupe immédiate", "Impression", Kind.Do),
        ["media.feed_length"] = new("Longueur d'avance", "Support", Kind.Text),
        ["sensor.select"] = new("Capteur de support", "Support", Kind.Text),
        ["device.frontpanel.key_press"] = new("Appui sur une touche du panneau", "Système", Kind.Do),
        ["device.feature.mcr"] = new("Lecteur de piste magnétique", "Système", Kind.ReadOnly),
        ["usb.mirror.enable"] = new("Mise à jour par clé USB (miroir)", "Système", Kind.OnOff),
        ["capture.channel1.port"] = new("Capture de flux (port)", "Communication", Kind.Text),
        ["ezpl.reprint_mode"] = new("Réimpression après une erreur", "Impression", Kind.OnOff),
        ["file.dir"] = new("Liste des fichiers", "Lecture", Kind.Do),
        ["bluetooth.enable"] = new("Bluetooth", "Communication", Kind.OnOff),
        ["bluetooth.discoverable"] = new("Bluetooth visible", "Communication", Kind.OnOff),
        ["bluetooth.friendly_name"] = new("Nom Bluetooth", "Communication", Kind.Text),
        ["comm.baud"] = new("Vitesse du port série (bauds)", "Communication", Kind.Choice, Choices: ["1200", "2400", "4800", "9600", "14400", "19200", "28800", "38400", "57600", "115200"]),
        ["rfid.enable"] = new("Module RFID", "Système", Kind.OnOff),
        ["appl.name"] = new("Version du firmware", "Système", Kind.ReadOnly),
        ["device.unique_id"] = new("Numéro de série", "Système", Kind.ReadOnly),
        ["interface.network.active.ip_addr"] = new("Adresse IP active", "Réseau", Kind.ReadOnly),
        ["head.resolution.in_dpi"] = new("Résolution de la tête (dpi)", "Système", Kind.ReadOnly),
        ["odometer.total_print_length"] = new("Compteur de longueur imprimée", "Système", Kind.ReadOnly),
    };

    // ------------------------------------------------------------------ Analyse

    private sealed class Ctx
    {
        public List<ConfigItem> Items { get; } = [];

        public List<ConfigFinding> Findings { get; } = [];

        public Dictionary<string, ConfigSetting> Settings { get; } = new(StringComparer.Ordinal);

        public SortedSet<string> Languages { get; } = new(StringComparer.Ordinal);

        public bool Risky { get; set; }

        public int ResetLine { get; set; }

        public int ZplSaveLine { get; set; }

        public int FirstZplSettingLine { get; set; }

        public int LastZplSettingLine { get; set; }

        /// <summary>Position (rang de la commande) de ^JUS et du dernier réglage ZPL : l'ordre compte même sur une seule ligne.</summary>
        public int ZplSaveIndex { get; set; } = -1;

        public int LastZplSettingIndex { get; set; } = -1;

        public void Item(int line, string language, string command, string meaning, string category)
        {
            Languages.Add(language);
            Items.Add(new ConfigItem(line, language, command.Length > 80 ? command[..80] + "…" : command, meaning, category));
        }

        public void Find(int line, Severity severity, string message) => Findings.Add(new ConfigFinding(line, severity, message));

        public void Set(string key, string label, string value, int line, bool comparable = false) =>
            Settings[key] = new ConfigSetting(key, label, value, line, comparable);
    }

    /// <summary>Analyse un fichier de configuration (un ou plusieurs langages mêlés).</summary>
    public static ConfigReport Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ctx = new Ctx();
        text = text.TrimStart('﻿');
        var normalized = Notation(text);
        var fileKind = ProgramFile.Detect(normalized);
        if (fileKind == ProgramKind.Unknown && TsplConfigLine().IsMatch(normalized))
        {
            fileKind = ProgramKind.Tspl;
        }

        if (normalized.Contains('\u001B', StringComparison.Ordinal))
        {
            Sbpl(normalized, ctx);
        }
        else
        {
            var lines = normalized.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                AnalyzeLine(lines[i].TrimEnd('\r'), i + 1, fileKind, ctx);
            }

            // Une commande SGD sans retour à la ligne final n'est pas exécutée.
            var last = lines.LastOrDefault(l => l.Trim().Length > 0)?.Trim();
            if (last is not null && (last.StartsWith("! U", StringComparison.Ordinal) || last.StartsWith("{}{", StringComparison.Ordinal))
                && !text.TrimEnd(' ', '\t').EndsWith('\n'))
            {
                ctx.Find(Array.FindLastIndex(lines, l => l.Trim().Length > 0) + 1, Severity.Error,
                    "La dernière commande SGD n'est pas suivie d'un retour à la ligne : l'imprimante ne l'exécute pas. Ajoute une ligne vide à la fin du fichier.");
            }
        }

        Whole(ctx);
        var settings = ctx.Settings.Values.OrderBy(s => s.Line).ToList();
        return new ConfigReport([.. ctx.Languages], ctx.Items, [.. ctx.Findings.OrderBy(f => f.Line)], settings) { Risky = ctx.Risky };
    }

    /// <summary>Contrôles sur tout le fichier : ordre des commandes, sauvegarde des réglages ZPL.</summary>
    private static void Whole(Ctx ctx)
    {
        if (ctx.Items.Count == 0)
        {
            ctx.Find(0, Severity.Warning, "Aucune commande de configuration reconnue. Vérifie que c'est bien le fichier envoyé à l'imprimante (ZPL, SGD, TSPL, DPL, SBPL, Fingerprint, EPL2).");
            return;
        }

        if (ctx.ResetLine > 0 && ctx.Items.Any(i => i.Line > ctx.ResetLine && i.Category != "Lecture"))
        {
            ctx.Find(ctx.ResetLine, Severity.Warning,
                "Redémarrage ou retour usine avant d'autres commandes : l'imprimante redémarre et ignore ce qui suit dans le fichier. Place cette commande en dernier.");
        }

        if (ctx.FirstZplSettingLine > 0 && ctx.ZplSaveLine == 0)
        {
            ctx.Find(ctx.FirstZplSettingLine, Severity.Warning,
                "Réglages ZPL (^MD, ^PR, ^MN, ^MT, ^MM, ^PW, ^LL…) sans ^JUS : ils sont perdus à l'extinction de l'imprimante. Ajoute ^JUS avant ^XZ pour les enregistrer.");
        }
        else if (ctx.ZplSaveIndex >= 0 && ctx.LastZplSettingIndex > ctx.ZplSaveIndex)
        {
            ctx.Find(ctx.ZplSaveLine, Severity.Warning, "^JUS est placé avant certains réglages : ceux qui suivent ne sont pas enregistrés. Mets ^JUS juste avant ^XZ.");
        }
    }

    /// <summary>Notation texte des caractères de contrôle (« &lt;STX&gt; », « &lt;ESC&gt; ») → vrais caractères.</summary>
    private static string Notation(string text) => text
        .Replace("<STX>", "\u0002", StringComparison.OrdinalIgnoreCase)
        .Replace("<SOH>", "\u0001", StringComparison.OrdinalIgnoreCase)
        .Replace("<ESC>", "\u001B", StringComparison.OrdinalIgnoreCase)
        .Replace("<ETX>", "\u0003", StringComparison.OrdinalIgnoreCase);

    private static void AnalyzeLine(string raw, int n, ProgramKind fileKind, Ctx ctx)
    {
        var line = raw.Trim();
        if (line.Length == 0)
        {
            return;
        }

        // Guillemets typographiques (copier-coller depuis Word, un PDF, un e-mail) : l'imprimante ne les reconnaît pas.
        if (line.IndexOfAny(['“', '”', '«', '»', '‘', '’']) >= 0
            && (line.StartsWith('!') || line.Contains('"') || line.StartsWith("{}", StringComparison.Ordinal) || line.StartsWith("SETUP", StringComparison.OrdinalIgnoreCase)
                || line.Contains('“')))
        {
            ctx.Find(n, Severity.Error, "Guillemets typographiques (“ ” « » ’) : l'imprimante n'accepte que les guillemets droits \". Recopie la ligne depuis un éditeur de texte brut (Bloc-notes).");
            line = line.Replace('“', '"').Replace('”', '"').Replace('«', '"').Replace('»', '"').Replace('’', '\'').Replace('‘', '\'');
        }

        if (line.StartsWith('!'))
        {
            Sgd(line, n, ctx);
        }
        else if (line.StartsWith("{}", StringComparison.Ordinal) || (line.StartsWith('{') && line.Contains("\":", StringComparison.Ordinal)))
        {
            JsonSgd(line, n, ctx);
        }
        else if (line.IndexOfAny(['^', '~']) >= 0 && fileKind is ProgramKind.Zpl or ProgramKind.Unknown && ZplToken().IsMatch(line))
        {
            Zpl(line, n, ctx);
        }
        else if (line[0] is '\u0002' or '\u0001')
        {
            Dpl(line, n, ctx);
        }
        else if (fileKind == ProgramKind.Fingerprint || FingerprintSetup().IsMatch(line))
        {
            Fingerprint(line, n, ctx);
        }
        else if (fileKind == ProgramKind.Epl2)
        {
            Epl2(line, n, ctx);
        }
        else if (fileKind == ProgramKind.Tspl || TsplConfigLine().IsMatch(line))
        {
            Tspl(line, n, ctx);
        }
        else if (fileKind == ProgramKind.Dpl)
        {
            Dpl(line, n, ctx);
        }
        else
        {
            ctx.Find(n, Severity.Info, $"Ligne non reconnue : « {Short(line)} ».");
        }
    }

    private static string Short(string s) => s.Length > 60 ? s[..60] + "…" : s;

    // ------------------------------------------------------------------ SGD Zebra

    private static void Sgd(string line, int n, Ctx ctx)
    {
        var m = SgdLine().Match(line);
        if (!m.Success)
        {
            var hint = line.StartsWith("!U", StringComparison.OrdinalIgnoreCase) ? " Il manque l'espace après « ! » : écris « ! U1 setvar … »."
                : !line.Contains('"', StringComparison.Ordinal) ? " Les noms et valeurs doivent être entre guillemets droits : ! U1 setvar \"print.tone\" \"20\"."
                : " Forme attendue : ! U1 setvar \"nom\" \"valeur\", ! U1 getvar \"nom\" ou ! U1 do \"nom\" \"valeur\".";
            ctx.Item(n, "SGD", line, "Commande SGD mal formée", "Système");
            ctx.Find(n, Severity.Error, "Commande SGD illisible pour l'imprimante." + hint);
            return;
        }

        var u = m.Groups[1].Value;
        var verb = m.Groups[2].Value;
        var name = m.Groups[3].Value;
        var value = m.Groups[4].Success ? m.Groups[5].Value : null;
        if (u != "U1" && u != "U")
        {
            ctx.Find(n, Severity.Error, $"« ! {u} » : l'en-tête SGD s'écrit « ! U1 » (U majuscule).");
        }

        if (verb != verb.ToLowerInvariant())
        {
            ctx.Find(n, Severity.Error, $"« {verb} » : les commandes SGD s'écrivent en minuscules (setvar, getvar, do).");
        }

        if (name != name.ToLowerInvariant())
        {
            ctx.Find(n, Severity.Warning, $"« {name} » : les noms de réglages SGD s'écrivent en minuscules.");
        }

        SgdCommand(verb.ToLowerInvariant(), name.ToLowerInvariant(), value, n, ctx, "SGD", line);
    }

    /// <summary>{}{"print.tone":"20","media.speed":"6"} : setvar ; valeur null : getvar.</summary>
    private static void JsonSgd(string line, int n, Ctx ctx)
    {
        var json = line.StartsWith("{}", StringComparison.Ordinal) ? line[2..] : line;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException();
            }

            foreach (var p in doc.RootElement.EnumerateObject())
            {
                var value = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => null,
                    JsonValueKind.String => p.Value.GetString(),
                    _ => p.Value.GetRawText(),
                };
                SgdCommand(value is null ? "getvar" : "setvar", p.Name, value, n, ctx, "JSON SGD", $"\"{p.Name}\": {p.Value.GetRawText()}");
            }

            if (!line.StartsWith("{}", StringComparison.Ordinal))
            {
                ctx.Find(n, Severity.Warning, "Commande JSON SGD sans le préfixe « {} » : l'imprimante attend {}{\"nom\":\"valeur\"}.");
            }
        }
        catch (JsonException)
        {
            ctx.Item(n, "JSON SGD", line, "JSON mal formé", "Système");
            ctx.Find(n, Severity.Error, "JSON SGD invalide (accolades, virgules ou guillemets) : l'imprimante ignore toute la ligne. Forme attendue : {}{\"print.tone\":\"20\",\"media.speed\":\"6\"}.");
        }
    }

    private static void SgdCommand(string verb, string name, string? value, int n, Ctx ctx, string language, string raw)
    {
        SgdTable.TryGetValue(name, out var def);
        var label = def?.Label ?? name;
        var category = def?.Category ?? Category(name);
        switch (verb)
        {
            case "getvar":
                ctx.Item(n, language, raw, name == "allcv" ? "Lecture de tous les réglages de l'imprimante" : $"Lecture : {label}", "Lecture");
                break;
            case "do":
                ctx.Item(n, language, raw, $"Action : {label}" + (value is { Length: > 0 } ? $" ({value})" : string.Empty), category);
                if (name == "device.reset")
                {
                    ctx.Risky = true;
                    ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                    ctx.Find(n, Severity.Info, "Redémarre l'imprimante : la connexion est coupée quelques secondes.");
                }
                else if (name == "device.restore_defaults")
                {
                    ctx.Risky = true;
                    ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                    ctx.Find(n, Severity.Warning, $"Retour aux réglages d'usine ({(string.IsNullOrEmpty(value) ? "partie non précisée" : value)}) : les réglages actuels sont perdus"
                        + (value is "ip" or "wlan" or "all" ? ", y compris l'adresse réseau : l'imprimante peut devenir injoignable." : "."));
                }

                if (def is null)
                {
                    CheckSgdValue(name, value ?? string.Empty, null, n, ctx);
                }

                if (value is null)
                {
                    ctx.Find(n, Severity.Error, $"« do \"{name}\" » sans valeur : écris ! U1 do \"{name}\" \"\" (valeur vide entre guillemets).");
                }

                break;
            default:
                if (value is null)
                {
                    ctx.Item(n, language, raw, $"Réglage incomplet : {label}", category);
                    ctx.Find(n, Severity.Error, $"setvar \"{name}\" sans valeur : écris ! U1 setvar \"{name}\" \"valeur\".");
                    return;
                }

                ctx.Item(n, language, raw, $"{label} → {(def?.Kind == Kind.Secret ? "••••••" : value)}", category);
                ctx.Set(name, label, def?.Kind == Kind.Secret ? "••••••" : value, n, comparable: def?.Kind != Kind.Secret);
                if (category == "Réseau")
                {
                    ctx.Risky = true;
                }

                CheckSgdValue(name, value, def, n, ctx);
                break;
        }
    }

    private static void CheckSgdValue(string name, string value, SgdDef? def, int n, Ctx ctx)
    {
        if (def is null)
        {
            var near = SgdTable.Keys.Select(k => (k, d: Distance(k, name))).Where(x => x.d is > 0 and <= 2).OrderBy(x => x.d).Select(x => x.k).FirstOrDefault();
            ctx.Find(n, Severity.Warning, near is null
                ? $"« {name} » : réglage inconnu de Scanalyse. S'il n'existe pas sur l'imprimante, elle l'ignore sans message d'erreur : vérifie son nom en lisant l'imprimante (bouton « Lire l'imprimante »)."
                : $"« {name} » n'existe pas : vouliez-vous écrire « {near} » ? Un nom inconnu est ignoré par l'imprimante, sans message d'erreur.");
            return;
        }

        var v = value.Trim();
        switch (def.Kind)
        {
            case Kind.ReadOnly:
                ctx.Find(n, Severity.Warning, $"« {name} » est en lecture seule : l'imprimante ignore ce setvar.");
                break;
            case Kind.Do:
                ctx.Find(n, Severity.Warning, $"« {name} » s'utilise avec « do », pas « setvar » : ! U1 do \"{name}\" \"{value}\".");
                break;
            case Kind.OnOff when v is not ("on" or "off"):
                ctx.Find(n, Severity.Error, $"« {name} » attend « on » ou « off », pas « {value} ».");
                break;
            case Kind.Choice when def.Choices is { } choices && !choices.Contains(v, StringComparer.Ordinal):
                var closeChoice = choices.FirstOrDefault(c => string.Equals(c, v, StringComparison.OrdinalIgnoreCase));
                ctx.Find(n, closeChoice is null ? Severity.Warning : Severity.Error, closeChoice is not null
                    ? $"« {value} » : écris « {closeChoice} » (les valeurs SGD respectent les minuscules)."
                    : $"« {value} » n'est pas une valeur connue pour « {name} » ({string.Join(", ", choices)}). Selon le modèle, d'autres valeurs existent : vérifie en lisant l'imprimante.");
                break;
            case Kind.Number or Kind.Integer:
                if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                {
                    ctx.Find(n, Severity.Error, v.Contains(',', StringComparison.Ordinal)
                        ? $"« {value} » : le séparateur décimal est le point (ex. 20.5), pas la virgule."
                        : $"« {value} » n'est pas un nombre ({def.Label}).");
                }
                else if (def.Kind == Kind.Integer && d != Math.Floor(d))
                {
                    ctx.Find(n, Severity.Error, $"« {value} » : nombre entier attendu ({def.Label}).");
                }
                else if (def.Max > def.Min && (d < def.Min || d > def.Max))
                {
                    ctx.Find(n, Severity.Error, $"« {value} » hors limites pour « {name} » ({def.Min.ToString(CultureInfo.InvariantCulture)} à {def.Max.ToString(CultureInfo.InvariantCulture)})"
                        + (def.Note is null ? "." : $" ; {def.Note}."));
                }
                else if (def.Note is not null && name == "media.speed" && d > 8)
                {
                    ctx.Find(n, Severity.Info, $"Vitesse {value} ips : {def.Note}. Une vitesse élevée diminue la qualité des codes-barres.");
                }

                break;
            case Kind.Ip when !IsIpv4(v):
                ctx.Find(n, Severity.Error, $"« {value} » n'est pas une adresse IPv4 valide (quatre nombres de 0 à 255 séparés par des points).");
                break;
            case Kind.Text when def.Max > 0 && v.Length > def.Max:
                ctx.Find(n, Severity.Warning, $"« {value} » : {v.Length} caractères, l'imprimante en garde {def.Max.ToString(CultureInfo.InvariantCulture)} au plus.");
                break;
            case Kind.Secret:
                ctx.Find(n, Severity.Warning, "Mot de passe Wi-Fi écrit en clair dans ce fichier : ne le diffuse pas, et supprime-le après usage." + (v.Length is < 8 or > 64 ? " Longueur invalide : 8 à 63 caractères (ou 64 hexadécimaux)." : string.Empty));
                break;
        }

        if (def.Note is not null && def.Kind == Kind.Choice && name == "device.languages")
        {
            ctx.Find(n, Severity.Info, $"Langage réglé sur « {value} » : {def.Note}.");
        }

        if (name is "zpl.caret" or "zpl.control_character")
        {
            ctx.Find(n, Severity.Warning, $"{def.Label} : {def.Note}.");
        }
    }

    private static string Category(string name) => name.Split('.')[0] switch
    {
        "ip" or "wlan" or "internal_wired" or "interface" or "netmanage" or "weblink" => "Réseau",
        "media" or "ezpl" or "sensor" or "zpl" => "Support",
        "print" or "head" => "Impression",
        "bluetooth" or "comm" or "usb" or "parallel_port" => "Communication",
        _ => "Système",
    };

    private static bool IsIpv4(string s)
    {
        var parts = s.Split('.');
        return parts.Length == 4 && parts.All(p => p.Length is > 0 and <= 3 && p.All(char.IsAsciiDigit) && int.Parse(p, CultureInfo.InvariantCulture) <= 255);
    }

    /// <summary>Distance d'édition (fautes de frappe dans les noms de réglages).</summary>
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2)
        {
            return 99;
        }

        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            d[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0];
            d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var tmp = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = tmp;
            }
        }

        return d[b.Length];
    }

    // ------------------------------------------------------------------ ZPL

    private static readonly Dictionary<char, string> MediaModes = new()
    {
        ['T'] = "tear off",
        ['P'] = "peel off",
        ['R'] = "rewind",
        ['A'] = "applicator",
        ['C'] = "cutter",
        ['D'] = "delayed cut",
        ['K'] = "kiosk",
        ['L'] = "linerless peel",
        ['U'] = "linerless rewind",
        ['F'] = "rfid",
    };

    private static void Zpl(string line, int n, Ctx ctx)
    {
        foreach (Match m in ZplToken().Matches(line))
        {
            var prefix = m.Groups[1].Value[0];
            var code = m.Groups[2].Value.ToUpperInvariant();
            var args = m.Groups[3].Value;
            var a = args.Split(',');
            string A(int k) => k < a.Length ? a[k].Trim() : string.Empty;
            var raw = prefix + code + args;
            switch ((prefix, code))
            {
                case ('^', "XA"):
                    ctx.Item(n, "ZPL", raw, "Début de format", "Étiquette");
                    break;
                case ('^', "XZ"):
                    ctx.Item(n, "ZPL", raw, "Fin de format (les commandes sont appliquées)", "Étiquette");
                    break;
                case ('~', "SD"):
                    Range(ctx, n, raw, "Noirceur (contraste)", "Impression", A(0), 0, 30, "print.tone");
                    break;
                case ('^', "MD"):
                    Range(ctx, n, raw, "Ajustement de noirceur (relatif au réglage ~SD)", "Impression", A(0), -30, 30, "ZPL:MD");
                    ZplSetting(ctx, n);
                    break;
                case ('^', "PR"):
                {
                    var speed = A(0).ToUpperInvariant() switch { "A" => "2", "B" => "3", "C" => "4", "D" => "6", "E" => "8", var s => s };
                    Range(ctx, n, raw, "Vitesse d'impression (pouces par seconde)" + (a.Length > 1 ? ", d'avance et de recul" : string.Empty), "Impression", speed, 1, 14, "media.speed");
                    ZplSetting(ctx, n);
                    break;
                }

                case ('^', "MN"):
                {
                    var (meaning, sgd) = A(0).ToUpperInvariant() switch
                    {
                        "N" => ("Support continu", "continuous"),
                        "Y" or "W" => ("Étiquettes séparées par un espace (détection de l'espace)", "gap/notch"),
                        "M" => ("Marque noire au dos (détection de la marque)", "mark"),
                        "A" => ("Détection automatique du support", null),
                        "V" => ("Support continu de longueur variable", null),
                        _ => (string.Empty, null),
                    };
                    if (meaning.Length == 0)
                    {
                        ctx.Item(n, "ZPL", raw, "Suivi du support", "Support");
                        ctx.Find(n, Severity.Error, $"^MN{A(0)} : valeur inconnue (N continu, Y ou W espace, M marque noire, A automatique, V continu variable).");
                    }
                    else
                    {
                        ctx.Item(n, "ZPL", raw, "Suivi du support : " + meaning, "Support");
                        if (sgd is not null)
                        {
                            ctx.Set("ezpl.media_type", "Type de support", sgd, n, comparable: true);
                        }
                    }

                    ZplSetting(ctx, n);
                    break;
                }

                case ('^', "MT"):
                {
                    var t = A(0).ToUpperInvariant();
                    if (t is "T" or "D")
                    {
                        ctx.Item(n, "ZPL", raw, t == "T" ? "Transfert thermique (avec ruban)" : "Thermique direct (sans ruban)", "Support");
                        ctx.Set("ezpl.print_method", "Méthode d'impression", t == "T" ? "thermal trans" : "direct thermal", n, comparable: true);
                    }
                    else
                    {
                        ctx.Item(n, "ZPL", raw, "Méthode d'impression", "Support");
                        ctx.Find(n, Severity.Error, $"^MT{A(0)} : T (transfert thermique, avec ruban) ou D (thermique direct) attendu.");
                    }

                    ZplSetting(ctx, n);
                    break;
                }

                case ('^', "MM"):
                {
                    var mode = A(0).ToUpperInvariant();
                    if (mode.Length == 1 && MediaModes.TryGetValue(mode[0], out var printmode))
                    {
                        ctx.Item(n, "ZPL", raw, "Mode de sortie : " + printmode + (A(1).Equals("Y", StringComparison.OrdinalIgnoreCase) ? " (prédécollage)" : string.Empty), "Impression");
                        ctx.Set("media.printmode", "Mode de sortie", printmode, n, comparable: true);
                        if (mode is "P" or "C" or "R" or "A" or "D")
                        {
                            ctx.Find(n, Severity.Info, $"Mode « {printmode} » : l'option correspondante (décolleur, massicot, enrouleur, applicateur) doit être installée, sinon l'imprimante reste en erreur.");
                        }
                    }
                    else
                    {
                        ctx.Item(n, "ZPL", raw, "Mode de sortie", "Impression");
                        ctx.Find(n, Severity.Error, $"^MM{A(0)} : mode inconnu (T détachage, P prédécollage, R enroulement, A applicateur, C coupe, D coupe différée, K kiosque).");
                    }

                    ZplSetting(ctx, n);
                    break;
                }

                case ('^', "PW"):
                    Range(ctx, n, raw, "Largeur d'impression (points)", "Support", A(0), 2, 5000, "ezpl.print_width", integer: true);
                    ZplSetting(ctx, n);
                    break;
                case ('^', "LL"):
                    Range(ctx, n, raw, "Longueur d'étiquette (points)", "Support", A(0), 1, 32000, "zpl.label_length", integer: true);
                    ZplSetting(ctx, n);
                    break;
                case ('^', "LS"):
                    Range(ctx, n, raw, "Décalage horizontal de l'étiquette (points)", "Support", A(0), -9999, 9999, "zpl.left_position", integer: true);
                    ZplSetting(ctx, n);
                    break;
                case ('^', "LT"):
                    Range(ctx, n, raw, "Décalage vertical de l'étiquette (points)", "Support", A(0), -120, 120, "ZPL:LT", integer: true);
                    ZplSetting(ctx, n);
                    break;
                case ('~', "TA"):
                    Range(ctx, n, raw, "Position de détachage (points)", "Support", A(0), -120, 120, "ezpl.tear_off", integer: true);
                    break;
                case ('^', "LH"):
                    ctx.Item(n, "ZPL", raw, "Origine de l'étiquette (x, y en points)", "Support");
                    break;
                case ('^', "PO"):
                    ctx.Item(n, "ZPL", raw, A(0).Equals("I", StringComparison.OrdinalIgnoreCase) ? "Impression retournée (180°)" : "Impression normale", "Impression");
                    ZplSetting(ctx, n);
                    break;
                case ('^', "MF"):
                {
                    string Act(string c) => c.ToUpperInvariant() switch { "F" => "avance", "C" => "calibrage", "L" => "mesure de longueur", "N" => "aucun mouvement", "S" => "calibrage court", _ => "?" };
                    ctx.Item(n, "ZPL", raw, $"Mise sous tension : {Act(A(0))} ; fermeture de la tête : {Act(A(1))}", "Support");
                    if (Act(A(0)) == "?" || (A(1).Length > 0 && Act(A(1)) == "?"))
                    {
                        ctx.Find(n, Severity.Error, "^MF : F (avance), C (calibrage), L (longueur), N (aucun mouvement) ou S (calibrage court) attendus.");
                    }

                    ZplSetting(ctx, n);
                    break;
                }

                case ('^', "JU"):
                {
                    var j = A(0).ToUpperInvariant();
                    var meaning = j switch
                    {
                        "S" => "Enregistre les réglages (conservés à l'extinction)",
                        "R" => "Recharge les derniers réglages enregistrés",
                        "F" => "Retour aux réglages d'usine (hors réseau)",
                        "N" => "Retour aux réglages réseau d'usine",
                        "A" => "Retour aux réglages d'usine (y compris réseau)",
                        _ => string.Empty,
                    };
                    ctx.Item(n, "ZPL", raw, meaning.Length > 0 ? meaning : "Configuration (^JU)", "Système");
                    if (j == "S")
                    {
                        ctx.ZplSaveLine = n;
                        ctx.ZplSaveIndex = ctx.Items.Count;
                    }
                    else if (j is "F" or "N" or "A")
                    {
                        ctx.Risky = true;
                        ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                        ctx.Find(n, Severity.Warning, $"^JU{j} : {meaning.ToLowerInvariant()} ; les réglages actuels sont perdus" + (j is "N" or "A" ? ", y compris l'adresse IP : l'imprimante peut devenir injoignable." : "."));
                    }
                    else if (meaning.Length == 0)
                    {
                        ctx.Find(n, Severity.Error, $"^JU{A(0)} : valeur inconnue (S enregistrer, R recharger, F usine, N réseau d'usine).");
                    }

                    break;
                }

                case ('~', "JR"):
                    ctx.Item(n, "ZPL", raw, "Redémarrage de l'imprimante", "Système");
                    ctx.Risky = true;
                    ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                    break;
                case ('~', "JC"):
                    ctx.Item(n, "ZPL", raw, "Calibrage du capteur d'étiquettes", "Support");
                    break;
                case ('~', "JL"):
                    ctx.Item(n, "ZPL", raw, "Mesure de la longueur d'étiquette", "Support");
                    break;
                case ('~', "WC"):
                    ctx.Item(n, "ZPL", raw, "Impression de l'étiquette de configuration", "Lecture");
                    break;
                case ('~', "HS") or ('~', "HI") or ('~', "HQ") or ('^', "HH"):
                    ctx.Item(n, "ZPL", raw, "Demande d'état ou d'informations à l'imprimante", "Lecture");
                    break;
                case ('^', "KN"):
                    ctx.Item(n, "ZPL", raw, $"Nom de l'imprimante : {A(0)}" + (A(1).Length > 0 ? $" ({A(1)})" : string.Empty), "Système");
                    if (A(0).Length > 16)
                    {
                        ctx.Find(n, Severity.Warning, "^KN : nom limité à 16 caractères, la suite est coupée.");
                    }

                    break;
                case ('^', "KP"):
                    ctx.Item(n, "ZPL", raw, "Mot de passe du panneau de commande", "Système");
                    if (!(A(0).Length == 4 && A(0).All(char.IsAsciiDigit)))
                    {
                        ctx.Find(n, Severity.Error, "^KP : mot de passe de 4 chiffres attendu.");
                    }

                    ctx.Find(n, Severity.Info, "Change le mot de passe du panneau : note-le, sinon les réglages ne seront plus modifiables sur place.");
                    break;
                case ('^', "NS") or ('^', "ND") or ('^', "WI"):
                {
                    ctx.Risky = true;
                    var (mode, ip, mask, gw) = code == "ND" ? (A(1), A(2), A(3), A(4)) : (A(0), A(1), A(2), A(3));
                    ctx.Item(n, "ZPL", raw, (code == "NS" ? "Réseau" : "Wi-Fi") + $" : attribution {Resolution(mode)}" + (ip.Length > 0 ? $", IP {ip}" : string.Empty) + (mask.Length > 0 ? $", masque {mask}" : string.Empty)
                        + (gw.Length > 0 ? $", passerelle {gw}" : string.Empty), "Réseau");
                    foreach (var (label, value) in new[] { ("adresse IP", ip), ("masque", mask), ("passerelle", gw) })
                    {
                        if (value.Length > 0 && !IsIpv4(value))
                        {
                            ctx.Find(n, Severity.Error, $"{raw[..3]} : {label} « {value} » invalide.");
                        }
                    }

                    if (mode.Equals("P", StringComparison.OrdinalIgnoreCase) && ip.Length == 0)
                    {
                        ctx.Find(n, Severity.Error, $"{raw[..3]} : adresse permanente (P) sans adresse IP.");
                    }

                    if (ip.Length > 0)
                    {
                        if (code == "NS")
                        {
                            ctx.Set("ip.addr", "Adresse IP (filaire)", ip, n, comparable: true);
                        }
                        else
                        {
                            ctx.Set("wlan.ip.addr", "Adresse IP (Wi-Fi)", ip, n, comparable: true);
                        }
                    }

                    if (mode.Length > 0 && Resolution(mode) == mode)
                    {
                        ctx.Find(n, Severity.Error, $"{raw[..3]} : attribution « {mode} » inconnue (A automatique, B BOOTP, C DHCP et BOOTP, D DHCP, G glanage, R RARP, P adresse fixe).");
                    }

                    break;
                }

                case ('^', "NC"):
                    ctx.Item(n, "ZPL", raw, "Réseau principal : " + A(0) switch { "1" => "filaire", "2" => "Wi-Fi", "3" => "serveur d'impression externe", _ => A(0) }, "Réseau");
                    if (A(0) is not ("1" or "2" or "3"))
                    {
                        ctx.Find(n, Severity.Error, $"^NC{A(0)} : 1 (filaire), 2 (Wi-Fi) ou 3 (serveur externe) attendu.");
                    }

                    ctx.Risky = true;
                    break;
                case ('^', "NP"):
                    ctx.Item(n, "ZPL", raw, "Appareil principal : " + A(0).ToUpperInvariant() switch { "P" => "imprimante", "M" => "serveur d'impression", _ => A(0) }, "Réseau");
                    if (!A(0).Equals("P", StringComparison.OrdinalIgnoreCase) && !A(0).Equals("M", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Find(n, Severity.Error, $"^NP{A(0)} : P (imprimante) ou M (serveur d'impression) attendu.");
                    }

                    break;
                case ('^', "WS"):
                    ctx.Item(n, "ZPL", raw, $"Wi-Fi : réseau « {A(0)} »" + (A(1).Length > 0 ? ", mode " + (A(1).Equals("I", StringComparison.OrdinalIgnoreCase) ? "infrastructure" : A(1).Equals("A", StringComparison.OrdinalIgnoreCase) ? "ad hoc" : A(1)) : string.Empty), "Réseau");
                    if (A(0).Length > 0)
                    {
                        ctx.Set("wlan.essid", "Nom du réseau Wi-Fi (SSID)", A(0), n, comparable: true);
                    }

                    if (A(0).Length > 32)
                    {
                        ctx.Find(n, Severity.Error, $"^WS : nom de réseau (SSID) de {A(0).Length} caractères, 32 au plus.");
                    }

                    if (A(1).Length > 0 && !A(1).Equals("I", StringComparison.OrdinalIgnoreCase) && !A(1).Equals("A", StringComparison.OrdinalIgnoreCase))
                    {
                        ctx.Find(n, Severity.Error, $"^WS : mode « {A(1)} » inconnu (I infrastructure, A ad hoc).");
                    }

                    ctx.Risky = true;
                    break;
                case ('^', "WX"):
                {
                    var type = A(0) switch
                    {
                        "01" => "aucune",
                        "02" => "WEP 40 bits",
                        "03" => "WEP 128 bits",
                        "04" => "EAP-TLS",
                        "05" => "EAP-TTLS",
                        "06" => "EAP-FAST",
                        "07" => "PEAP",
                        "08" => "LEAP",
                        "09" => "WPA PSK",
                        "10" => "WPA EAP-TLS",
                        "11" => "WPA EAP-TTLS",
                        "12" => "WPA EAP-FAST",
                        "13" => "WPA PEAP",
                        "14" => "WPA LEAP",
                        "15" => "Kerberos",
                        _ => string.Empty,
                    };
                    ctx.Item(n, "ZPL", raw, "Sécurité Wi-Fi : " + (type.Length > 0 ? type : A(0)), "Réseau");
                    if (type.Length == 0)
                    {
                        ctx.Find(n, Severity.Error, $"^WX{A(0)} : type de sécurité inconnu (01 aucune, 02-03 WEP, 04-08 EAP / PEAP / LEAP, 09 WPA PSK, 10-14 WPA entreprise, 15 Kerberos).");
                    }
                    else if (A(0) is "02" or "03")
                    {
                        ctx.Find(n, Severity.Info, "Sécurité WEP : ancienne et refusée par la plupart des bornes Wi-Fi actuelles ; préfère WPA.");
                    }

                    if (a.Skip(1).Any(v => v.Trim().Length >= 8))
                    {
                        ctx.Find(n, Severity.Warning, "Clé ou mot de passe Wi-Fi écrit en clair dans ce fichier : ne le diffuse pas, et supprime-le après usage.");
                    }

                    ctx.Risky = true;
                    break;
                }

                case ('^', "WE"):
                    ctx.Item(n, "ZPL", raw, "Chiffrement WEP (Wi-Fi) : " + (A(0).Equals("OFF", StringComparison.OrdinalIgnoreCase) ? "désactivé" : A(0) + " bits"), "Réseau");
                    if (a.Skip(4).Any(v => v.Trim().Length > 0))
                    {
                        ctx.Find(n, Severity.Warning, "Clé WEP écrite en clair dans ce fichier : ne le diffuse pas, et supprime-le après usage.");
                    }

                    ctx.Risky = true;
                    break;
                case ('~', "WR"):
                    ctx.Item(n, "ZPL", raw, "Redémarrage de la carte Wi-Fi et du serveur d'impression", "Réseau");
                    ctx.Risky = true;
                    ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                    break;

                case ('^', "SC"):
                    ctx.Item(n, "ZPL", raw, $"Port série : {A(0)} bauds, {A(1)} bits, parité {A(2)}, {A(3)} bit(s) d'arrêt", "Communication");
                    break;
                case ('^', "CI"):
                    ctx.Item(n, "ZPL", raw, "Jeu de caractères " + A(0) + (A(0) == "28" ? " (UTF-8)" : string.Empty), "Impression");
                    break;
                case ('^', "KL"):
                    ctx.Item(n, "ZPL", raw, "Langue de l'écran", "Système");
                    break;
                case ('^', "SZ"):
                    ctx.Item(n, "ZPL", raw, A(0) == "1" ? "Mode ZPL I" : "Mode ZPL II", "Système");
                    if (A(0) == "1")
                    {
                        ctx.Find(n, Severity.Warning, "^SZ1 passe l'imprimante en ZPL I : la plupart des programmes actuels (ZPL II) s'impriment mal.");
                    }

                    break;
                case ('^', "JZ"):
                    ctx.Item(n, "ZPL", raw, A(0).Equals("N", StringComparison.OrdinalIgnoreCase) ? "Pas de réimpression après une erreur" : "Réimpression après une erreur", "Impression");
                    break;
                case ('~', "JA"):
                    ctx.Item(n, "ZPL", raw, "Annulation de tous les travaux en attente", "Système");
                    break;
                case ('^', "FX"):
                    ctx.Item(n, "ZPL", raw, "Commentaire", "Étiquette");
                    break;
                default:
                {
                    if (ZplKnown(prefix, code, out var meaning, out var category))
                    {
                        ctx.Item(n, "ZPL", raw, meaning, category);
                        if (category == "Réseau")
                        {
                            ctx.Risky = true;
                        }
                    }
                    else
                    {
                        ZplUnknown(ctx, n, prefix, code, raw);
                    }

                    break;
                }
            }
        }
    }

    private static string Resolution(string mode) => mode.ToUpperInvariant() switch
    {
        "A" => "automatique (toutes méthodes)",
        "B" => "BOOTP",
        "C" => "DHCP et BOOTP",
        "D" => "DHCP",
        "G" => "glanage",
        "R" => "RARP",
        "P" => "permanente (adresse fixe)",
        _ => mode.Length == 0 ? "inchangée" : mode,
    };

    private static void ZplSetting(Ctx ctx, int n)
    {
        ctx.FirstZplSettingLine = ctx.FirstZplSettingLine == 0 ? n : ctx.FirstZplSettingLine;
        ctx.LastZplSettingLine = n;
        ctx.LastZplSettingIndex = ctx.Items.Count;
    }

    // key : nom SGD Zebra (comparable à l'imprimante), ou « LANGAGE:COMMANDE », ou null (non retenu).
    private static void Range(Ctx ctx, int n, string raw, string label, string category, string value, double min, double max, string? key, bool integer = false, string language = "ZPL")
    {
        ctx.Item(n, language, raw, $"{label} : {value}", category);
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            ctx.Find(n, Severity.Error, $"{Short(raw)} : « {value} » n'est pas un nombre ({label.ToLowerInvariant()}).");
            return;
        }

        if ((integer && d != Math.Floor(d)) || d < min || d > max)
        {
            ctx.Find(n, Severity.Error, $"{Short(raw)} : {value} hors limites ({min.ToString(CultureInfo.InvariantCulture)} à {max.ToString(CultureInfo.InvariantCulture)}{(integer ? ", nombre entier" : string.Empty)}).");
        }

        if (key is not null)
        {
            ctx.Set(key, label, value, n, comparable: !key.Contains(':', StringComparison.Ordinal));
        }
    }

    // ------------------------------------------------------------------ TSPL (TSC)

    private static readonly double[] TsplSpeeds = [1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 6, 7, 8, 9, 10, 12, 14, 15, 18];

    private static void Tspl(string line, int n, Ctx ctx)
    {
        var m = TsplCommand().Match(line);
        if (!m.Success)
        {
            ctx.Find(n, Severity.Info, $"Ligne TSPL non reconnue : « {Short(line)} ».");
            return;
        }

        var cmd = m.Groups[1].Value.ToUpperInvariant();
        var arg = m.Groups[2].Value.Trim();
        switch (cmd)
        {
            case "SIZE":
                ctx.Item(n, "TSPL", line, $"Taille d'étiquette : {arg}", "Support");
                ctx.Set("TSPL:SIZE", "Taille d'étiquette", arg, n);
                break;
            case "GAP":
                ctx.Item(n, "TSPL", line, $"Espace entre étiquettes : {arg}" + (arg.StartsWith('0') ? " (support continu)" : string.Empty), "Support");
                ctx.Set("TSPL:GAP", "Espace entre étiquettes", arg, n);
                break;
            case "BLINE":
                ctx.Item(n, "TSPL", line, $"Marque noire : {arg}", "Support");
                break;
            case "SPEED":
                ctx.Item(n, "TSPL", line, $"Vitesse d'impression : {arg} pouces par seconde", "Impression");
                ctx.Set("TSPL:SPEED", "Vitesse d'impression", arg, n);
                if (!double.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed))
                {
                    ctx.Find(n, Severity.Error, $"SPEED {arg} : nombre attendu (le séparateur décimal est le point).");
                }
                else if (!TsplSpeeds.Contains(speed))
                {
                    ctx.Find(n, Severity.Warning, $"SPEED {arg} : vitesse inhabituelle ; chaque modèle n'accepte que certaines valeurs (ex. 2, 3, 4, 5, 6) et prend sinon la plus proche.");
                }

                break;
            case "DENSITY":
                Range(ctx, n, line, "Noirceur (densité)", "Impression", arg, 0, 15, "TSPL:DENSITY", integer: true, language: "TSPL");
                break;
            case "DIRECTION":
            {
                var d = arg.Split(',').Select(s => s.Trim()).ToArray();
                ctx.Item(n, "TSPL", line, "Sens d'impression" + (d.Length > 1 && d[1] == "1" ? " (miroir)" : string.Empty), "Impression");
                if (d.Any(x => x is not ("0" or "1")))
                {
                    ctx.Find(n, Severity.Error, "DIRECTION : 0 ou 1 attendu (et 0 ou 1 pour le miroir).");
                }

                break;
            }

            case "REFERENCE" or "OFFSET" or "SHIFT":
                ctx.Item(n, "TSPL", line, cmd switch { "REFERENCE" => "Origine de l'étiquette", "OFFSET" => "Décalage d'arrêt après impression", _ => "Décalage d'impression" } + $" : {arg}", "Support");
                break;
            case "CODEPAGE":
                ctx.Item(n, "TSPL", line, $"Jeu de caractères : {arg}", "Impression");
                break;
            case "COUNTRY":
                ctx.Item(n, "TSPL", line, $"Pays (clavier, caractères) : {arg}", "Système");
                break;
            case "LIMITFEED":
                ctx.Item(n, "TSPL", line, $"Longueur maximale avant erreur de détection : {arg}", "Support");
                break;
            case "SET":
                TsplSet(arg, line, n, ctx);
                break;
            case "INITIALPRINTER":
                ctx.Item(n, "TSPL", line, "Retour aux réglages d'usine", "Système");
                ctx.Risky = true;
                ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
                ctx.Find(n, Severity.Warning, "INITIALPRINTER : retour aux réglages d'usine, les réglages actuels sont perdus.");
                break;
            case "GAPDETECT" or "BLINEDETECT" or "AUTODETECT":
                ctx.Item(n, "TSPL", line, "Calibrage du capteur d'étiquettes", "Support");
                break;
            case "SELFTEST":
                ctx.Item(n, "TSPL", line, "Impression de l'étiquette d'autotest (configuration)", "Lecture");
                break;
            case "NET":
                ctx.Risky = true;
                ctx.Item(n, "TSPL", line, $"Réseau : {arg}", "Réseau");
                foreach (Match ip in IpInQuotes().Matches(arg))
                {
                    if (!IsIpv4(ip.Groups[1].Value))
                    {
                        ctx.Find(n, Severity.Error, $"NET : adresse « {ip.Groups[1].Value} » invalide.");
                    }
                }

                break;
            case "CLS" or "PRINT" or "TEXT" or "BARCODE" or "QRCODE" or "BOX" or "BAR" or "BITMAP" or "PUTBMP" or "BLOCK" or "DMATRIX" or "PDF417" or "ERASE" or "REVERSE":
                ctx.Item(n, "TSPL", line, "Contenu d'étiquette", "Étiquette");
                break;
            case "HOME" or "FORMFEED" or "FEED" or "BACKFEED" or "BACKUP" or "CUT" or "EOP":
                ctx.Item(n, "TSPL", line, "Mouvement du support", "Support");
                break;
            default:
                ctx.Item(n, "TSPL", line, $"Commande TSPL {cmd}", "Système");
                ctx.Find(n, Severity.Info, $"{cmd} : commande TSPL non répertoriée par Scanalyse, vérifie-la dans le manuel du modèle.");
                break;
        }
    }

    private static void TsplSet(string arg, string line, int n, Ctx ctx)
    {
        var m = TsplSetArg().Match(arg);
        var key = m.Success ? m.Groups[1].Value.ToUpperInvariant() : arg.ToUpperInvariant();
        var value = m.Success ? m.Groups[2].Value.Trim() : string.Empty;
        var (label, category, allowed) = key switch
        {
            "CUTTER" => ("Massicot (OFF, BATCH ou toutes les n étiquettes)", "Impression", (string[]?)null),
            "PARTIAL_CUTTER" => ("Coupe partielle", "Impression", null),
            "BACK" => ("Recul après coupe", "Impression", ["ON", "OFF"]),
            "TEAR" or "STRIPER" => ("Position de détachage", "Impression", ["ON", "OFF"]),
            "PEEL" => ("Prédécollage", "Impression", ["ON", "OFF"]),
            "GAP" => ("Détection de l'espace", "Support", null),
            "HEAD" => ("Contrôle d'ouverture de la tête", "Support", ["ON", "OFF"]),
            "RIBBON" => ("Ruban (transfert thermique ON, thermique direct OFF)", "Support", ["ON", "OFF"]),
            "REWIND" => ("Enrouleur", "Impression", ["ON", "OFF", "RS232"]),
            "COUNTER" => ("Compteur", "Système", null),
            "KEY1" or "KEY2" or "KEY" => ("Touche du panneau", "Système", null),
            "LED1" or "LED2" or "LED3" => ("Voyant", "Système", null),
            "REPRINT" => ("Réimpression après une erreur", "Impression", ["ON", "OFF"]),
            "PRINTKEY" => ("Impression par la touche FEED", "Impression", null),
            "COM1" => ("Port série (bauds, parité, bits, arrêt)", "Communication", null),
            "ENCODER" => ("Détection du ruban (encodeur)", "Support", ["ON", "OFF"]),
            "SENSOR" => ("Capteur", "Support", null),
            _ => ($"Réglage {key}", "Système", null),
        };
        ctx.Item(n, "TSPL", line, $"{label} : {value}", category);
        ctx.Set("TSPL:SET " + key, label, value, n);
        if (allowed is not null && !allowed.Contains(value.ToUpperInvariant().Trim('"'), StringComparer.Ordinal))
        {
            ctx.Find(n, Severity.Error, $"SET {key} {value} : {string.Join(" ou ", allowed)} attendu.");
        }

        if (key is "PEEL" or "CUTTER" && value.ToUpperInvariant() is not ("OFF" or ""))
        {
            ctx.Find(n, Severity.Info, $"SET {key} : l'option (décolleur ou massicot) doit être installée, sinon l'imprimante reste en erreur.");
        }
    }

    // ------------------------------------------------------------------ EPL2

    private static void Epl2(string line, int n, Ctx ctx)
    {
        if (line is "N")
        {
            ctx.Item(n, "EPL2", line, "Effacement de la mémoire d'image", "Étiquette");
            return;
        }

        if (line is "ZT" or "ZB")
        {
            ctx.Item(n, "EPL2", line, line == "ZT" ? "Impression depuis le haut" : "Impression depuis le bas (retournée)", "Impression");
            return;
        }

        if (line is "^@")
        {
            ctx.Item(n, "EPL2", line, "Redémarrage de l'imprimante", "Système");
            ctx.Risky = true;
            ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
            return;
        }

        var cmd = line.Length >= 2 && line[..2] is "JF" or "JB" or "UQ" or "xa" or "LO" or "LE" or "LW" or "GW" or "oR" ? line[..2] : line[..1];
        var arg = line[cmd.Length..].Trim();
        switch (cmd)
        {
            case "q":
                Range(ctx, n, line, "Largeur d'étiquette (points)", "Support", arg, 1, 5000, "EPL2:q", integer: true, language: "EPL2");
                break;
            case "Q":
                ctx.Item(n, "EPL2", line, $"Longueur d'étiquette et espace : {arg}", "Support");
                break;
            case "S":
                Range(ctx, n, line, "Vitesse d'impression (code)", "Impression", arg, 0, 6, "EPL2:S", integer: true, language: "EPL2");
                break;
            case "D":
                Range(ctx, n, line, "Noirceur (densité)", "Impression", arg, 0, 15, "EPL2:D", integer: true, language: "EPL2");
                break;
            case "O":
                ctx.Item(n, "EPL2", line, "Options matérielles" + (arg.Contains('D', StringComparison.Ordinal) ? " (thermique direct)" : string.Empty)
                    + (arg.Contains('P', StringComparison.Ordinal) ? " (prédécollage)" : string.Empty) + (arg.Contains('C', StringComparison.Ordinal) ? " (massicot)" : string.Empty), "Impression");
                break;
            case "JF" or "JB":
                ctx.Item(n, "EPL2", line, cmd == "JF" ? "Recul avant impression activé" : "Recul avant impression désactivé", "Support");
                break;
            case "I":
                ctx.Item(n, "EPL2", line, $"Jeu de caractères : {arg}", "Impression");
                break;
            case "R":
                ctx.Item(n, "EPL2", line, $"Origine de l'étiquette : {arg}", "Support");
                break;
            case "xa":
                ctx.Item(n, "EPL2", line, "Calibrage automatique du capteur", "Support");
                break;
            case "U" or "UQ":
                ctx.Item(n, "EPL2", line, "Demande de configuration", "Lecture");
                break;
            case "P":
                ctx.Item(n, "EPL2", line, "Impression", "Étiquette");
                break;
            case "A" or "B" or "b" or "X" or "LO" or "LE" or "LW" or "GW":
                ctx.Item(n, "EPL2", line, "Contenu d'étiquette", "Étiquette");
                break;
            default:
                ctx.Item(n, "EPL2", line, $"Commande EPL2 {cmd}", "Système");
                break;
        }
    }

    // ------------------------------------------------------------------ DPL (Honeywell / Datamax)

    private static void Dpl(string line, int n, Ctx ctx)
    {
        var soh = line[0] == '\u0001';
        var body = line.TrimStart('\u0002', '\u0001');
        if (body.Length == 0)
        {
            return;
        }

        if (soh)
        {
            var (meaning, category) = body[0] switch
            {
                '#' => ("Redémarrage de l'imprimante", "Système"),
                'A' or 'F' => ("Demande d'état", "Lecture"),
                'B' => ("Arrêt immédiat (annulation)", "Système"),
                'C' => ("Annulation de tous les travaux", "Système"),
                _ => ($"Commande immédiate <SOH>{body[0]}", "Système"),
            };
            ctx.Item(n, "DPL", "<SOH>" + body, meaning, category);
            if (body[0] == '#')
            {
                ctx.Risky = true;
                ctx.ResetLine = ctx.ResetLine == 0 ? n : ctx.ResetLine;
            }

            return;
        }

        var display = "<STX>" + body;
        if (line[0] != '\u0002')
        {
            // Lignes du format d'étiquette (après <STX>L) : H chaleur, P/S vitesse, D taille des points, Q quantité, E fin.
            var c = body[0];
            var rest = body[1..];
            switch (c)
            {
                case 'H' when rest.Length >= 2 && rest.All(char.IsAsciiDigit):
                    Range(ctx, n, body, "Chaleur (noirceur) du format", "Impression", rest, 0, 30, "DPL:H", integer: true, language: "DPL");
                    return;
                case 'D' when rest.Length == 2 && rest.All(char.IsAsciiDigit):
                    ctx.Item(n, "DPL", body, $"Taille des points : {rest[0]} × {rest[1]}", "Impression");
                    return;
                case 'P' or 'S' when rest.Length == 1:
                    ctx.Item(n, "DPL", body, (c == 'P' ? "Vitesse d'impression" : "Vitesse d'avance") + $" (code {rest})", "Impression");
                    return;
                case 'Q' when rest.All(char.IsAsciiDigit):
                    ctx.Item(n, "DPL", body, $"Quantité : {rest.TrimStart('0')}", "Étiquette");
                    return;
                case 'E' when rest.Length == 0:
                    ctx.Item(n, "DPL", body, "Fin du format (impression)", "Étiquette");
                    return;
                default:
                    ctx.Item(n, "DPL", body, "Contenu d'étiquette", "Étiquette");
                    return;
            }
        }

        var cmd = body[0];
        var arg = body[1..];
        switch (cmd)
        {
            case 'L':
                ctx.Item(n, "DPL", display, "Début d'un format d'étiquette", "Étiquette");
                break;
            case 'm' or 'n':
                ctx.Item(n, "DPL", display, cmd == 'm' ? "Unités métriques (0,1 mm)" : "Unités en pouces (0,01 pouce)", "Support");
                break;
            case 'e':
                ctx.Item(n, "DPL", display, "Capteur d'espace (étiquettes séparées)", "Support");
                ctx.Set("DPL:SENSOR", "Capteur de support", "espace", n);
                break;
            case 'r':
                ctx.Item(n, "DPL", display, "Capteur réfléchissant (marque noire)", "Support");
                ctx.Set("DPL:SENSOR", "Capteur de support", "marque noire", n);
                break;
            case 'c':
                ctx.Item(n, "DPL", display, $"Support continu, longueur {arg}", "Support");
                ctx.Set("DPL:SENSOR", "Capteur de support", "continu", n);
                break;
            case 'M':
                ctx.Item(n, "DPL", display, $"Longueur maximale d'étiquette : {arg}", "Support");
                break;
            case 'O':
                ctx.Item(n, "DPL", display, $"Décalage du début d'impression : {arg}", "Support");
                break;
            case 'f':
                ctx.Item(n, "DPL", display, $"Position d'arrêt (détachage) : {arg}", "Support");
                break;
            case 'K' when arg.StartsWith('C'):
                ctx.Item(n, "DPL", display, "Demande de la configuration", "Lecture");
                break;
            case 'K' when arg.StartsWith('c'):
                ctx.Item(n, "DPL", display, "Modification de la configuration enregistrée (Kc)", "Système");
                ctx.Find(n, Severity.Info, "<STX>Kc : réglages enregistrés dans l'imprimante ; Scanalyse ne détaille pas chaque paramètre, vérifie-les dans le manuel DPL du modèle.");
                break;
            case 'Z':
                ctx.Item(n, "DPL", display, "Impression de l'étiquette de configuration", "Lecture");
                break;
            case 'A':
                ctx.Item(n, "DPL", display, "Réglage de l'horloge", "Système");
                break;
            default:
                ctx.Item(n, "DPL", display, $"Commande système DPL <STX>{cmd}", "Système");
                break;
        }
    }

    // ------------------------------------------------------------------ Fingerprint / Direct Protocol

    private static void Fingerprint(string line, int n, Ctx ctx)
    {
        var body = FingerprintLineNumber().Replace(line, string.Empty);
        var m = FingerprintSetup().Match(body);
        if (!m.Success)
        {
            if (body.StartsWith("SETUP WRITE", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Item(n, "Fingerprint", body, "Copie de la configuration vers un fichier ou un port", "Lecture");
                return;
            }

            ctx.Item(n, "Fingerprint", body, "Instruction Fingerprint", body.StartsWith("PRBAR", StringComparison.OrdinalIgnoreCase) || body.StartsWith("PRTXT", StringComparison.OrdinalIgnoreCase) ? "Étiquette" : "Système");
            return;
        }

        var parts = SplitQuoted(m.Groups[1].Value);
        var section = parts.FirstOrDefault()?.Trim('"').ToUpperInvariant() ?? string.Empty;
        var category = section switch
        {
            "MEDIA" => "Support",
            "PRINT DEFS" => "Impression",
            "SER-COM" or "SER-COM,UART1" or "SER-COM,UART2" => "Communication",
            "NETWORK" or "NET1" or "WLAN" => "Réseau",
            _ => "Système",
        };
        var value = parts.Count > 1 ? parts[^1].Trim().Trim('"') : string.Empty;
        var key = string.Join(" / ", parts.Take(Math.Max(1, parts.Count - 1)).Select(p => p.Trim().Trim('"')));
        ctx.Item(n, "Fingerprint", body, $"{key} → {value}", category);
        ctx.Set("FP:" + key, key, value, n);
        if (category == "Réseau")
        {
            ctx.Risky = true;
        }

        if (parts.Count < 2)
        {
            ctx.Find(n, Severity.Error, "SETUP : section, réglage et valeur attendus, entre guillemets et séparés par des virgules (ex. SETUP \"MEDIA\",\"MEDIA TYPE\",\"LABEL (W GAPS)\").");
        }

        if (m.Groups[1].Value.Count(c => c == '"') % 2 != 0 || parts.Any(p => p.Trim().StartsWith('"') != p.Trim().EndsWith('"')))
        {
            ctx.Find(n, Severity.Error, "SETUP : guillemet en trop ou non refermé ; chaque section, réglage et valeur s'écrit entre guillemets, séparés par des virgules.");
        }
    }

    private static List<string> SplitQuoted(string s)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        var quoted = false;
        foreach (var c in s)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }

            if (c == ',' && !quoted)
            {
                list.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        if (cur.Length > 0)
        {
            list.Add(cur.ToString());
        }

        return list;
    }

    // ------------------------------------------------------------------ SBPL (SATO)

    private static void Sbpl(string text, Ctx ctx)
    {
        var line = 1;
        var started = false;
        foreach (var seg in text.Split('\u001B'))
        {
            var n = line;
            line += seg.Count(c => c == '\n');
            var s = seg.TrimEnd('\r', '\n', '\u0002', '\u0003');
            if (s.Length == 0)
            {
                continue;
            }

            var two = s.Length >= 2 ? s[..2] : s;
            switch (two)
            {
                case "CS":
                {
                    var v = s[2..].Trim();
                    ctx.Item(n, "SBPL", "<ESC>" + s, $"Vitesse d'impression (code {v})", "Impression");
                    ctx.Set("SBPL:CS", "Vitesse d'impression (code)", v, n);
                    if (!started)
                    {
                        ctx.Find(n, Severity.Warning, "<ESC>CS hors d'un bloc <ESC>A … <ESC>Z : la commande est ignorée.");
                    }

                    ctx.Find(n, Severity.Info, "<ESC>CS n'est pas enregistrée : la vitesse revient au réglage du panneau à l'extinction (enregistrement : <ESC>PG ou menu de l'imprimante). Code de vitesse propre au modèle.");
                    break;
                }

                case "#E":
                {
                    var v = s[2..].Trim();
                    ctx.Item(n, "SBPL", "<ESC>" + s, $"Noirceur (niveau {v})", "Impression");
                    ctx.Set("SBPL:#E", "Noirceur", v, n);
                    if (v.Length == 0 || v[0] is < '1' or > '5' || (v.Length > 1 && v[1] is < 'A' or > 'F'))
                    {
                        ctx.Find(n, Severity.Error, $"<ESC>#E{v} : niveau 1 à 5 (1 à 3 sur certains modèles) suivi éventuellement d'une lettre A à F.");
                    }

                    break;
                }

                case "IG":
                {
                    var v = s[2..].Trim();
                    var meaning = v switch { "1" => "réflexion (marque noire)", "2" => "transmission (espace entre étiquettes)", "3" => "capteur ignoré", _ => "?" };
                    ctx.Item(n, "SBPL", "<ESC>" + s, $"Capteur : {meaning}", "Support");
                    if (meaning == "?")
                    {
                        ctx.Find(n, Severity.Error, $"<ESC>IG{v} : 1 (réflexion), 2 (transmission) ou 3 (ignoré) attendu.");
                    }

                    break;
                }

                case "A3":
                    ctx.Item(n, "SBPL", "<ESC>" + s, "Correction du point de départ (non enregistrée)", "Support");
                    break;
                case "A1":
                    ctx.Item(n, "SBPL", "<ESC>" + s, "Taille d'étiquette", "Support");
                    break;
                case "PG":
                    ctx.Item(n, "SBPL", "<ESC>" + s, "Enregistrement des réglages dans la mémoire de l'imprimante", "Système");
                    ctx.Risky = true;
                    ctx.Find(n, Severity.Info, "<ESC>PG enregistre les réglages (vitesse, noirceur, capteur, taille…) de façon permanente : champs à position fixe, vérifie-les dans le manuel SBPL du modèle.");
                    break;
                default:
                    if (s[0] == 'A' && s.Length == 1)
                    {
                        started = true;
                        ctx.Item(n, "SBPL", "<ESC>A", "Début de bloc", "Étiquette");
                    }
                    else if (s[0] == 'Z' && s.Length == 1)
                    {
                        started = false;
                        ctx.Item(n, "SBPL", "<ESC>Z", "Fin de bloc (exécution)", "Étiquette");
                    }
                    else
                    {
                        ctx.Item(n, "SBPL", "<ESC>" + Short(s), "Contenu d'étiquette ou commande SBPL", "Étiquette");
                    }

                    break;
            }
        }
    }

    // ------------------------------------------------------------------ Imprimante (Zebra) : lecture et comparaison

    /// <summary>Lit la réponse de « ! U1 getvar "allcv" » : une ligne « nom : valeur , Choices: … » par réglage.</summary>
    public static IReadOnlyDictionary<string, PrinterValue> ParseAllcv(string reply)
    {
        ArgumentNullException.ThrowIfNull(reply);
        var map = new Dictionary<string, PrinterValue>(StringComparer.Ordinal);
        foreach (var raw in reply.Split('\n'))
        {
            var line = raw.Trim().Trim('"').Trim();
            var colon = line.IndexOf(" : ", StringComparison.Ordinal);
            if (colon <= 0)
            {
                continue;
            }

            var name = line[..colon].Trim();
            if (name.Contains(' ', StringComparison.Ordinal) || !name.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            var rest = line[(colon + 3)..];
            var choicesAt = rest.LastIndexOf(", Choices:", StringComparison.Ordinal);
            var value = (choicesAt >= 0 ? rest[..choicesAt] : rest).Trim();
            var choices = choicesAt >= 0 ? rest[(choicesAt + 10)..].Trim() : null;
            map[name] = new PrinterValue(value, string.IsNullOrEmpty(choices) ? null : choices);
        }

        return map;
    }

    /// <summary>Compare les réglages du fichier à ceux lus sur l'imprimante (valeurs et choix acceptés par ce modèle).</summary>
    public static IReadOnlyList<ConfigFinding> Compare(ConfigReport report, IReadOnlyDictionary<string, PrinterValue> printer)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(printer);
        var list = new List<ConfigFinding>();
        foreach (var s in report.Settings.Where(s => s.Comparable))
        {
            if (!printer.TryGetValue(s.Key, out var current))
            {
                list.Add(new ConfigFinding(s.Line, Severity.Warning, $"« {s.Key} » n'existe pas sur cette imprimante (modèle ou firmware) : le réglage sera ignoré."));
                continue;
            }

            if (current.Choices is { } choices && !Accepts(choices, s.Value))
            {
                list.Add(new ConfigFinding(s.Line, Severity.Error, $"{s.Label} : « {s.Value} » refusé par cette imprimante (valeurs acceptées : {choices})."));
            }
            else if (SameValue(current.Value, s.Value))
            {
                list.Add(new ConfigFinding(s.Line, Severity.Ok, $"{s.Label} : déjà réglé sur « {current.Value} »."));
            }
            else
            {
                list.Add(new ConfigFinding(s.Line, Severity.Info, $"{s.Label} : « {current.Value} » sur l'imprimante → « {s.Value} » après envoi."));
            }
        }

        return list;
    }

    /// <summary>La valeur fait partie des choix annoncés (« on,off » ou plage « 0.0-30.0 »).</summary>
    public static bool Accepts(string choices, string value)
    {
        ArgumentNullException.ThrowIfNull(choices);
        ArgumentNullException.ThrowIfNull(value);
        var v = value.Trim();
        var range = RangeChoice().Match(choices.Trim());
        if (range.Success)
        {
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                && d >= double.Parse(range.Groups[1].Value, CultureInfo.InvariantCulture) && d <= double.Parse(range.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        return choices.Split(',').Select(c => c.Trim()).Contains(v, StringComparer.Ordinal);
    }

    private static bool SameValue(string a, string b) =>
        double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            ? Math.Abs(x - y) < 0.0001
            : string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);

    /// <summary>Commande qui demande à une Zebra tous ses réglages (réponse : <see cref="ParseAllcv"/>).</summary>
    public static byte[] AllcvRequest => Encoding.ASCII.GetBytes("! U1 getvar \"allcv\"\r\n");

    [GeneratedRegex(@"^!\s+(U1?|u1?)\s+(\w+)\s+""([^""]*)""(\s+""([^""]*)"")?\s*$")]
    private static partial Regex SgdLine();

    [GeneratedRegex(@"([\^~])([A-Za-z0-9@][A-Za-z0-9@]?)([^\^~]*)")]
    private static partial Regex ZplToken();

    [GeneratedRegex(@"^([A-Za-z_]+)\b\s*(.*)$")]
    private static partial Regex TsplCommand();

    [GeneratedRegex(@"^([A-Za-z0-9_]+)\s*(.*)$")]
    private static partial Regex TsplSetArg();

    [GeneratedRegex(@"^(SET\s+(CUTTER|PEEL|TEAR|GAP|HEAD|RIBBON|COUNTER|REPRINT|PRINTKEY|COM1|BACK|PARTIAL_CUTTER)|INITIALPRINTER|GAPDETECT|BLINEDETECT|AUTODETECT|DENSITY\s+\d|SPEED\s+\d|LIMITFEED|NET\s)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TsplConfigLine();

    [GeneratedRegex(@"^(?:\d+\s+)?SETUP\s+(""[^\n]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintSetup();

    [GeneratedRegex(@"^\s*\d+\s+")]
    private static partial Regex FingerprintLineNumber();

    [GeneratedRegex(@"""([0-9.]+)""")]
    private static partial Regex IpInQuotes();

    [GeneratedRegex(@"^(-?[0-9.]+)-(-?[0-9.]+)$")]
    private static partial Regex RangeChoice();
}
