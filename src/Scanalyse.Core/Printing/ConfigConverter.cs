using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

/// <summary>Type de support.</summary>
public enum MediaKind
{
    Continuous,
    Gap,
    Mark,
}

/// <summary>Méthode d'impression.</summary>
public enum PrintMethod
{
    ThermalTransfer,
    DirectThermal,
}

/// <summary>Sortie des étiquettes.</summary>
public enum OutputMode
{
    TearOff,
    PeelOff,
    Rewind,
    Cutter,
    DelayedCut,
    Applicator,
}

/// <summary>Langage d'arrivée d'une conversion ou de l'assistant.</summary>
public enum ConfigLanguage
{
    Zpl,
    Sgd,
    Tspl,
    Epl2,
    Dpl,
    Fingerprint,
    Sbpl,
}

/// <summary>
/// Réglages communs à toutes les imprimantes d'étiquettes, indépendants du langage : ce qu'on lit dans un fichier et ce
/// qu'on écrit dans un autre. Une valeur nulle n'est pas réglée. Noirceur de 0 à 1, vitesse en pouces par seconde,
/// dimensions en millimètres (converties en points avec <see cref="Dpi"/>).
/// </summary>
public sealed class PrinterSettings
{
    public double? Darkness { get; set; }

    public double? SpeedIps { get; set; }

    public MediaKind? Media { get; set; }

    public PrintMethod? Method { get; set; }

    public OutputMode? Mode { get; set; }

    public double? WidthMm { get; set; }

    public double? LengthMm { get; set; }

    /// <summary>Espace entre étiquettes, ou hauteur de la marque noire.</summary>
    public double? GapMm { get; set; }

    public double? LeftShiftMm { get; set; }

    public double? TopShiftMm { get; set; }

    public double? TearOffMm { get; set; }

    /// <summary>Adresse automatique (DHCP) ; faux : adresse fixe ; nul : réseau non réglé.</summary>
    public bool? Dhcp { get; set; }

    public string? IpAddress { get; set; }

    public string? Netmask { get; set; }

    public string? Gateway { get; set; }

    /// <summary>Enregistrer les réglages dans l'imprimante (ZPL ^JUS).</summary>
    public bool Save { get; set; } = true;

    /// <summary>Résolution de la tête, pour passer des points aux millimètres.</summary>
    public int Dpi { get; set; } = 203;

    /// <summary>Au moins un réglage renseigné.</summary>
    public bool IsEmpty => Darkness is null && SpeedIps is null && Media is null && Method is null && Mode is null && WidthMm is null && LengthMm is null
        && GapMm is null && LeftShiftMm is null && TopShiftMm is null && TearOffMm is null && Dhcp is null;

    /// <summary>Copie indépendante.</summary>
    public PrinterSettings Clone() => (PrinterSettings)MemberwiseClone();
}

/// <summary>Sort d'une ligne ou d'un réglage dans la conversion.</summary>
public enum ConversionStatus
{
    /// <summary>Repris tel quel (même sens, même valeur).</summary>
    Converted,

    /// <summary>Repris avec une valeur approchée (échelle, unité ou codes différents) : à vérifier après un essai.</summary>
    Approximated,

    /// <summary>Sans équivalent dans le langage d'arrivée.</summary>
    NotConverted,
}

/// <summary>Une ligne du compte-rendu de conversion.</summary>
/// <param name="Line">Ligne du fichier d'origine (0 : réglage, pas une ligne précise).</param>
/// <param name="Status">Sort du réglage.</param>
/// <param name="Message">Explication.</param>
public sealed record ConversionNote(int Line, ConversionStatus Status, string Message);

/// <summary>Résultat d'une conversion ou de l'assistant.</summary>
/// <param name="Text">Fichier écrit dans le langage d'arrivée.</param>
/// <param name="Notes">Compte-rendu : repris, approché, non converti.</param>
/// <param name="Settings">Réglages communs utilisés.</param>
public sealed record ConversionResult(string Text, IReadOnlyList<ConversionNote> Notes, PrinterSettings Settings);

/// <summary>
/// Conversion d'un fichier de configuration d'un langage d'imprimante à un autre, en passant par les réglages communs
/// (<see cref="PrinterSettings"/>) : noirceur, vitesse, support, méthode, sortie, dimensions, décalages, réseau.
/// Les réglages propres à une marque (Wi-Fi Zebra, Weblink, mots de passe, &lt;ESC&gt;PG SATO, &lt;STX&gt;Kc DPL…)
/// sont listés comme non convertis, avec l'endroit où régler leur équivalent.
/// </summary>
public static partial class ConfigConverter
{
    /// <summary>Nom affiché d'un langage d'arrivée.</summary>
    public static string Name(ConfigLanguage language) => language switch
    {
        ConfigLanguage.Zpl => "ZPL (Zebra)",
        ConfigLanguage.Sgd => "SGD (Zebra, ! U1 setvar)",
        ConfigLanguage.Tspl => "TSPL (TSC)",
        ConfigLanguage.Epl2 => "EPL2 (Zebra, anciens modèles)",
        ConfigLanguage.Dpl => "DPL (Honeywell / Datamax)",
        ConfigLanguage.Fingerprint => "Fingerprint (Honeywell / Intermec)",
        _ => "SBPL (SATO)",
    };

    /// <summary>Langage d'arrivée conseillé pour une marque.</summary>
    public static ConfigLanguage ForBrand(PrinterBrand brand) => brand switch
    {
        PrinterBrand.Tsc => ConfigLanguage.Tspl,
        PrinterBrand.Honeywell or PrinterBrand.Intermec => ConfigLanguage.Fingerprint,
        PrinterBrand.Datamax => ConfigLanguage.Dpl,
        PrinterBrand.Sato => ConfigLanguage.Sbpl,
        _ => ConfigLanguage.Zpl,
    };

    /// <summary>Langage d'origine d'un réglage, pour l'échelle de noirceur et de vitesse.</summary>
    private static ConfigLanguage? FromAnalysis(string language) => language switch
    {
        "ZPL" => ConfigLanguage.Zpl,
        "SGD" or "JSON SGD" => ConfigLanguage.Sgd,
        "TSPL" => ConfigLanguage.Tspl,
        "EPL2" => ConfigLanguage.Epl2,
        "DPL" => ConfigLanguage.Dpl,
        "Fingerprint" => ConfigLanguage.Fingerprint,
        "SBPL" => ConfigLanguage.Sbpl,
        _ => null,
    };

    // ------------------------------------------------------------------ Lecture

    /// <summary>
    /// Réglages communs lus dans un fichier (tous langages), et les lignes de réglage sans équivalent commun.
    /// <paramref name="dpi"/> sert à convertir les points en millimètres.
    /// </summary>
    public static (PrinterSettings Settings, IReadOnlyList<ConversionNote> Notes, IReadOnlySet<ConfigLanguage> Sources) Extract(string text, int dpi = 203)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = new PrinterSettings { Dpi = dpi };
        var notes = new List<ConversionNote>();
        var sources = new HashSet<ConfigLanguage>();
        var report = PrinterConfig.Analyze(text);
        double? sd = null;
        double? md = null;
        foreach (var item in report.Items)
        {
            if (FromAnalysis(item.Language) is { } lang)
            {
                var known = item.Language switch
                {
                    "ZPL" => Zpl(item.Command, s, ref sd, ref md),
                    "SGD" => Sgd(item.Command, s),
                    "JSON SGD" => Json(item.Command, s),
                    "TSPL" => Tspl(item.Command, s),
                    "EPL2" => Epl2(item.Command, s),
                    "DPL" => Dpl(item.Command, s),
                    "Fingerprint" => Fingerprint(item.Command, s),
                    "SBPL" => Sbpl(item.Command, s),
                    _ => Known.Other,
                };
                if (known == Known.Setting)
                {
                    sources.Add(lang);
                }
                else if (known == Known.Other && item.Category is not ("Étiquette" or "Lecture"))
                {
                    notes.Add(new ConversionNote(item.Line, ConversionStatus.NotConverted, $"{item.Command} ({item.Meaning}) : {WhereElse(item)}"));
                }
            }
        }

        if (sd is not null || md is not null)
        {
            s.Darkness = Clamp(((sd ?? 15) + (md ?? 0)) / 30);
        }

        return (s, notes, sources);
    }

    private enum Known
    {
        /// <summary>Réglage commun repris.</summary>
        Setting,

        /// <summary>Structure ou contenu sans réglage (^XA, &lt;ESC&gt;A, E…) : ignoré sans le signaler.</summary>
        Structure,

        /// <summary>Réglage propre au langage ou à la marque : non converti.</summary>
        Other,
    }

    private static string WhereElse(ConfigItem item) => item.Category switch
    {
        "Réseau" => "réglage réseau propre à la marque (Wi-Fi, sécurité, services) ; à régler sur l'imprimante d'arrivée par sa page web, son panneau ou l'outil du fabricant (Zebra Setup Utilities, TSC Console, NetTool, SATO All-In-One Tool…).",
        "Système" => "réglage propre à la marque ou au modèle, sans équivalent commun ; à régler par le panneau ou l'outil du fabricant de l'imprimante d'arrivée.",
        _ => "sans équivalent commun entre les langages ; à vérifier sur l'imprimante d'arrivée.",
    };

    private static double Clamp(double v) => Math.Clamp(v, 0, 1);

    private static double Mm(double dots, PrinterSettings s) => dots / s.Dpi * 25.4;

    private static int Dots(double mm, PrinterSettings s) => (int)Math.Round(mm / 25.4 * s.Dpi);

    private static bool Num(string? v, out double d) =>
        double.TryParse(v?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out d);

    private static Known Zpl(string raw, PrinterSettings s, ref double? sd, ref double? md)
    {
        if (raw.Length < 3)
        {
            return Known.Structure;
        }

        var code = raw[..3].ToUpperInvariant();
        var args = raw[3..].Split(',');
        string A(int i) => i < args.Length ? args[i].Trim() : string.Empty;
        switch (code)
        {
            case "~SD" when Num(A(0), out var d):
                sd = d;
                return Known.Setting;
            case "^MD" when Num(A(0), out var d):
                md = d;
                return Known.Setting;
            case "^PR":
            {
                var v = A(0).ToUpperInvariant() switch { "A" => "2", "B" => "3", "C" => "4", "D" => "6", "E" => "8", var x => x };
                if (Num(v, out var ips))
                {
                    s.SpeedIps = ips;
                    return Known.Setting;
                }

                return Known.Other;
            }

            case "^MN":
                s.Media = A(0).ToUpperInvariant() switch { "N" or "V" => MediaKind.Continuous, "Y" or "W" => MediaKind.Gap, "M" => MediaKind.Mark, _ => s.Media };
                return A(0).ToUpperInvariant() == "A" ? Known.Other : Known.Setting;
            case "^MT":
                s.Method = A(0).Equals("D", StringComparison.OrdinalIgnoreCase) ? PrintMethod.DirectThermal : PrintMethod.ThermalTransfer;
                return Known.Setting;
            case "^MM":
                s.Mode = A(0).ToUpperInvariant() switch
                {
                    "T" => OutputMode.TearOff,
                    "P" => OutputMode.PeelOff,
                    "R" => OutputMode.Rewind,
                    "A" => OutputMode.Applicator,
                    "C" => OutputMode.Cutter,
                    "D" => OutputMode.DelayedCut,
                    _ => s.Mode,
                };
                return Known.Setting;
            case "^PW" when Num(A(0), out var w):
                s.WidthMm = Mm(w, s);
                return Known.Setting;
            case "^LL" when Num(A(0), out var l):
                s.LengthMm = Mm(l, s);
                return Known.Setting;
            case "^LS" when Num(A(0), out var x):
                s.LeftShiftMm = Mm(x, s);
                return Known.Setting;
            case "^LT" when Num(A(0), out var y):
                s.TopShiftMm = Mm(y, s);
                return Known.Setting;
            case "~TA" when Num(A(0), out var t):
                s.TearOffMm = Mm(t, s);
                return Known.Setting;
            case "^JU" when A(0).Equals("S", StringComparison.OrdinalIgnoreCase):
                s.Save = true;
                return Known.Structure;
            case "^NS":
                s.Dhcp = A(0).ToUpperInvariant() switch { "P" => false, "" => s.Dhcp, _ => true };
                s.IpAddress = A(1).Length > 0 ? A(1) : s.IpAddress;
                s.Netmask = A(2).Length > 0 ? A(2) : s.Netmask;
                s.Gateway = A(3).Length > 0 ? A(3) : s.Gateway;
                return Known.Setting;
            case "^XA" or "^XZ" or "^FX" or "^FS" or "^FO" or "^FD" or "^PQ" or "^LH" or "^CI":
                return Known.Structure;
            default:
                return Known.Other;
        }
    }

    private static Known SgdValue(string name, string value, PrinterSettings s)
    {
        var v = value.Trim();
        switch (name)
        {
            case "print.tone" when Num(v, out var d):
                s.Darkness = Clamp(d / 30);
                return Known.Setting;
            case "media.speed" when Num(v, out var ips):
                s.SpeedIps = ips;
                return Known.Setting;
            case "media.type" or "ezpl.media_type":
                s.Media = v switch { "continuous" or "variable length continuous" => MediaKind.Continuous, "gap/notch" => MediaKind.Gap, "mark" => MediaKind.Mark, _ => s.Media };
                return Known.Setting;
            case "ezpl.print_method":
                s.Method = v == "direct thermal" ? PrintMethod.DirectThermal : PrintMethod.ThermalTransfer;
                return Known.Setting;
            case "media.printmode":
                s.Mode = v switch
                {
                    "tear off" => OutputMode.TearOff,
                    "peel off" => OutputMode.PeelOff,
                    "rewind" => OutputMode.Rewind,
                    "applicator" => OutputMode.Applicator,
                    "cutter" => OutputMode.Cutter,
                    "delayed cut" => OutputMode.DelayedCut,
                    _ => s.Mode,
                };
                return Known.Setting;
            case "ezpl.print_width" or "print.width" when Num(v, out var w):
                s.WidthMm = Mm(w, s);
                return Known.Setting;
            case "zpl.label_length" when Num(v, out var l):
                s.LengthMm = Mm(l, s);
                return Known.Setting;
            case "zpl.left_position" when Num(v, out var x):
                s.LeftShiftMm = Mm(x, s);
                return Known.Setting;
            case "ezpl.tear_off" when Num(v, out var t):
                s.TearOffMm = Mm(t, s);
                return Known.Setting;
            case "ip.dhcp.enable":
                s.Dhcp = v == "on";
                return Known.Setting;
            case "ip.protocol":
                s.Dhcp = v != "permanent";
                return Known.Setting;
            case "ip.addr":
                s.IpAddress = v;
                return Known.Setting;
            case "ip.netmask":
                s.Netmask = v;
                return Known.Setting;
            case "ip.gateway":
                s.Gateway = v;
                return Known.Setting;
            default:
                return Known.Other;
        }
    }

    private static Known Sgd(string raw, PrinterSettings s)
    {
        var m = SgdLine().Match(raw);
        if (!m.Success)
        {
            return Known.Other;
        }

        return m.Groups[1].Value.ToLowerInvariant() switch
        {
            "getvar" => Known.Structure,
            "setvar" when m.Groups[3].Success => SgdValue(m.Groups[2].Value, m.Groups[3].Value, s),
            _ => Known.Other,
        };
    }

    private static Known Json(string raw, PrinterSettings s)
    {
        var m = JsonPair().Match(raw);
        return m.Success ? SgdValue(m.Groups[1].Value, m.Groups[2].Value, s) : Known.Other;
    }

    /// <summary>Valeur TSPL avec son unité : « 100 mm », « 3 » (pouces), « 24 dot ».</summary>
    private static double? TsplMm(string part, PrinterSettings s)
    {
        var t = part.Trim().ToLowerInvariant();
        var unit = t.EndsWith("mm", StringComparison.Ordinal) ? "mm" : t.EndsWith("dot", StringComparison.Ordinal) ? "dot" : "in";
        var number = t.Replace("mm", string.Empty, StringComparison.Ordinal).Replace("dot", string.Empty, StringComparison.Ordinal).Trim();
        if (!Num(number, out var v))
        {
            return null;
        }

        return unit switch { "mm" => v, "dot" => Mm(v, s), _ => v * 25.4 };
    }

    private static Known Tspl(string raw, PrinterSettings s)
    {
        var m = TsplLine().Match(raw.Trim());
        if (!m.Success)
        {
            return Known.Other;
        }

        var cmd = m.Groups[1].Value.ToUpperInvariant();
        var args = m.Groups[2].Value.Split(',');
        switch (cmd)
        {
            case "SIZE":
                s.WidthMm = TsplMm(args[0], s) ?? s.WidthMm;
                s.LengthMm = args.Length > 1 ? TsplMm(args[1], s) ?? s.LengthMm : s.LengthMm;
                return Known.Setting;
            case "GAP":
            {
                var gap = TsplMm(args[0], s);
                s.Media = gap is 0 ? MediaKind.Continuous : MediaKind.Gap;
                s.GapMm = gap is > 0 ? gap : s.GapMm;
                return Known.Setting;
            }

            case "BLINE":
                s.Media = MediaKind.Mark;
                s.GapMm = TsplMm(args[0], s) ?? s.GapMm;
                return Known.Setting;
            case "SPEED" when Num(args[0], out var ips):
                s.SpeedIps = ips;
                return Known.Setting;
            case "DENSITY" when Num(args[0], out var d):
                s.Darkness = Clamp(d / 15);
                return Known.Setting;
            case "SHIFT" when Num(args[^1], out var y):
                s.TopShiftMm = Mm(y, s);
                return Known.Setting;
            case "OFFSET":
                s.TearOffMm = TsplMm(args[0], s) ?? s.TearOffMm;
                return Known.Setting;
            case "SET":
            {
                var parts = m.Groups[2].Value.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var what = parts.Length > 0 ? parts[0].ToUpperInvariant() : string.Empty;
                var value = parts.Length > 1 ? parts[1].Trim().ToUpperInvariant() : string.Empty;
                var on = value != "OFF";
                switch (what)
                {
                    case "RIBBON":
                        s.Method = on ? PrintMethod.ThermalTransfer : PrintMethod.DirectThermal;
                        return Known.Setting;
                    case "PEEL" when on:
                        s.Mode = OutputMode.PeelOff;
                        return Known.Setting;
                    case "CUTTER" or "PARTIAL_CUTTER" when on:
                        s.Mode = OutputMode.Cutter;
                        return Known.Setting;
                    case "TEAR" or "STRIPER" when on:
                        s.Mode = OutputMode.TearOff;
                        return Known.Setting;
                    case "REWIND" when on:
                        s.Mode = OutputMode.Rewind;
                        return Known.Setting;
                    case "PEEL" or "CUTTER" or "PARTIAL_CUTTER" or "TEAR" or "STRIPER" or "REWIND":
                        return Known.Structure;
                    default:
                        return Known.Other;
                }
            }

            case "NET":
            {
                var t = m.Groups[2].Value.Trim();
                if (t.StartsWith("DHCP", StringComparison.OrdinalIgnoreCase))
                {
                    s.Dhcp = true;
                    return Known.Setting;
                }

                var ips = Regex.Matches(t, @"\d{1,3}(?:\.\d{1,3}){3}").Select(x => x.Value).ToList();
                if (ips.Count > 0)
                {
                    s.Dhcp = false;
                    s.IpAddress = ips[0];
                    s.Netmask = ips.Count > 1 ? ips[1] : s.Netmask;
                    s.Gateway = ips.Count > 2 ? ips[2] : s.Gateway;
                    return Known.Setting;
                }

                return Known.Other;
            }

            case "CLS" or "PRINT" or "TEXT" or "BARCODE" or "QRCODE" or "BOX" or "BAR" or "BITMAP" or "PUTBMP" or "BLOCK" or "DMATRIX" or "HOME" or "FORMFEED" or "FEED":
                return Known.Structure;
            default:
                return Known.Other;
        }
    }

    private static Known Epl2(string raw, PrinterSettings s)
    {
        var t = raw.Trim();
        if (t.Length == 0)
        {
            return Known.Structure;
        }

        var args = t[1..].Split(',');
        switch (t[0])
        {
            case 'q' when Num(args[0], out var w):
                s.WidthMm = Mm(w, s);
                return Known.Setting;
            case 'Q' when Num(args[0], out var l):
                s.LengthMm = Mm(l, s);
                var gap = args.Length > 1 ? args[1].Trim() : string.Empty;
                if (gap.StartsWith('B'))
                {
                    s.Media = MediaKind.Mark;
                    s.GapMm = Num(gap[1..], out var b) ? Mm(b, s) : s.GapMm;
                }
                else if (Num(gap, out var g))
                {
                    s.Media = g == 0 ? MediaKind.Continuous : MediaKind.Gap;
                    s.GapMm = g > 0 ? Mm(g, s) : s.GapMm;
                }

                return Known.Setting;
            case 'S' when Num(args[0], out var code):
                s.SpeedIps = code;
                return Known.Setting;
            case 'D' when Num(args[0], out var d):
                s.Darkness = Clamp(d / 15);
                return Known.Setting;
            case 'O':
                s.Method = t.Contains('D', StringComparison.Ordinal) ? PrintMethod.DirectThermal : PrintMethod.ThermalTransfer;
                return Known.Setting;
            case 'N' or 'P' or 'A' or 'B' or 'b' or 'X' or 'L':
                return Known.Structure;
            default:
                return t is "ZT" or "ZB" ? Known.Structure : Known.Other;
        }
    }

    /// <summary>Codes de vitesse DPL : A = 1 pouce/s, puis un demi-pouce par lettre.</summary>
    private static double DplSpeed(char code) => 1 + ((char.ToUpperInvariant(code) - 'A') * 0.5);

    private static Known Dpl(string raw, PrinterSettings s)
    {
        var body = raw.StartsWith("<STX>", StringComparison.Ordinal) ? raw[5..] : raw;
        var system = raw.StartsWith("<STX>", StringComparison.Ordinal);
        if (body.Length == 0)
        {
            return Known.Structure;
        }

        var rest = body[1..];
        if (!system)
        {
            switch (body[0])
            {
                case 'H' when Num(rest, out var h):
                    s.Darkness = Clamp(h / 30);
                    return Known.Setting;
                case 'P' when rest.Length == 1 && char.IsAsciiLetter(rest[0]):
                    s.SpeedIps = DplSpeed(rest[0]);
                    return Known.Setting;
                default:
                    return Known.Structure;
            }
        }

        switch (body[0])
        {
            case 'e':
                s.Media = MediaKind.Gap;
                return Known.Setting;
            case 'r':
                s.Media = MediaKind.Mark;
                return Known.Setting;
            case 'c':
                s.Media = MediaKind.Continuous;
                if (Num(rest, out var c) && c > 0)
                {
                    s.LengthMm = c * 0.254;
                }

                return Known.Setting;
            case 'O' when Num(rest, out var o):
                s.TopShiftMm = o * 0.254;
                return Known.Setting;
            case 'f' when Num(rest, out var f):
                s.TearOffMm = f * 0.254;
                return Known.Setting;
            case 'L' or 'm' or 'n' or 'M':
                return Known.Structure;
            default:
                return Known.Other;
        }
    }

    private static Known Fingerprint(string raw, PrinterSettings s)
    {
        var t = raw.TrimStart();
        if (!t.StartsWith("SETUP ", StringComparison.OrdinalIgnoreCase))
        {
            return Known.Structure;
        }

        var parts = FingerprintQuoted().Matches(t[6..]).Select(m => m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value.Trim()).Where(p => p.Length > 0).ToList();
        if (parts.Count < 3)
        {
            return Known.Other;
        }

        var key = string.Join("/", parts.Take(parts.Count - 1)).ToUpperInvariant();
        var value = parts[^1];
        var upper = value.ToUpperInvariant();
        switch (key)
        {
            case "MEDIA/MEDIA TYPE":
                s.Media = upper.Contains("GAP", StringComparison.Ordinal) ? MediaKind.Gap : upper.Contains("MARK", StringComparison.Ordinal) ? MediaKind.Mark : MediaKind.Continuous;
                return Known.Setting;
            case "MEDIA/PAPER TYPE":
                s.Method = upper.Contains("DIRECT", StringComparison.Ordinal) ? PrintMethod.DirectThermal : PrintMethod.ThermalTransfer;
                return Known.Setting;
            case "MEDIA/CONTRAST" when Num(value.Replace("%", string.Empty, StringComparison.Ordinal), out var c):
                s.Darkness = Clamp((c + 10) / 20);
                return Known.Setting;
            case "PRINT DEFS/PRINT SPEED" when Num(value, out var mms):
                s.SpeedIps = mms / 25.4;
                return Known.Setting;
            case "MEDIA/MEDIA SIZE/WIDTH" when Num(value, out var w):
                s.WidthMm = Mm(w, s);
                return Known.Setting;
            case "MEDIA/MEDIA SIZE/LENGTH" when Num(value, out var l):
                s.LengthMm = Mm(l, s);
                return Known.Setting;
            case "FEEDADJ/STARTADJ" when Num(value, out var st):
                s.TopShiftMm = Mm(st, s);
                return Known.Setting;
            case "FEEDADJ/STOPADJ" when Num(value, out var sp):
                s.TearOffMm = Mm(sp, s);
                return Known.Setting;
            default:
                return Known.Other;
        }
    }

    private static Known Sbpl(string raw, PrinterSettings s)
    {
        var body = raw.StartsWith("<ESC>", StringComparison.Ordinal) ? raw[5..] : raw;
        if (body.StartsWith("CS", StringComparison.Ordinal) && Num(body[2..], out var cs))
        {
            s.SpeedIps = cs;
            return Known.Setting;
        }

        if (body.StartsWith("#E", StringComparison.Ordinal) && body.Length > 2 && char.IsAsciiDigit(body[2]))
        {
            s.Darkness = Clamp((body[2] - '1') / 4.0);
            return Known.Setting;
        }

        if (body.StartsWith("IG", StringComparison.Ordinal) && body.Length > 2)
        {
            s.Media = body[2] switch { '1' => MediaKind.Mark, '2' => MediaKind.Gap, _ => MediaKind.Continuous };
            return Known.Setting;
        }

        return body is "A" or "Z" || body.Length == 0 ? Known.Structure : body.StartsWith("PG", StringComparison.Ordinal) || body.StartsWith("A3", StringComparison.Ordinal) ? Known.Other : Known.Structure;
    }

    // ------------------------------------------------------------------ Conversion

    /// <summary>
    /// Convertit un fichier vers <paramref name="target"/> : réglages communs relus puis réécrits, avec le compte-rendu
    /// (repris, approché, non converti).
    /// </summary>
    public static ConversionResult Convert(string text, ConfigLanguage target, int dpi = 203)
    {
        var (settings, notes, sources) = Extract(text, dpi);
        var result = Write(settings, target, sources);
        return result with { Notes = [.. notes, .. result.Notes] };
    }

    /// <summary>
    /// Écrit les réglages communs dans <paramref name="target"/> (assistant « Configurer », ou fin de conversion).
    /// <paramref name="sources"/> : langages d'origine, pour signaler les valeurs approchées (échelles différentes).
    /// </summary>
    public static ConversionResult Write(PrinterSettings settings, ConfigLanguage target, IReadOnlySet<ConfigLanguage>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var s = settings;
        var w = new Writer(target, sources ?? new HashSet<ConfigLanguage>());
        switch (target)
        {
            case ConfigLanguage.Zpl:
                WriteZpl(s, w);
                break;
            case ConfigLanguage.Sgd:
                WriteSgd(s, w);
                break;
            case ConfigLanguage.Tspl:
                WriteTspl(s, w);
                break;
            case ConfigLanguage.Epl2:
                WriteEpl2(s, w);
                break;
            case ConfigLanguage.Dpl:
                WriteDpl(s, w);
                break;
            case ConfigLanguage.Fingerprint:
                WriteFingerprint(s, w);
                break;
            default:
                WriteSbpl(s, w);
                break;
        }

        if (s.IsEmpty)
        {
            w.Note(ConversionStatus.NotConverted, "Aucun réglage commun trouvé (noirceur, vitesse, support, méthode, sortie, taille, décalages, réseau) : rien à écrire.");
        }

        return new ConversionResult(w.Text(), w.Notes, s);
    }

    private sealed class Writer(ConfigLanguage target, IReadOnlySet<ConfigLanguage> sources)
    {
        private readonly StringBuilder _text = new();

        public ConfigLanguage Target { get; } = target;

        /// <summary>Le fichier d'origine est dans un autre langage que celui d'arrivée : échelles et codes peuvent différer.</summary>
        public bool Foreign { get; } = sources.Count > 0 && !sources.All(x => x == target || ((x is ConfigLanguage.Zpl or ConfigLanguage.Sgd) && (target is ConfigLanguage.Zpl or ConfigLanguage.Sgd)));

        public List<ConversionNote> Notes { get; } = [];

        public void Line(string line) => _text.Append(line).Append("\r\n");

        public void Note(ConversionStatus status, string message) => Notes.Add(new ConversionNote(0, status, message));

        /// <summary>Réglage écrit : repris, ou approché si les échelles diffèrent entre les deux langages.</summary>
        public void Written(string what, string written, bool scaled)
        {
            var status = scaled && Foreign ? ConversionStatus.Approximated : ConversionStatus.Converted;
            Note(status, status == ConversionStatus.Approximated
                ? $"{what} : {written} (valeur approchée, l'échelle diffère entre les deux langages : à ajuster après un essai)."
                : $"{what} : {written}.");
        }

        public string Text() => _text.ToString();
    }

    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static void Missing(Writer w, string what, string why) => w.Note(ConversionStatus.NotConverted, $"{what} : {why}");

    private static void WriteZpl(PrinterSettings s, Writer w)
    {
        if (s.Darkness is { } d)
        {
            var v = (int)Math.Round(d * 30);
            w.Line($"~SD{v:00}");
            w.Written("Noirceur", $"~SD{v:00} (0 à 30)", true);
        }

        w.Line("^XA");
        if (s.SpeedIps is { } sp)
        {
            var v = (int)Math.Clamp(Math.Round(sp), 1, 14);
            w.Line($"^PR{v}");
            w.Written("Vitesse", $"^PR{v} ({v} pouces par seconde)", Math.Abs(v - sp) > 0.01);
        }

        if (s.Media is { } m)
        {
            var c = m switch { MediaKind.Continuous => "N", MediaKind.Mark => "M", _ => "Y" };
            w.Line($"^MN{c}");
            w.Written("Support", $"^MN{c}", false);
        }

        if (s.Method is { } me)
        {
            var c = me == PrintMethod.DirectThermal ? "D" : "T";
            w.Line($"^MT{c}");
            w.Written("Méthode d'impression", $"^MT{c}", false);
        }

        if (s.Mode is { } mo)
        {
            var c = mo switch { OutputMode.PeelOff => "P", OutputMode.Rewind => "R", OutputMode.Cutter => "C", OutputMode.DelayedCut => "D", OutputMode.Applicator => "A", _ => "T" };
            w.Line($"^MM{c}");
            w.Written("Sortie des étiquettes", $"^MM{c}", false);
        }

        DotsLine(s.WidthMm, s, w, "^PW", "Largeur d'impression");
        DotsLine(s.LengthMm, s, w, "^LL", "Longueur d'étiquette");
        DotsLine(s.LeftShiftMm, s, w, "^LS", "Décalage horizontal");
        DotsLine(s.TopShiftMm, s, w, "^LT", "Décalage vertical");
        if (s.TearOffMm is { } t)
        {
            var v = Math.Clamp(Dots(t, s), -120, 120);
            w.Line($"~TA{v:000;-00}");
            w.Written("Position de détachage", $"~TA{v:000;-00} ({v} points à {s.Dpi} dpi)", false);
        }

        if (s.Dhcp is { } dhcp)
        {
            var line = dhcp ? "^NSA" : $"^NSP,{s.IpAddress},{s.Netmask},{s.Gateway}";
            w.Line(line);
            w.Written("Réseau filaire", line, false);
        }

        if (s.Save)
        {
            w.Line("^JUS");
        }

        w.Line("^XZ");
    }

    private static void DotsLine(double? mm, PrinterSettings s, Writer w, string command, string what)
    {
        if (mm is not { } v)
        {
            return;
        }

        var dots = Dots(v, s);
        w.Line($"{command}{dots}");
        w.Written(what, $"{command}{dots} ({F(v)} mm à {s.Dpi} dpi)", false);
    }

    private static void WriteSgd(PrinterSettings s, Writer w)
    {
        void Set(string name, string value, string what, bool scaled = false)
        {
            w.Line($"! U1 setvar \"{name}\" \"{value}\"");
            w.Written(what, $"{name} = {value}", scaled);
        }

        if (s.Darkness is { } d)
        {
            Set("print.tone", F(Math.Round(d * 30)), "Noirceur (0 à 30)", true);
        }

        if (s.SpeedIps is { } sp)
        {
            var v = Math.Clamp(Math.Round(sp), 1, 14);
            Set("media.speed", F(v), "Vitesse (pouces par seconde)", Math.Abs(v - sp) > 0.01);
        }

        if (s.Media is { } m)
        {
            Set("ezpl.media_type", m switch { MediaKind.Continuous => "continuous", MediaKind.Mark => "mark", _ => "gap/notch" }, "Support");
        }

        if (s.Method is { } me)
        {
            Set("ezpl.print_method", me == PrintMethod.DirectThermal ? "direct thermal" : "thermal trans", "Méthode d'impression");
        }

        if (s.Mode is { } mo)
        {
            Set("media.printmode", mo switch
            {
                OutputMode.PeelOff => "peel off",
                OutputMode.Rewind => "rewind",
                OutputMode.Cutter => "cutter",
                OutputMode.DelayedCut => "delayed cut",
                OutputMode.Applicator => "applicator",
                _ => "tear off",
            }, "Sortie des étiquettes");
        }

        if (s.WidthMm is { } wi)
        {
            Set("ezpl.print_width", Dots(wi, s).ToString(CultureInfo.InvariantCulture), $"Largeur d'impression ({F(wi)} mm à {s.Dpi} dpi)");
        }

        if (s.LengthMm is { } l)
        {
            Set("zpl.label_length", Dots(l, s).ToString(CultureInfo.InvariantCulture), $"Longueur d'étiquette ({F(l)} mm à {s.Dpi} dpi)");
        }

        if (s.LeftShiftMm is { } x)
        {
            Set("zpl.left_position", Dots(x, s).ToString(CultureInfo.InvariantCulture), "Décalage horizontal");
        }

        if (s.TopShiftMm is not null)
        {
            Missing(w, "Décalage vertical", "pas de réglage SGD équivalent ; utilise ^LT en ZPL (convertis vers ZPL).");
        }

        if (s.TearOffMm is { } t)
        {
            Set("ezpl.tear_off", Math.Clamp(Dots(t, s), -120, 120).ToString(CultureInfo.InvariantCulture), "Position de détachage");
        }

        if (s.Dhcp is { } dhcp)
        {
            Set("ip.protocol", dhcp ? "all" : "permanent", "Attribution de l'adresse IP");
            if (!dhcp)
            {
                Set("ip.addr", s.IpAddress ?? string.Empty, "Adresse IP");
                Set("ip.netmask", s.Netmask ?? string.Empty, "Masque");
                Set("ip.gateway", s.Gateway ?? string.Empty, "Passerelle");
            }
        }
    }

    private static readonly double[] TsplSpeedSteps = [1.5, 2, 3, 4, 5, 6, 8, 10, 12, 14];

    private static void WriteTspl(PrinterSettings s, Writer w)
    {
        if (s.WidthMm is not null || s.LengthMm is not null)
        {
            if (s.WidthMm is { } wi && s.LengthMm is { } l)
            {
                w.Line($"SIZE {F(wi)} mm, {F(l)} mm");
                w.Written("Taille d'étiquette", $"SIZE {F(wi)} mm, {F(l)} mm", false);
            }
            else
            {
                Missing(w, "Taille d'étiquette", "SIZE demande la largeur et la longueur ; renseigne les deux dans l'assistant.");
            }
        }

        if (s.Media is { } m)
        {
            var line = m switch
            {
                MediaKind.Continuous => "GAP 0 mm, 0 mm",
                MediaKind.Mark => $"BLINE {F(s.GapMm ?? 3)} mm, 0 mm",
                _ => $"GAP {F(s.GapMm ?? 3)} mm, 0 mm",
            };
            w.Line(line);
            w.Written("Support", line + (s.GapMm is null && m != MediaKind.Continuous ? " (3 mm par défaut : mesure l'espace réel)" : string.Empty), false);
        }

        if (s.SpeedIps is { } sp)
        {
            var v = TsplSpeedSteps.OrderBy(x => Math.Abs(x - sp)).First();
            w.Line($"SPEED {F(v)}");
            w.Written("Vitesse", $"SPEED {F(v)} (pouces par seconde ; chaque modèle n'accepte que certaines vitesses)", Math.Abs(v - sp) > 0.01);
        }

        if (s.Darkness is { } d)
        {
            var v = (int)Math.Round(d * 15);
            w.Line($"DENSITY {v}");
            w.Written("Noirceur", $"DENSITY {v} (0 à 15)", true);
        }

        if (s.Method is { } me)
        {
            var line = me == PrintMethod.DirectThermal ? "SET RIBBON OFF" : "SET RIBBON ON";
            w.Line(line);
            w.Written("Méthode d'impression", line, false);
        }

        if (s.Mode is { } mo)
        {
            var lines = mo switch
            {
                OutputMode.PeelOff => new[] { "SET CUTTER OFF", "SET PEEL ON" },
                OutputMode.Cutter or OutputMode.DelayedCut => ["SET PEEL OFF", "SET CUTTER BATCH"],
                OutputMode.Rewind => ["SET PEEL OFF", "SET CUTTER OFF", "SET REWIND ON"],
                OutputMode.Applicator => [],
                _ => ["SET PEEL OFF", "SET CUTTER OFF", "SET TEAR ON"],
            };
            foreach (var l in lines)
            {
                w.Line(l);
            }

            if (lines.Length == 0)
            {
                Missing(w, "Sortie : applicateur", "pas de mode applicateur en TSPL ; à régler sur l'imprimante (interface applicateur).");
            }
            else
            {
                w.Written("Sortie des étiquettes", string.Join(" ; ", lines), mo == OutputMode.DelayedCut);
            }
        }

        if (s.TopShiftMm is { } y)
        {
            w.Line($"SHIFT {Dots(y, s)}");
            w.Written("Décalage vertical", $"SHIFT {Dots(y, s)} (points)", false);
        }

        if (s.LeftShiftMm is { } x)
        {
            w.Line($"REFERENCE {Dots(x, s)},0");
            w.Written("Décalage horizontal", $"REFERENCE {Dots(x, s)},0 (origine de l'étiquette, en points)", true);
        }

        if (s.TearOffMm is { } t)
        {
            w.Line($"OFFSET {F(t)} mm");
            w.Written("Position de détachage", $"OFFSET {F(t)} mm", false);
        }

        if (s.Dhcp is { } dhcp)
        {
            var line = dhcp ? "NET DHCP" : $"NET IP \"{s.IpAddress}\",\"{s.Netmask}\",\"{s.Gateway}\"";
            w.Line(line);
            w.Written("Réseau", line + " (selon le modèle et la carte réseau : à vérifier)", true);
        }
    }

    private static void WriteEpl2(PrinterSettings s, Writer w)
    {
        if (s.WidthMm is { } wi)
        {
            w.Line($"q{Dots(wi, s)}");
            w.Written("Largeur", $"q{Dots(wi, s)} ({F(wi)} mm à {s.Dpi} dpi)", false);
        }

        if (s.LengthMm is { } l)
        {
            var gap = s.Media switch
            {
                MediaKind.Continuous => "0",
                MediaKind.Mark => "B" + Dots(s.GapMm ?? 3, s),
                _ => Dots(s.GapMm ?? 3, s).ToString(CultureInfo.InvariantCulture),
            };
            w.Line($"Q{Dots(l, s)},{gap}");
            w.Written("Longueur et support", $"Q{Dots(l, s)},{gap}", false);
        }
        else if (s.Media is not null)
        {
            Missing(w, "Support", "en EPL2, le type de support s'écrit avec la longueur d'étiquette (Q) : renseigne la longueur.");
        }

        if (s.SpeedIps is { } sp)
        {
            var v = (int)Math.Clamp(Math.Round(sp), 1, 6);
            w.Line($"S{v}");
            w.Written("Vitesse", $"S{v} (code de vitesse propre au modèle)", true);
        }

        if (s.Darkness is { } d)
        {
            var v = (int)Math.Round(d * 15);
            w.Line($"D{v}");
            w.Written("Noirceur", $"D{v} (0 à 15)", true);
        }

        if (s.Method == PrintMethod.DirectThermal)
        {
            w.Line("OD");
            w.Written("Méthode d'impression", "OD (thermique direct)", false);
        }
        else if (s.Method == PrintMethod.ThermalTransfer)
        {
            w.Written("Méthode d'impression", "transfert thermique (réglage par défaut, rien à écrire)", false);
        }

        if (s.Mode is { } mo && mo != OutputMode.TearOff)
        {
            Missing(w, "Sortie des étiquettes", "le mode de sortie (décolleur, massicot) se règle sur l'imprimante en EPL2 (option O, selon le modèle).");
        }

        NoNetwork(s, w, "EPL2");
        NoShift(s, w, "EPL2");
    }

    private static void NoNetwork(PrinterSettings s, Writer w, string language)
    {
        if (s.Dhcp is not null)
        {
            Missing(w, "Réseau", $"pas de commande réseau en {language} : règle l'adresse par la page web, le panneau ou l'outil du fabricant.");
        }
    }

    private static void NoShift(PrinterSettings s, Writer w, string language)
    {
        if (s.LeftShiftMm is not null || s.TopShiftMm is not null || s.TearOffMm is not null)
        {
            Missing(w, "Décalages", $"pas d'équivalent direct en {language} : règle-les par le panneau de l'imprimante.");
        }
    }

    private static void WriteDpl(PrinterSettings s, Writer w)
    {
        if (s.Media is { } m)
        {
            var line = m switch
            {
                MediaKind.Continuous => $"<STX>c{(int)Math.Round((s.LengthMm ?? 0) / 0.254):0000}",
                MediaKind.Mark => "<STX>r",
                _ => "<STX>e",
            };
            w.Line(line);
            w.Written("Support", line, false);
        }

        if (s.LengthMm is { } l)
        {
            var v = (int)Math.Round(l * 1.2 / 0.254);
            w.Line($"<STX>M{v:0000}");
            w.Written("Longueur maximale d'étiquette", $"<STX>M{v:0000} (longueur + 20 %, en centièmes de pouce)", true);
        }

        if (s.TopShiftMm is { } y)
        {
            w.Line($"<STX>O{(int)Math.Round(y / 0.254):0000}");
            w.Written("Décalage du début d'impression", $"<STX>O{(int)Math.Round(y / 0.254):0000} (centièmes de pouce)", false);
        }

        if (s.TearOffMm is { } t)
        {
            w.Line($"<STX>f{(int)Math.Round(t / 0.254):000}");
            w.Written("Position d'arrêt (détachage)", $"<STX>f{(int)Math.Round(t / 0.254):000} (centièmes de pouce)", false);
        }

        if (s.Darkness is { } d)
        {
            Missing(w, "Noirceur", $"en DPL, la chaleur s'écrit dans chaque format d'étiquette (ligne H{(int)Math.Round(d * 30):00} après <STX>L) ou se règle dans le menu de l'imprimante.");
        }

        if (s.SpeedIps is { } sp)
        {
            var code = (char)('A' + (int)Math.Clamp(Math.Round((sp - 1) / 0.5), 0, 25));
            Missing(w, "Vitesse", $"en DPL, la vitesse s'écrit dans chaque format d'étiquette (ligne P{code} ≈ {F(DplSpeed(code))} pouces par seconde) ou se règle dans le menu de l'imprimante.");
        }

        if (s.WidthMm is not null)
        {
            Missing(w, "Largeur", "pas de largeur d'impression en commande système DPL : elle se règle dans le menu ou par <STX>Kc (manuel du modèle).");
        }

        if (s.Method is not null || s.Mode is not null)
        {
            Missing(w, "Méthode et sortie", "se règlent par le menu de l'imprimante ou <STX>Kc (paramètres propres au modèle, voir son manuel DPL).");
        }

        NoNetwork(s, w, "DPL");
        NoShiftLeft(s, w, "DPL");
    }

    private static void NoShiftLeft(PrinterSettings s, Writer w, string language)
    {
        if (s.LeftShiftMm is not null)
        {
            Missing(w, "Décalage horizontal", $"pas d'équivalent direct en {language} : règle-le par le panneau de l'imprimante.");
        }
    }

    private static void WriteFingerprint(PrinterSettings s, Writer w)
    {
        void Setup(string section, string key, string value, string what, bool scaled = false, bool quoted = true)
        {
            var line = $"SETUP \"{section}\",\"{key}\"," + (quoted ? $"\"{value}\"" : value);
            w.Line(line);
            w.Written(what, line, scaled);
        }

        if (s.Media is { } m)
        {
            Setup("MEDIA", "MEDIA TYPE", m switch { MediaKind.Continuous => "VAR LENGTH STRIP", MediaKind.Mark => "TICKET (W MARK)", _ => "LABEL (W GAPS)" }, "Support");
        }

        if (s.Method is { } me)
        {
            Setup("MEDIA", "PAPER TYPE", me == PrintMethod.DirectThermal ? "DIRECT THERMAL" : "TRANSFER", "Méthode d'impression");
        }

        if (s.Darkness is { } d)
        {
            var v = (int)Math.Round((d * 20) - 10);
            Setup("MEDIA", "CONTRAST", (v >= 0 ? "+" : string.Empty) + v + "%", "Noirceur (contraste -10 % à +10 %)", true);
        }

        if (s.SpeedIps is { } sp)
        {
            var v = (int)(Math.Round(sp * 25.4 / 25) * 25);
            Setup("PRINT DEFS", "PRINT SPEED", v.ToString(CultureInfo.InvariantCulture), "Vitesse (mm par seconde)", true, quoted: false);
        }

        if (s.WidthMm is { } wi)
        {
            w.Line($"SETUP \"MEDIA\",\"MEDIA SIZE\",\"WIDTH\",{Dots(wi, s)}");
            w.Written("Largeur", $"WIDTH {Dots(wi, s)} points ({F(wi)} mm à {s.Dpi} dpi)", false);
        }

        if (s.LengthMm is { } l)
        {
            w.Line($"SETUP \"MEDIA\",\"MEDIA SIZE\",\"LENGTH\",{Dots(l, s)}");
            w.Written("Longueur", $"LENGTH {Dots(l, s)} points ({F(l)} mm à {s.Dpi} dpi)", false);
        }

        if (s.TopShiftMm is { } y)
        {
            Setup("FEEDADJ", "STARTADJ", Dots(y, s).ToString(CultureInfo.InvariantCulture), "Décalage du début d'impression (points)", quoted: false);
        }

        if (s.TearOffMm is { } t)
        {
            Setup("FEEDADJ", "STOPADJ", Dots(t, s).ToString(CultureInfo.InvariantCulture), "Position d'arrêt (points)", quoted: false);
        }

        if (s.Mode is { } mo && mo != OutputMode.TearOff)
        {
            Missing(w, "Sortie des étiquettes", "le décolleur ou le massicot se règlent par le menu de l'imprimante (selon les options installées).");
        }

        if (s.Dhcp is { } dhcp)
        {
            Setup("NETWORK", "IP SELECTION", dhcp ? "DHCP" : "MANUAL", "Réseau (à vérifier selon le modèle)", true);
            if (!dhcp)
            {
                Setup("NETWORK", "IP ADDRESS", s.IpAddress ?? string.Empty, "Adresse IP");
                Setup("NETWORK", "NETMASK", s.Netmask ?? string.Empty, "Masque");
                Setup("NETWORK", "DEFAULT ROUTER", s.Gateway ?? string.Empty, "Passerelle");
            }
        }

        NoShiftLeft(s, w, "Fingerprint");
    }

    private static void WriteSbpl(PrinterSettings s, Writer w)
    {
        w.Line("<ESC>A");
        if (s.SpeedIps is { } sp)
        {
            var v = (int)Math.Clamp(Math.Round(sp), 1, 16);
            w.Line($"<ESC>CS{v}");
            w.Written("Vitesse", $"<ESC>CS{v} (code propre au modèle : vérifie la correspondance dans son manuel)", true);
        }

        if (s.Darkness is { } d)
        {
            var v = (int)Math.Round((d * 4) + 1);
            w.Line($"<ESC>#E{v}");
            w.Written("Noirceur", $"<ESC>#E{v} (1 à 5)", true);
        }

        if (s.Media is { } m)
        {
            var v = m switch { MediaKind.Mark => 1, MediaKind.Gap => 2, _ => 3 };
            w.Line($"<ESC>IG{v}");
            w.Written("Support", $"<ESC>IG{v}", false);
        }

        w.Line("<ESC>Z");
        w.Note(ConversionStatus.Approximated, "SBPL : ces réglages valent pour la session ; pour les garder à l'extinction, enregistre-les dans le menu de l'imprimante (ou <ESC>PG, propre au modèle).");
        if (s.WidthMm is not null || s.LengthMm is not null)
        {
            Missing(w, "Taille d'étiquette", "en SBPL, elle s'écrit dans chaque étiquette (<ESC>A1) ou se règle dans le menu de l'imprimante.");
        }

        if (s.Method is not null || s.Mode is not null)
        {
            Missing(w, "Méthode et sortie", "se règlent dans le menu de l'imprimante SATO (ou son outil All-In-One).");
        }

        NoNetwork(s, w, "SBPL");
        NoShift(s, w, "SBPL");
    }

    [GeneratedRegex("""(setvar|getvar|do)\s+"([^"]+)"(?:\s+"([^"]*)")?""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SgdLine();

    [GeneratedRegex("""^\s*"([^"]+)"\s*:\s*"?([^",}]*)"?""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonPair();

    [GeneratedRegex(@"^([A-Za-z]+)\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex TsplLine();

    [GeneratedRegex("\"([^\"]*)\"|([^,\"]+)", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintQuoted();
}
