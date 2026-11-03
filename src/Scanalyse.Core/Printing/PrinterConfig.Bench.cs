using System.Globalization;
using System.Text;

namespace Scanalyse.Core.Printing;

/// <summary>Écart entre deux imprimantes (ou entre une imprimante et un état enregistré) pour un réglage.</summary>
/// <param name="Key">Nom du réglage.</param>
/// <param name="Reference">Valeur sur l'imprimante de référence (null : réglage absent).</param>
/// <param name="Target">Valeur sur l'autre imprimante (null : réglage absent).</param>
/// <param name="Use">Ce qu'on peut faire du réglage (repris ou non dans une copie).</param>
public sealed record SettingDiff(string Key, string? Reference, string? Target, SettingUse Use)
{
    /// <summary>Les deux imprimantes ont ce réglage, avec la même valeur.</summary>
    public bool Same => Reference is not null && Target is not null && string.Equals(Reference, Target, StringComparison.Ordinal);
}

/// <summary>Valeurs qu'un réglage accepte, telles que l'imprimante les annonce.</summary>
/// <param name="Options">Liste de valeurs (« on, off »), vide pour une plage ou une valeur libre.</param>
/// <param name="Min">Minimum d'une plage (« 0.0-30.0 »).</param>
/// <param name="Max">Maximum d'une plage.</param>
public sealed record SettingChoices(IReadOnlyList<string> Options, double? Min, double? Max);

/// <summary>Plan de travail sur les réglages lus : familles, choix, comparaison de deux imprimantes, copie, états enregistrés.</summary>
public static partial class PrinterConfig
{
    /// <summary>Familles affichées, dans l'ordre.</summary>
    public static IReadOnlyList<string> SettingFamilies { get; } =
        ["Impression", "Support", "Réseau", "Wi-Fi", "Bluetooth", "Interfaces", "Langages", "RFID", "Système", "Autres"];

    /// <summary>Famille lisible d'un réglage (« media.speed » → « Support », « wlan.ssid » → « Wi-Fi »).</summary>
    public static string FamilyOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var head = key.Split('.')[0].ToLowerInvariant();
        switch (head)
        {
            case "wlan" or "wifi":
                return "Wi-Fi";
            case "bluetooth":
                return "Bluetooth";
            case "rfid":
                return "RFID";
            case "ip" or "internal_wired" or "external_wired" or "tcp" or "http" or "netbios" or "snmp" or "smtp" or "ftp" or "lpd" or "telnet"
                or "weblink" or "cloud" or "dns" or "netmanage" or "alerts" or "mirror" or "card":
                return "Réseau";
            case "usb" or "comm" or "parallel_port" or "interface" or "serial" or "input" or "odometer" or "sensor":
                return "Interfaces";
            case "zpl" or "epl" or "apl" or "zbi" or "cpcl" or "line_print" or "xml" or "pdf" or "virtual":
                return "Langages";
            case "media" or "ezpl" or "cutter" or "rewind" or "applicator" or "ribbon":
                return "Support";
            case "print" or "head" or "label":
                return "Impression";
        }

        if (SgdTable.TryGetValue(key, out var def))
        {
            return def.Category switch
            {
                "Impression" or "Étiquette" => "Impression",
                "Support" => "Support",
                "Réseau" => "Réseau",
                _ => "Système",
            };
        }

        return head is "device" or "appl" or "power" or "display" or "power_save" or "file" or "format" or "capture" or "memory" or "rtc" or "log" ? "Système" : "Autres";
    }

    /// <summary>Choix annoncés par l'imprimante : liste de valeurs, plage, ou null (valeur libre ou inconnue).</summary>
    public static SettingChoices? ChoicesOf(PrinterValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Choices is not { Length: > 0 } c)
        {
            return null;
        }

        var range = RangeChoice().Match(c.Trim());
        if (range.Success
            && double.TryParse(range.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var min)
            && double.TryParse(range.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var max))
        {
            return new SettingChoices([], min, max);
        }

        var options = c.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return options.Count >= 2 && options.Count <= 60 ? new SettingChoices(options, null, null) : null;
    }

    /// <summary>
    /// Compare deux imprimantes réglage par réglage (référence → autre). Les informations en lecture seule (compteurs, état,
    /// versions) changent forcément d'une machine à l'autre : elles ne sont comparées que sur demande.
    /// </summary>
    public static IReadOnlyList<SettingDiff> Diff(IReadOnlyDictionary<string, PrinterValue> reference, IReadOnlyDictionary<string, PrinterValue> target, bool includeReadOnly = false)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        var list = new List<SettingDiff>();
        foreach (var key in reference.Keys.Union(target.Keys, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            reference.TryGetValue(key, out var a);
            target.TryGetValue(key, out var b);
            var use = Classify(key, a ?? b!);
            if (use == SettingUse.ReadOnly && !includeReadOnly)
            {
                continue;
            }

            list.Add(new SettingDiff(key, a is null ? null : Unquote(a.Value), b is null ? null : Unquote(b.Value), use));
        }

        return list;
    }

    /// <summary>
    /// Fichier « ! U1 setvar » qui donne à l'autre imprimante les réglages de la référence : seulement ce qui diffère, existe
    /// sur l'autre modèle et y est accepté ; jamais les adresses, noms, secrets ni les actions.
    /// </summary>
    /// <returns>Le fichier, le nombre de réglages écrits, et ceux que l'autre imprimante ne connaît pas ou refuserait.</returns>
    public static (string Text, int Written, IReadOnlyList<string> Refused) CopyFile(IReadOnlyDictionary<string, PrinterValue> reference, IReadOnlyDictionary<string, PrinterValue> target)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(target);
        var sb = new StringBuilder();
        var refused = new List<string>();
        var written = 0;
        foreach (var (key, a) in reference.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (Classify(key, a) != SettingUse.Portable)
            {
                continue;
            }

            var value = Unquote(a.Value);
            if (!target.TryGetValue(key, out var b) || (b.Choices is { } choices && !Accepts(choices, value)))
            {
                refused.Add(key);
                continue;
            }

            if (SameValue(Unquote(b.Value), value))
            {
                continue;
            }

            sb.Append(SetvarLine(key, a)).Append("\r\n");
            written++;
        }

        return (sb.ToString(), written, refused);
    }

    /// <summary>
    /// État enregistré d'une imprimante : la réponse « allcv » précédée d'un en-tête (modèle, date). Se relit avec
    /// <see cref="ParseAllcv"/>, pour comparer plus tard ou copier vers une autre imprimante.
    /// </summary>
    public static string SaveState(IReadOnlyDictionary<string, PrinterValue> settings, string printerName, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(printerName);
        var sb = new StringBuilder();
        sb.Append("# Scanalyse - etat de l'imprimante ").Append(printerName.Replace(" : ", " - ", StringComparison.Ordinal))
            .Append(" - ").Append(when.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append("\r\n");
        foreach (var (key, v) in settings.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(key).Append(" : ").Append(v.Value);
            if (v.Choices is { Length: > 0 } c)
            {
                sb.Append(" , Choices: ").Append(c);
            }

            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Nom de l'imprimante noté dans l'en-tête d'un état enregistré, sinon null.</summary>
    public static string? StateName(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        const string Head = "# Scanalyse - etat de l'imprimante ";
        var first = text.Split('\n')[0].Trim();
        return first.StartsWith(Head, StringComparison.Ordinal) ? first[Head.Length..] : null;
    }
}
