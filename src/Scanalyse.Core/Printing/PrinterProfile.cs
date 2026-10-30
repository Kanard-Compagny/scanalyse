using System.Text.RegularExpressions;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

/// <summary>Marque d'imprimante d'étiquettes.</summary>
public enum PrinterBrand
{
    Unknown,
    Zebra,
    Tsc,
    Honeywell,
    Intermec,
    Datamax,
    Sato,
    Toshiba,
}

/// <summary>
/// Ce que Scanalyse sait de l'imprimante choisie : marque, modèle, firmware, langages actifs et, pour une Zebra, tous ses
/// réglages avec leurs valeurs acceptées. Sert de référence à l'analyse du fichier (compatibilité, noms, valeurs).
/// </summary>
/// <param name="Brand">Marque reconnue.</param>
/// <param name="Model">Modèle (ex. ZT411, TE210, PC42t), si connu.</param>
/// <param name="Firmware">Version du firmware, si connue.</param>
/// <param name="Languages">Langages actifs annoncés par l'imprimante (vide si inconnus).</param>
/// <param name="Settings">Réglages lus (Zebra : « allcv ») ; vide pour les autres marques.</param>
/// <param name="Source">D'où viennent ces informations (réseau, SNMP, pilote Windows…).</param>
public sealed record PrinterProfile(
    PrinterBrand Brand,
    string? Model,
    string? Firmware,
    IReadOnlyList<string> Languages,
    IReadOnlyDictionary<string, PrinterValue> Settings,
    string Source)
{
    /// <summary>Nom affiché : « Zebra ZT411 » ou la marque seule.</summary>
    public string DisplayName
    {
        get
        {
            var brand = PrinterConfig.BrandName(Brand);
            if (string.IsNullOrWhiteSpace(Model))
            {
                return brand;
            }

            return Model.Contains(brand, StringComparison.OrdinalIgnoreCase) || Brand == PrinterBrand.Unknown ? Model.Trim() : $"{brand} {Model.Trim()}";
        }
    }
}

public static partial class PrinterConfig
{
    /// <summary>Nom de la marque, pour l'affichage.</summary>
    public static string BrandName(PrinterBrand brand) => brand switch
    {
        PrinterBrand.Zebra => "Zebra",
        PrinterBrand.Tsc => "TSC",
        PrinterBrand.Honeywell => "Honeywell",
        PrinterBrand.Intermec => "Intermec",
        PrinterBrand.Datamax => "Datamax",
        PrinterBrand.Sato => "SATO",
        PrinterBrand.Toshiba => "Toshiba",
        _ => "Imprimante",
    };

    /// <summary>Marque d'après un texte : description SNMP, nom du pilote Windows ou de l'imprimante installée.</summary>
    public static PrinterBrand DetectBrand(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PrinterBrand.Unknown;
        }

        var t = text.ToUpperInvariant();
        return t switch
        {
            _ when t.Contains("ZEBRA", StringComparison.Ordinal) || t.Contains("ZDESIGNER", StringComparison.Ordinal) || ZebraModel().IsMatch(t) => PrinterBrand.Zebra,
            _ when t.Contains("DATAMAX", StringComparison.Ordinal) => PrinterBrand.Datamax,
            _ when t.Contains("INTERMEC", StringComparison.Ordinal) => PrinterBrand.Intermec,
            _ when t.Contains("HONEYWELL", StringComparison.Ordinal) => PrinterBrand.Honeywell,
            _ when t.Contains("SATO", StringComparison.Ordinal) => PrinterBrand.Sato,
            _ when t.Contains("TOSHIBA", StringComparison.Ordinal) || ToshibaModel().IsMatch(t) => PrinterBrand.Toshiba,
            _ when TscName().IsMatch(t) => PrinterBrand.Tsc,
            _ => PrinterBrand.Unknown,
        };
    }

    /// <summary>
    /// Langages d'après des descriptions d'interpréteurs (MIB imprimante, réglage Zebra « device.languages ») :
    /// noms tels qu'utilisés par l'analyse (ZPL, EPL2, TSPL, DPL, Fingerprint, SBPL).
    /// </summary>
    public static IReadOnlyList<string> LanguagesFrom(IEnumerable<string> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var d in descriptions)
        {
            var t = d.ToUpperInvariant();
            if (t.Contains("ZPL", StringComparison.Ordinal) || t.Contains("ZSIM", StringComparison.Ordinal) || t.Contains("PL-Z", StringComparison.Ordinal))
            {
                found.Add("ZPL");
            }

            if (t.Contains("EPL", StringComparison.Ordinal) || t.Contains("ESIM", StringComparison.Ordinal) || t.Contains("PL-E", StringComparison.Ordinal))
            {
                found.Add("EPL2");
            }

            if (t.Contains("TSPL", StringComparison.Ordinal))
            {
                found.Add("TSPL");
            }

            if (t.Contains("DPL", StringComparison.Ordinal) || t.Contains("APL-D", StringComparison.Ordinal) || t.Contains("DSIM", StringComparison.Ordinal))
            {
                found.Add("DPL");
            }

            if (t.Contains("FINGERPRINT", StringComparison.Ordinal) || Regex.IsMatch(t, @"\bFP\b") || t.Contains("DIRECT PROTOCOL", StringComparison.Ordinal))
            {
                found.Add("Fingerprint");
            }

            if (t.Contains("SBPL", StringComparison.Ordinal))
            {
                found.Add("SBPL");
            }

            if (t.Contains("TPCL", StringComparison.Ordinal))
            {
                found.Add("TPCL");
            }
        }

        return [.. found];
    }

    private enum Support
    {
        Native,
        Emulation,
        None,
    }

    /// <summary>Ce que chaque marque comprend : langage natif, émulation (selon modèle et langage actif) ou rien.</summary>
    private static Support Supports(PrinterBrand brand, string language) => (brand, language) switch
    {
        (PrinterBrand.Zebra, "ZPL" or "SGD" or "JSON SGD") => Support.Native,
        (PrinterBrand.Zebra, "EPL2" or "DPL") => Support.Emulation,
        (PrinterBrand.Tsc, "TSPL") => Support.Native,
        (PrinterBrand.Tsc, "EPL2" or "ZPL" or "DPL") => Support.Emulation,
        (PrinterBrand.Honeywell, "Fingerprint" or "DPL") => Support.Native,
        (PrinterBrand.Honeywell, "ZPL" or "EPL2") => Support.Emulation,
        (PrinterBrand.Intermec, "Fingerprint") => Support.Native,
        (PrinterBrand.Intermec, "ZPL" or "EPL2" or "DPL") => Support.Emulation,
        (PrinterBrand.Datamax, "DPL") => Support.Native,
        (PrinterBrand.Datamax, "ZPL" or "EPL2") => Support.Emulation,
        (PrinterBrand.Sato, "SBPL") => Support.Native,
        (PrinterBrand.Sato, "ZPL" or "DPL") => Support.Emulation,
        (PrinterBrand.Toshiba, "TPCL") => Support.Native,
        (PrinterBrand.Toshiba, "ZPL") => Support.Emulation,
        _ => Support.None,
    };

    /// <summary>
    /// Compatibilité du fichier avec l'imprimante choisie : langages compris (natifs, par émulation, ou non), langage actif
    /// (quand l'imprimante l'annonce), commandes SGD propres aux Zebra. Repère placé sur la première ligne du langage concerné.
    /// </summary>
    public static IReadOnlyList<ConfigFinding> Compatibility(ConfigReport report, PrinterProfile profile)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(profile);
        var list = new List<ConfigFinding>();
        if (profile.Brand == PrinterBrand.Unknown)
        {
            return list;
        }

        var name = profile.DisplayName;
        foreach (var language in report.Languages)
        {
            var line = report.Items.FirstOrDefault(i => i.Language == language)?.Line ?? 0;
            var label = language is "SGD" or "JSON SGD" ? "SGD" : language;
            if (label == "SGD" && list.Any(f => f.Message.Contains("SGD", StringComparison.Ordinal)))
            {
                continue;
            }

            var active = profile.Languages.Count > 0 && label != "SGD" ? profile.Languages.Contains(label) : (bool?)null;
            switch (Supports(profile.Brand, language))
            {
                case Support.Native when active == false:
                    list.Add(new ConfigFinding(line, Severity.Warning,
                        $"{name} : le {label} n'apparaît pas parmi les langages actifs annoncés ({string.Join(", ", profile.Languages)}). S'il n'est pas actif, les commandes {label} sont ignorées ou imprimées en texte : vérifie le langage de l'imprimante avant d'envoyer."));
                    break;
                case Support.Native:
                    break;
                case Support.Emulation when active == true:
                    list.Add(new ConfigFinding(line, Severity.Info,
                        $"{name} : le {label} est compris par émulation (annoncé par cette imprimante). Certaines commandes de réglage peuvent être ignorées ou interprétées différemment : vérifie le résultat après l'envoi."));
                    break;
                case Support.Emulation:
                    list.Add(new ConfigFinding(line, active == false ? Severity.Error : Severity.Warning,
                        $"{name} : le {label} n'est pas son langage d'origine ({NativeLanguage(profile.Brand)}). Il n'est compris que si l'émulation {label} existe sur ce modèle et qu'elle est active"
                        + (active == false ? $" ; langage(s) annoncé(s) : {string.Join(", ", profile.Languages)}." : " : vérifie-le avant d'envoyer, sinon les commandes sont ignorées ou imprimées en texte.")));
                    break;
                default:
                    list.Add(label == "SGD"
                        ? new ConfigFinding(line, Severity.Error, $"{name} : les commandes SGD (« ! U1 setvar », JSON « {{}}{{…}} ») sont propres aux Zebra. Cette imprimante les ignore ou les imprime en texte ; ses réglages passent par son langage ({NativeLanguage(profile.Brand)}), son panneau ou sa page web.")
                        : new ConfigFinding(line, Severity.Error, $"{name} ne comprend pas le {label} (langage d'origine : {NativeLanguage(profile.Brand)}). Ces commandes seront ignorées ou imprimées en texte."));
                    break;
            }
        }

        return list;
    }

    /// <summary>Langage d'origine de la marque.</summary>
    public static string NativeLanguage(PrinterBrand brand) => brand switch
    {
        PrinterBrand.Zebra => "ZPL et SGD",
        PrinterBrand.Tsc => "TSPL",
        PrinterBrand.Honeywell => "Fingerprint / Direct Protocol, ou DPL selon le modèle",
        PrinterBrand.Intermec => "Fingerprint / Direct Protocol, IPL",
        PrinterBrand.Datamax => "DPL",
        PrinterBrand.Sato => "SBPL",
        PrinterBrand.Toshiba => "TPCL",
        _ => "inconnu",
    };

    /// <summary>
    /// Profil d'une Zebra d'après ses réglages « allcv » : modèle, firmware et langages actifs (device.languages).
    /// </summary>
    public static PrinterProfile ZebraProfile(IReadOnlyDictionary<string, PrinterValue> settings, string source)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? Get(string key) => settings.TryGetValue(key, out var v) && v.Value.Length > 0 ? v.Value : null;
        var languages = new List<string>();
        if (Get("device.languages") is { } dl)
        {
            var l = dl.ToLowerInvariant();
            if (l.Contains("zpl", StringComparison.Ordinal))
            {
                languages.Add("ZPL");
            }

            if (l.Contains("epl", StringComparison.Ordinal))
            {
                languages.Add("EPL2");
            }

            if (l.Contains("apl-d", StringComparison.Ordinal))
            {
                languages.Add("DPL");
            }
        }

        return new PrinterProfile(PrinterBrand.Zebra, Get("device.product_name"), Get("appl.name"), languages, settings, source);
    }

    [GeneratedRegex(@"\b(Z[TDQ]\d{3}|ZD\d{3,4}|ZT\d{3}|GK420|GX4[23]0|GC420|ZM[46]00|ZE5\d\d|ZQ\d{3}|QLN\d{3}|105SL|110XI|XI4|ZC\d{3}|ZXP)", RegexOptions.CultureInvariant)]
    private static partial Regex ZebraModel();

    [GeneratedRegex(@"\b(B-EX\d|B-SX\d|B-FV4|B-FX3|B-SA4|B-EP\d|B-452|B-852|BA4[0-9]{2}|B-472|B-572)", RegexOptions.CultureInvariant)]
    private static partial Regex ToshibaModel();

    [GeneratedRegex(@"(\bTSC\b|^TSC|\bTTP-|\bTE[23]\d0\b|\bTX[23]\d0|\bMH\d{3}|\bML\d{3}|\bDA[23]\d0\b|\bTDP-|\bTA[23]\d0\b|\bME[234]\d0|\bMX\d{3}|\bTH[23]\d0)", RegexOptions.CultureInvariant)]
    private static partial Regex TscName();
}
