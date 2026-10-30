using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Scanalyse.Core.Printing;

public static partial class ConfigConverter
{
    // ------------------------------------------------------------------ Wi-Fi

    /// <summary>Pays Wi-Fi proposés (valeurs Zebra de wlan.country_code) et leur nom.</summary>
    public static IReadOnlyList<(string Value, string Label)> WifiCountries { get; } =
    [
        ("europe", "Europe"),
        ("usa/canada", "États-Unis / Canada"),
        ("japan", "Japon"),
        ("australia/nz", "Australie / Nouvelle-Zélande"),
    ];

    private static string Low(string v) => v.ToLowerInvariant();

    /// <summary>
    /// Clé WPA/WPA2 personnelle (PSK) calculée à partir du mot de passe et du nom du réseau, comme le fait la borne :
    /// PBKDF2-HMAC-SHA1, 4096 tours, 32 octets, écrits en 64 caractères hexadécimaux.
    /// </summary>
    public static string WpaPsk(string passphrase, string ssid)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(ssid);
        var key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(passphrase), Encoding.UTF8.GetBytes(ssid), 4096, HashAlgorithmName.SHA1, 32);
        return System.Convert.ToHexString(key).ToLowerInvariant();
    }

    /// <summary>Clé déjà calculée : exactement 64 caractères hexadécimaux.</summary>
    public static bool IsHexKey(string? s) => s is { Length: 64 } && s.All(char.IsAsciiHexDigit);

    /// <summary>Mot de passe Wi-Fi recevable : 8 à 63 caractères imprimables, ou clé hexadécimale de 64 caractères.</summary>
    public static bool IsValidPassphrase(string? s) =>
        IsHexKey(s) || (s is { Length: >= 8 and <= 63 } && s.All(c => c is >= ' ' and <= '~'));

    /// <summary>Lignes TSPL « WLAN … » (module Wi-Fi TSC) relues dans les réglages communs.</summary>
    private static Known TsplWlan(string t, PrinterSettings s)
    {
        var m = WlanArgs().Match(t);
        if (!m.Success)
        {
            return Known.Other;
        }

        var quoted = Regex.Matches(m.Groups[2].Value, "\"([^\"]*)\"").Select(x => x.Groups[1].Value).ToList();
        switch (m.Groups[1].Value.ToUpperInvariant())
        {
            case "SSID" when quoted.Count > 0:
                s.WifiSsid = quoted[0];
                return Known.Setting;
            case "WPA" when quoted.Count > 0:
                s.WifiSecurity = WifiSecurity.WpaPersonal;
                s.WifiPassphrase = quoted[0];
                return Known.Setting;
            case "WPA" when m.Groups[2].Value.Trim().Equals("OFF", StringComparison.OrdinalIgnoreCase):
                s.WifiSecurity = WifiSecurity.Open;
                return Known.Setting;
            case "DHCP":
                s.WifiDhcp = true;
                return Known.Setting;
            case "IP" when quoted.Count > 0:
                s.WifiDhcp = false;
                s.WifiIpAddress = quoted[0];
                s.WifiNetmask = quoted.Count > 1 ? quoted[1] : s.WifiNetmask;
                s.WifiGateway = quoted.Count > 2 ? quoted[2] : s.WifiGateway;
                return Known.Setting;
            default:
                return Known.Other;
        }
    }

    /// <summary>
    /// Wi-Fi écrit à la fin du fichier, après les autres réglages : SGD pour Zebra (y compris à la suite d'un ZPL),
    /// commandes WLAN pour TSC ; ailleurs, le réglage passe par l'outil du fabricant.
    /// </summary>
    private static void WriteWifi(PrinterSettings s, Writer w)
    {
        if (!s.HasWifi)
        {
            return;
        }

        switch (w.Target)
        {
            case ConfigLanguage.Zpl or ConfigLanguage.Sgd:
                WriteZebraWifi(s, w);
                break;
            case ConfigLanguage.Tspl:
                WriteTsplWifi(s, w);
                break;
            default:
                var tool = w.Target switch
                {
                    ConfigLanguage.Dpl or ConfigLanguage.Fingerprint => "l'outil Honeywell PrintSet ou la page web de l'imprimante",
                    ConfigLanguage.Sbpl => "l'outil SATO All-In-One ou la page web de l'imprimante",
                    ConfigLanguage.Tpcl => "l'outil de réglage Toshiba TEC ou la page web de l'imprimante",
                    _ => "les commandes SGD Zebra (choisis SGD : les imprimantes EPL2 récentes les acceptent) ou Zebra Setup Utilities",
                };
                Missing(w, "Wi-Fi", $"pas de commande Wi-Fi fiable en {Name(w.Target)} : règle-le avec {tool}, en USB, avant de passer en Wi-Fi.");
                break;
        }
    }

    private static void WriteZebraWifi(PrinterSettings s, Writer w)
    {
        void Set(string name, string value, string what)
        {
            w.Line($"! U1 setvar \"{name}\" \"{value}\"");
            w.Written(what, $"{name} = {(name == "wlan.wpa.psk" ? "(clé calculée)" : value)}", false);
        }

        if (w.Target == ConfigLanguage.Zpl)
        {
            w.Note(ConversionStatus.Converted, "Wi-Fi écrit en commandes SGD (! U1 setvar) à la suite du ZPL : c'est la méthode recommandée par Zebra, l'imprimante les accepte dans le même envoi.");
        }

        if (s.WifiCountry is { Length: > 0 } country)
        {
            Set("wlan.country_code", country, "Pays du Wi-Fi");
        }

        if (s.WifiSsid is { Length: > 0 } ssid)
        {
            Set("wlan.essid", ssid, "Nom du réseau Wi-Fi (SSID)");
            if (s.WifiSecurity == WifiSecurity.Open)
            {
                Set("wlan.security", "none", "Sécurité Wi-Fi (réseau ouvert)");
            }
            else if (s.WifiPassphrase is not { Length: > 0 } && s.WifiSecurity == WifiSecurity.WpaPersonal)
            {
                Set("wlan.security", "wpa psk", "Sécurité Wi-Fi (WPA / WPA2 personnel)");
                w.Note(ConversionStatus.Approximated, "Mot de passe Wi-Fi non renseigné : celui déjà enregistré dans l'imprimante est gardé.");
            }
            else if (s.WifiPassphrase is { Length: > 0 } pass)
            {
                Set("wlan.security", "wpa psk", "Sécurité Wi-Fi (WPA / WPA2 personnel)");
                if (IsHexKey(pass))
                {
                    Set("wlan.wpa.psk", pass.ToLowerInvariant(), "Clé Wi-Fi WPA");
                }
                else
                {
                    Set("wlan.wpa.psk", WpaPsk(pass, ssid), "Clé Wi-Fi WPA");
                    w.Note(ConversionStatus.Converted, "Mot de passe Wi-Fi transformé en clé WPA calculée avec le nom du réseau : il n'apparaît pas en clair dans le fichier. Si le nom du réseau change, la clé est à recalculer.");
                }
            }
        }

        if (s.WifiDhcp is { } dhcp)
        {
            Set("wlan.ip.protocol", dhcp ? "all" : "permanent", "Attribution de l'adresse IP (Wi-Fi)");
            if (!dhcp)
            {
                Set("wlan.ip.addr", s.WifiIpAddress ?? string.Empty, "Adresse IP (Wi-Fi)");
                Set("wlan.ip.netmask", s.WifiNetmask ?? string.Empty, "Masque (Wi-Fi)");
                Set("wlan.ip.gateway", s.WifiGateway ?? string.Empty, "Passerelle (Wi-Fi)");
            }
        }
    }

    private static void WriteTsplWifi(PrinterSettings s, Writer w)
    {
        void Put(string line, string what)
        {
            w.Line(line);
            w.Written(what, line + " (module Wi-Fi TSC : à vérifier selon le modèle)", false);
        }

        if (s.WifiCountry is not null)
        {
            Missing(w, "Pays du Wi-Fi", "pas de commande TSPL ; il se règle par la page web de l'imprimante.");
        }

        if (s.WifiSsid is { Length: > 0 } ssid)
        {
            Put($"WLAN SSID \"{ssid}\"", "Nom du réseau Wi-Fi (SSID)");
            if (s.WifiSecurity == WifiSecurity.Open)
            {
                Put("WLAN WPA OFF", "Sécurité Wi-Fi (réseau ouvert)");
            }
            else if (s.WifiPassphrase is { Length: > 0 } pass)
            {
                if (IsHexKey(pass))
                {
                    Missing(w, "Mot de passe Wi-Fi", "le fichier d'origine ne contient que la clé calculée ; TSPL attend le mot de passe lui-même : saisis-le dans le formulaire.");
                }
                else
                {
                    Put($"WLAN WPA \"{pass}\"", "Mot de passe Wi-Fi (WPA)");
                    w.Note(ConversionStatus.Approximated, "Mot de passe Wi-Fi écrit en clair : ne diffuse pas ce fichier (e-mail, ticket) et supprime-le après usage.");
                }
            }
        }

        if (s.WifiDhcp is { } dhcp)
        {
            Put(dhcp ? "WLAN DHCP" : $"WLAN IP \"{s.WifiIpAddress}\",\"{s.WifiNetmask}\",\"{s.WifiGateway}\"", "Adresse IP (Wi-Fi)");
        }
    }

    /// <summary>Redémarrage à la toute fin : le réseau et le Wi-Fi ne s'appliquent qu'après.</summary>
    private static void WriteRestart(PrinterSettings s, Writer w)
    {
        if (!s.Restart)
        {
            if (s.HasWifi || s.Dhcp is not null)
            {
                w.Note(ConversionStatus.Approximated, "Les réglages réseau et Wi-Fi ne s'appliquent qu'au redémarrage de l'imprimante : coche « Redémarrer à la fin » ou éteins-la et rallume-la après l'envoi.");
            }

            return;
        }

        if (w.Target is ConfigLanguage.Zpl or ConfigLanguage.Sgd)
        {
            w.Line("! U1 do \"device.reset\" \"\"");
            w.Written("Redémarrage", "device.reset, en dernière ligne", false);
        }
        else
        {
            Missing(w, "Redémarrage", $"pas de commande sûre en {Name(w.Target)} : éteins et rallume l'imprimante après l'envoi.");
        }
    }

    [GeneratedRegex(@"^([A-Za-z]+)\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex WlanArgs();
}
