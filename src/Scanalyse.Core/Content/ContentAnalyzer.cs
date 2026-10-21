using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Symbologies;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Content;

/// <summary>
/// Interprète le contenu d'un code : ce qu'il contient, à quoi il sert, et ce qui doit alerter.
/// L'analyse est entièrement locale : aucun lien n'est ouvert.
/// </summary>
public sealed partial class ContentAnalyzer(TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    private static readonly HashSet<string> Shorteners = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "is.gd", "buff.ly", "rebrand.ly", "cutt.ly", "qrco.de",
        "s.id", "shorturl.at", "rb.gy", "tiny.cc", "t.ly", "lnkd.in", "bl.ink", "qr.io", "q-r.to", "me-qr.com",
        "l.ead.me", "scan.page", "qrfy.io",
    };

    /// <summary>Analyse le contenu lu.</summary>
    public ContentAnalysis Analyze(ScanInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Text ?? string.Empty;
        var trimmed = text.Trim();
        var aim = input.SymbologyIdentifier is null ? null : SymbologyIdentifier.Describe(input.SymbologyIdentifier);

        if (StageNowProfile.TryDecode(input.RawBytes) is { } stageNow)
        {
            return AnalyzeStageNow(stageNow);
        }

        if (TryGs1(trimmed, aim, out var gs1Analysis))
        {
            return gs1Analysis;
        }

        var result =
            TryOtpAuth(trimmed) ??
            TryWifi(trimmed) ??
            TryVCard(trimmed) ??
            TryMeCard(trimmed) ??
            TryEvent(trimmed) ??
            TryEmail(trimmed) ??
            TryPhone(trimmed) ??
            TrySms(trimmed) ??
            TryGeo(trimmed) ??
            TryEpc(trimmed) ??
            TrySwissQr(trimmed) ??
            TryTwoDDoc(trimmed) ??
            TryMisc(trimmed) ??
            TryUrl(trimmed) ??
            TryJson(trimmed) ??
            (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit)
                ? new ContentAnalysis(ContentKind.Number, "Nombre")
                : new ContentAnalysis(ContentKind.Text, "Texte"));

        if (IsRetail(input.Symbology) || (input.Symbology == Symbology.Itf && trimmed.Length == 14))
        {
            AddRetail(result, trimmed, input);
        }

        return result;
    }

    // ---------------------------------------------------------------- GS1

    private bool TryGs1(string text, SymbologyIdentifier? aim, out ContentAnalysis analysis)
    {
        analysis = null!;
        var visual = ControlCharacters.FromVisualized(text);
        var flagged = aim?.IsGs1 == true || text.StartsWith('(');
        var hasGs = visual.Contains(ControlCharacters.GroupSeparator);
        if (!flagged && !hasGs)
        {
            return false;
        }

        var msg = Gs1Parser.Parse(text, _clock);
        if (msg is null)
        {
            return false;
        }

        // Sans marquage GS1, on n'accepte que si tous les AI sont connus (sinon c'est du texte ordinaire).
        if (!flagged && msg.Elements.Any(e => e.Definition is null))
        {
            return false;
        }

        analysis = new ContentAnalysis(ContentKind.Gs1, "Données GS1 (traçabilité)") { Gs1 = msg };
        AddGs1Fields(analysis, msg);
        if (!flagged)
        {
            analysis.Warn(Severity.Warning,
                $"Structure GS1 reconnue, mais le code n'est pas marqué GS1 ({aim?.Value ?? "identifiant standard"}, sans FNC1 en tête). " +
                "Les systèmes GS1 risquent de le refuser : active le mode GS1 dans le logiciel d'étiquetage.");
        }

        analysis.Warn(Severity.Info, "Chaîne d'éléments GS1 : identifiants d'application (AI) normalisés, lus par les systèmes logistiques et de caisse.");
        return true;
    }

    private void AddGs1Fields(ContentAnalysis a, Gs1Message msg)
    {
        foreach (var e in msg.Elements)
        {
            a.Add(e.Name, e.Display, hint: $"AI {e.Ai}");
            foreach (var issue in e.Issues)
            {
                a.Warn(Severity.Error, $"{e.Name} (AI {e.Ai}) : {issue}");
            }

            if (e.Date is { } d && e.Ai is "17" or "15" or "7003")
            {
                var today = DateOnly.FromDateTime(_clock.GetLocalNow().Date);
                var label = e.Ai == "15" ? "DDM" : "Date d'expiration";
                if (d < today)
                {
                    a.Warn(Severity.Error, $"{label} dépassée : {Gs1Parser.FormatDate(d)}.");
                }
                else if (d.DayNumber - today.DayNumber <= 30)
                {
                    var days = d.DayNumber - today.DayNumber;
                    a.Warn(Severity.Warning, $"{label} dans {days} jour{(days > 1 ? "s" : string.Empty)} ({Gs1Parser.FormatDate(d)}).");
                }
            }
        }
    }

    // ---------------------------------------------------------------- Distribution

    private static bool IsRetail(Symbology s) =>
        s is Symbology.Ean13 or Symbology.Ean8 or Symbology.UpcA or Symbology.UpcE or Symbology.Isbn or Symbology.Itf14;

    private static void AddRetail(ContentAnalysis a, string text, ScanInput input)
    {
        var d = new string(text.Where(char.IsAsciiDigit).ToArray());
        if (d.Length == 0)
        {
            return;
        }

        var g = input.Symbology == Symbology.UpcA && d.Length == 12 ? "0" + d : d;
        (a.Kind, a.Title) = d switch
        {
            _ when d.StartsWith("978", StringComparison.Ordinal) || d.StartsWith("979", StringComparison.Ordinal) => (ContentKind.Book, "Livre (ISBN)"),
            _ when d.StartsWith("977", StringComparison.Ordinal) => (ContentKind.Periodical, "Presse (ISSN)"),
            _ => (ContentKind.RetailGtin, "Article (GTIN)"),
        };

        var valid = CheckDigit.IsValid(d);
        a.Add("GTIN", d, hint: valid ? "clé valide" : "clé invalide");
        if (!valid)
        {
            a.Warn(Severity.Error, "La clé de contrôle est fausse : le code a été mal généré ou mal lu.");
        }

        if (g.Length == 13)
        {
            a.Add("Préfixe GS1", $"{g[..3]} : {Gs1Prefixes.Lookup(g)}");
        }
        else if (g.Length == 14)
        {
            a.Add("Indicateur logistique", g[..1]).Add("Préfixe GS1", Gs1Prefixes.Lookup(g.AsSpan(1)));
        }

        if (d.Length == 13 && d.StartsWith("978", StringComparison.Ordinal))
        {
            var body = d[3..12];
            var s = 0;
            for (var i = 0; i < 9; i++)
            {
                s += (10 - i) * (body[i] - '0');
            }

            var c = (11 - (s % 11)) % 11;
            a.Add("ISBN-10", body + (c == 10 ? "X" : c.ToString(CultureInfo.InvariantCulture)));
        }

        if (g.Length == 13 && g[0] == '2')
        {
            a.Warn(Severity.Info, "Préfixe 2 : code interne au magasin, souvent pour les produits pesés ou à prix variable.");
        }
        else if (g.Length >= 13)
        {
            a.Warn(Severity.Info, "Le préfixe GS1 indique le pays de l'organisation qui a attribué le numéro, pas le pays de fabrication.");
        }
    }

    // ---------------------------------------------------------------- Formats « métier »

    private static ContentAnalysis? TryOtpAuth(string t)
    {
        if (!t.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase) || !Uri.TryCreate(t, UriKind.Absolute, out var u))
        {
            return null;
        }

        var q = ParseQuery(u.Query);
        var a = new ContentAnalysis(ContentKind.OneTimePassword, "Double authentification (2FA)")
            .Add("Type", u.Host.ToUpperInvariant())
            .Add("Compte", Uri.UnescapeDataString(u.AbsolutePath.TrimStart('/')))
            .Add("Émetteur", q.GetValueOrDefault("issuer") ?? "—")
            .Add("Clé secrète", q.GetValueOrDefault("secret") ?? "—", sensitive: true)
            .Add("Algorithme", q.GetValueOrDefault("algorithm") ?? "SHA1")
            .Add("Chiffres", q.GetValueOrDefault("digits") ?? "6")
            .Add("Période", (q.GetValueOrDefault("period") ?? "30") + " s");
        return a.Warn(Severity.Error, "Ce code contient une clé secrète de connexion. Ne le partage pas et ne le publie pas.");
    }

    private static ContentAnalysis? TryWifi(string t)
    {
        if (!t.StartsWith("WIFI:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var f = SplitKeyValues(t[5..]);
        var type = f.GetValueOrDefault("T") ?? string.Empty;
        var pass = f.GetValueOrDefault("P");
        var a = new ContentAnalysis(ContentKind.Wifi, "Réseau Wi-Fi")
            .Add("Nom du réseau (SSID)", f.GetValueOrDefault("S") ?? string.Empty)
            .Add("Sécurité", type.ToUpperInvariant() switch
            {
                "WPA" => "WPA / WPA2 / WPA3",
                "SAE" => "WPA3 (SAE)",
                "WEP" => "WEP (obsolète)",
                "NOPASS" => "Aucune (réseau ouvert)",
                "" => "non précisée",
                _ => type,
            })
            .Add("Mot de passe", string.IsNullOrEmpty(pass) ? "—" : pass, sensitive: !string.IsNullOrEmpty(pass))
            .Add("Réseau masqué", f.GetValueOrDefault("H")?.Equals("true", StringComparison.OrdinalIgnoreCase) == true ? "oui" : "non");
        if (type.Equals("WEP", StringComparison.OrdinalIgnoreCase))
        {
            a.Warn(Severity.Warning, "WEP est un chiffrement obsolète et facile à casser.");
        }

        if (type.Equals("nopass", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(pass))
        {
            a.Warn(Severity.Warning, "Réseau sans mot de passe : le trafic n'est pas chiffré.");
        }

        return a;
    }

    private static ContentAnalysis? TryVCard(string t)
    {
        if (!t.StartsWith("BEGIN:VCARD", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["FN"] = "Nom", ["ORG"] = "Organisation", ["TITLE"] = "Fonction", ["TEL"] = "Téléphone", ["EMAIL"] = "E-mail",
            ["ADR"] = "Adresse", ["URL"] = "Site web", ["NOTE"] = "Note", ["BDAY"] = "Anniversaire",
        };
        var a = new ContentAnalysis(ContentKind.Contact, "Carte de visite (vCard)");
        var unfolded = UnfoldRegex().Replace(t, string.Empty);
        foreach (var line in unfolded.Split('\n'))
        {
            var m = VCardLineRegex().Match(line.TrimEnd('\r'));
            if (!m.Success || !labels.TryGetValue(m.Groups[1].Value, out var label))
            {
                continue;
            }

            var value = m.Groups[3].Value.Replace("\\n", " ", StringComparison.OrdinalIgnoreCase)
                .Replace("\\,", ",", StringComparison.Ordinal).Replace("\\;", ";", StringComparison.Ordinal);
            if (m.Groups[1].Value.Equals("ADR", StringComparison.OrdinalIgnoreCase))
            {
                value = string.Join(", ", value.Split(';', StringSplitOptions.RemoveEmptyEntries));
            }

            var type = TypeParamRegex().Match(m.Groups[2].Value);
            a.Add(label, value, hint: type.Success ? type.Groups[1].Value.ToLowerInvariant() : null);
        }

        return a;
    }

    private static ContentAnalysis? TryMeCard(string t)
    {
        if (!t.StartsWith("MECARD:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["N"] = "Nom", ["TEL"] = "Téléphone", ["EMAIL"] = "E-mail", ["ADR"] = "Adresse", ["URL"] = "Site web",
            ["NOTE"] = "Note", ["BDAY"] = "Anniversaire", ["ORG"] = "Organisation",
        };
        var a = new ContentAnalysis(ContentKind.Contact, "Carte de visite (MeCard)");
        foreach (var part in SplitUnescaped(t[7..], ';'))
        {
            var i = part.IndexOf(':');
            if (i > 0 && labels.TryGetValue(part[..i], out var label))
            {
                a.Add(label, Unescape(part[(i + 1)..]).Replace(",", ", ", StringComparison.Ordinal));
            }
        }

        return a;
    }

    private static ContentAnalysis? TryEvent(string t)
    {
        if (!t.Contains("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string? Get(string key)
        {
            var m = Regex.Match(t, "^" + key + "[^:\\n]*:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.TrimEnd('\r') : null;
        }

        static string FormatIcs(string? v)
        {
            if (v is null)
            {
                return "—";
            }

            var m = Regex.Match(v, @"(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2}))?");
            if (!m.Success)
            {
                return v;
            }

            var d = new DateOnly(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
            return Gs1Parser.FormatDate(d) + (m.Groups[4].Success ? $" à {m.Groups[4].Value} h {m.Groups[5].Value}" : string.Empty);
        }

        var a = new ContentAnalysis(ContentKind.CalendarEvent, "Événement d'agenda")
            .Add("Titre", Get("SUMMARY") ?? "—")
            .Add("Début", FormatIcs(Get("DTSTART")))
            .Add("Fin", FormatIcs(Get("DTEND")))
            .Add("Lieu", Get("LOCATION") ?? "—");
        if (Get("DESCRIPTION") is { } desc)
        {
            a.Add("Description", desc.Replace("\\n", " ", StringComparison.Ordinal));
        }

        return a;
    }

    private static ContentAnalysis? TryEmail(string t)
    {
        if (t.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
        {
            var rest = t[7..];
            var qi = rest.IndexOf('?');
            var to = Uri.UnescapeDataString(qi < 0 ? rest : rest[..qi]);
            var q = ParseQuery(qi < 0 ? string.Empty : rest[qi..]);
            var a = new ContentAnalysis(ContentKind.Email, "E-mail").Add("Destinataire", to);
            if (q.GetValueOrDefault("cc") is { } cc)
            {
                a.Add("Copie", cc);
            }

            if (q.GetValueOrDefault("subject") is { } subject)
            {
                a.Add("Objet", subject);
            }

            if (q.GetValueOrDefault("body") is { } body)
            {
                a.Add("Message", body);
            }

            return a;
        }

        if (t.StartsWith("MATMSG:", StringComparison.OrdinalIgnoreCase))
        {
            var f = SplitKeyValues(t[7..]);
            return new ContentAnalysis(ContentKind.Email, "E-mail")
                .Add("Destinataire", f.GetValueOrDefault("TO") ?? string.Empty)
                .Add("Objet", f.GetValueOrDefault("SUB") ?? "—")
                .Add("Message", f.GetValueOrDefault("BODY") ?? "—");
        }

        return null;
    }

    private static ContentAnalysis? TryPhone(string t)
    {
        if (!t.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var number = t[4..];
        var digits = new string(number.Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        var a = new ContentAnalysis(ContentKind.Phone, "Numéro de téléphone").Add("Numéro", number);
        if (digits.StartsWith("089", StringComparison.Ordinal) || digits.StartsWith("+3389", StringComparison.Ordinal))
        {
            a.Warn(Severity.Warning, "Numéro surtaxé (08 9x) : appel facturé au prix fort.");
        }

        return a;
    }

    private static ContentAnalysis? TrySms(string t)
    {
        string number, body;
        if (t.StartsWith("SMSTO:", StringComparison.OrdinalIgnoreCase))
        {
            var p = t[6..];
            var i = p.IndexOf(':');
            (number, body) = i < 0 ? (p, string.Empty) : (p[..i], p[(i + 1)..]);
        }
        else if (t.StartsWith("sms:", StringComparison.OrdinalIgnoreCase))
        {
            var p = t[4..];
            var i = p.IndexOf('?');
            number = i < 0 ? p : p[..i];
            body = i < 0 ? string.Empty : ParseQuery(p[i..]).GetValueOrDefault("body") ?? string.Empty;
        }
        else
        {
            return null;
        }

        return new ContentAnalysis(ContentKind.Sms, "SMS").Add("Numéro", number).Add("Message", body.Length > 0 ? body : "—");
    }

    private static ContentAnalysis? TryGeo(string t)
    {
        if (!t.StartsWith("geo:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var m = Regex.Match(t[4..], @"^(-?[\d.]+),(-?[\d.]+)(?:,(-?[\d.]+))?");
        var a = new ContentAnalysis(ContentKind.Geo, "Position géographique");
        if (m.Success)
        {
            a.Add("Latitude", m.Groups[1].Value).Add("Longitude", m.Groups[2].Value);
            if (m.Groups[3].Success)
            {
                a.Add("Altitude", m.Groups[3].Value + " m");
            }

            a.Link = new Uri($"https://www.openstreetmap.org/?mlat={m.Groups[1].Value}&mlon={m.Groups[2].Value}#map=16/{m.Groups[1].Value}/{m.Groups[2].Value}");
        }

        return a;
    }

    private static ContentAnalysis? TryEpc(string t)
    {
        if (!Regex.IsMatch(t, @"^BCD\r?\n"))
        {
            return null;
        }

        var l = t.Split('\n').Select(x => x.TrimEnd('\r')).ToArray();
        string L(int i) => i < l.Length ? l[i] : string.Empty;
        var amount = Regex.Match(L(7), @"^([A-Z]{3})([\d.]+)$");
        var iban = L(6).Replace(" ", string.Empty, StringComparison.Ordinal);
        var a = new ContentAnalysis(ContentKind.SepaPayment, "Virement SEPA (QR EPC)")
            .Add("Bénéficiaire", L(5).Length > 0 ? L(5) : "—")
            .Add("IBAN", Regex.Replace(iban, ".{4}", "$0 ").Trim())
            .Add("BIC", L(4).Length > 0 ? L(4) : "—")
            .Add("Montant", amount.Success
                ? decimal.Parse(amount.Groups[2].Value, CultureInfo.InvariantCulture).ToString("F2", CultureInfo.InvariantCulture).Replace('.', ',') + " " + amount.Groups[1].Value
                : "libre")
            .Add("Référence structurée", L(9).Length > 0 ? L(9) : "—")
            .Add("Motif", L(10).Length > 0 ? L(10) : (L(8).Length > 0 ? L(8) : "—"))
            .Add("Version", L(1));
        if (iban.Length > 0 && !Iban.IsValid(iban))
        {
            a.Warn(Severity.Error, "La clé de contrôle de l'IBAN est invalide.");
        }

        return a.Warn(Severity.Warning, "Vérifie toujours le bénéficiaire et l'IBAN avant de payer : un QR de paiement peut être collé par-dessus l'original.");
    }

    private static ContentAnalysis? TrySwissQr(string t)
    {
        if (!Regex.IsMatch(t, @"^SPC\r?\n"))
        {
            return null;
        }

        var a = new ContentAnalysis(ContentKind.SwissQrBill, "QR-facture suisse");
        var iban = Regex.Match(t, @"\b(CH|LI)\d{2}[0-9A-Z]{17}\b");
        if (iban.Success)
        {
            a.Add("IBAN", iban.Value);
        }

        return a.Warn(Severity.Info, "Format suisse de paiement (Swiss QR Code).");
    }

    private static ContentAnalysis? TryTwoDDoc(string t)
    {
        if (!Regex.IsMatch(t, "^DC0[1-4]"))
        {
            return null;
        }

        var a = new ContentAnalysis(ContentKind.TwoDDoc, "2D-Doc (document sécurisé français)")
            .Add("Version", t[2..4])
            .Add("Autorité de certification", t.Length >= 8 ? t[4..8] : "—")
            .Add("Certificat", t.Length >= 12 ? t[8..12] : "—");
        if (t.Length >= 16 && int.TryParse(t[12..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var days) && days < 0xFFFF)
        {
            a.Add("Date d'émission", Gs1Parser.FormatDate(new DateOnly(2000, 1, 1).AddDays(days)));
        }

        return a.Warn(Severity.Info, "La signature du 2D-Doc n'est pas vérifiée ici. Utilise l'application officielle de l'ANTS pour authentifier le document.");
    }

    private static ContentAnalysis? TryMisc(string t)
    {
        if (t.StartsWith("HC1:", StringComparison.Ordinal))
        {
            return new ContentAnalysis(ContentKind.EuHealthCertificate, "Certificat sanitaire européen (EU DCC)")
                .Warn(Severity.Info, "Contenu compressé et signé : il faut une application dédiée pour le lire.");
        }

        var crypto = Regex.Match(t, "^(bitcoin|ethereum|litecoin):([^?]+)", RegexOptions.IgnoreCase);
        if (crypto.Success)
        {
            return new ContentAnalysis(ContentKind.CryptoPayment, "Paiement en cryptomonnaie")
                .Add("Adresse", crypto.Groups[2].Value)
                .Warn(Severity.Warning, "Un paiement en cryptomonnaie est irréversible. Vérifie l'adresse.");
        }

        if (Regex.IsMatch(t, "^(market|itms-apps|intent):", RegexOptions.IgnoreCase))
        {
            return new ContentAnalysis(ContentKind.AppLink, "Lien vers une application");
        }

        return null;
    }

    private ContentAnalysis? TryUrl(string t)
    {
        if (!Regex.IsMatch(t, "^(https?://|www\\.)", RegexOptions.IgnoreCase))
        {
            return null;
        }

        var candidate = t.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + t : t;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var u))
        {
            return null;
        }

        var dl = DigitalLink.TryParse(u, _clock);
        var a = new ContentAnalysis(dl is null ? ContentKind.WebLink : ContentKind.Gs1DigitalLink, dl is null ? "Lien web" : "Lien GS1 Digital Link")
        {
            Link = u,
            Gs1 = dl,
        };
        a.Add("Protocole", u.Scheme == Uri.UriSchemeHttps ? "HTTPS" : "HTTP", hint: u.Scheme == Uri.UriSchemeHttps ? "chiffré" : "non chiffré")
         .Add("Domaine", u.IdnHost);
        if (!u.IsDefaultPort)
        {
            a.Add("Port", u.Port.ToString(CultureInfo.InvariantCulture));
        }

        if (u.AbsolutePath.Length > 1)
        {
            a.Add("Chemin", Uri.UnescapeDataString(u.AbsolutePath));
        }

        var q = ParseQuery(u.Query);
        if (q.Count > 0)
        {
            a.Add("Paramètres", string.Join(" · ", q.Select(kv => $"{kv.Key} = {kv.Value}")));
            var utm = q.Keys.Where(k => k.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)).ToList();
            if (utm.Count > 0)
            {
                a.Warn(Severity.Info, $"Lien de suivi marketing ({string.Join(", ", utm)}).");
            }
        }

        if (dl is not null)
        {
            AddGs1Fields(a, dl);
            a.Warn(Severity.Info, "Lien GS1 Digital Link : la même étiquette sert au passage en caisse et à une page web produit.");
        }

        if (u.Scheme == Uri.UriSchemeHttp)
        {
            a.Warn(Severity.Warning, "Lien non chiffré (http) : préfère https.");
        }

        if (Shorteners.Contains(u.Host))
        {
            a.Warn(Severity.Warning, "Lien raccourci ou redirigé : la destination finale n'est pas visible avant ouverture.");
        }

        if (u.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            a.Warn(Severity.Warning, "Le lien pointe vers une adresse IP plutôt qu'un nom de domaine : prudence.");
        }

        if (u.Host.Contains("xn--", StringComparison.OrdinalIgnoreCase) || u.IdnHost.Contains("xn--", StringComparison.OrdinalIgnoreCase))
        {
            a.Warn(Severity.Warning, "Nom de domaine internationalisé (punycode) : vérifie qu'il n'imite pas un site connu.");
        }

        if (!string.IsNullOrEmpty(u.UserInfo))
        {
            a.Warn(Severity.Error, "Le lien contient un « @ » avant le domaine : technique classique pour masquer la vraie destination.");
        }

        if (Regex.IsMatch(u.AbsolutePath, @"\.(apk|exe|msi|bat|scr|dmg|zip|js|vbs)$", RegexOptions.IgnoreCase))
        {
            a.Warn(Severity.Error, "Le lien télécharge directement un fichier exécutable ou une archive.");
        }

        return a.Warn(Severity.Ok, "Le lien n'a pas été ouvert : l'analyse est faite localement.");
    }

    private static ContentAnalysis? TryJson(string t)
    {
        if (t.Length == 0 || (t[0] != '{' && t[0] != '['))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            var structure = root.ValueKind == JsonValueKind.Array
                ? $"tableau de {root.GetArrayLength()} éléments"
                : "objet : " + string.Join(", ", root.EnumerateObject().Take(12).Select(p => p.Name));
            return new ContentAnalysis(ContentKind.Json, "Données JSON").Add("Structure", structure);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- StageNow

    private static ContentAnalysis AnalyzeStageNow(StageNowProfile p)
    {
        var a = new ContentAnalysis(ContentKind.ZebraStageNow, "Profil Zebra StageNow") { StageNow = p };
        a.Add("Type", "Profil de configuration Zebra StageNow");
        if (p.Total > 1)
        {
            a.Add("Partie", $"{p.Part} sur {p.Total}");
        }

        if (p.Script.Length == 0)
        {
            return a.Warn(Severity.Info, p.Total > 1
                ? "Profil réparti sur plusieurs codes : scanne toutes les parties avec StageNow sur le terminal Zebra."
                : "Contenu compressé non décodable ici (profil chiffré ou partiel).");
        }

        var actions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Add"] = "Ajouter", ["Connect"] = "Se connecter", ["Remove"] = "Supprimer", ["Disconnect"] = "Se déconnecter",
            ["Enable"] = "Activer", ["Disable"] = "Désactiver", ["Install"] = "Installer", ["Uninstall"] = "Désinstaller",
        };
        var wifiPassword = false;
        var step = 0;
        foreach (var call in p.Calls)
        {
            step++;
            var label = $"Étape {step}";
            if (call.Settings is not { } json)
            {
                var parts = new List<string>();
                foreach (var arg in call.Arguments)
                {
                    var cred = Regex.Match(arg, @"^[a-z]+://([^/@:]+):([^/@]+)@", RegexOptions.IgnoreCase);
                    if (cred.Success)
                    {
                        a.Warn(Severity.Warning, $"Une adresse du profil contient un identifiant ({cred.Groups[1].Value}) et un mot de passe en clair.");
                    }

                    parts.Add(Regex.Replace(arg, @"^([a-z]+://[^/@:]+:)[^/@]+@", "$1•••@", RegexOptions.IgnoreCase));
                }

                var verb = call.Name.Contains("download", StringComparison.OrdinalIgnoreCase) ? "Télécharger" : call.Name;
                a.Add(label, parts.Count > 0 ? $"{verb} · {string.Join(" → ", parts)}" : verb, hint: call.Name);
                continue;
            }

            var zd = json.TryGetProperty("zd", out var z) ? z : json;
            var act = zd.TryGetProperty("48", out var actEl) ? actEl.GetString() : null;
            var details = new List<string>();
            if (call.Name.Contains("wi-fi", StringComparison.OrdinalIgnoreCase) || call.Name.Contains("wifi", StringComparison.OrdinalIgnoreCase))
            {
                if (zd.TryGetProperty("0", out var net))
                {
                    if (net.TryGetProperty("49", out var ssid))
                    {
                        details.Add($"réseau {ssid.GetString()}");
                    }

                    if (net.TryGetProperty("176", out var sec))
                    {
                        if (sec.TryGetProperty("82", out var keyType))
                        {
                            details.Add($"clé : {keyType.GetString()}");
                        }

                        if (sec.TryGetProperty("88", out var pwd))
                        {
                            details.Add($"mot de passe {pwd.GetString()}");
                            wifiPassword = true;
                        }
                    }

                    if (net.TryGetProperty("177", out var ip))
                    {
                        string S(string k) => ip.TryGetProperty(k, out var v) ? v.GetString() ?? "?" : "?";
                        details.Add($"IP fixe {S("98")} / {S("101")}, passerelle {S("99")}, DNS {S("102")}");
                    }
                }
            }

            var actionLabel = act is null ? null : actions.GetValueOrDefault(act, act);
            var text = string.Join(" · ", new[] { actionLabel }.Concat(details).Where(s => !string.IsNullOrEmpty(s)));
            a.Add(label, text.Length > 0 ? text : "paramètres (voir le profil décompressé)", sensitive: wifiPassword && details.Any(d => d.StartsWith("mot de passe", StringComparison.Ordinal)), hint: call.Name);
        }

        if (wifiPassword)
        {
            a.Warn(Severity.Warning, "Ce code contient un mot de passe Wi-Fi lisible par n'importe quel lecteur : ne le diffuse pas au-delà des personnes qui préparent les terminaux.");
        }

        return a.Warn(Severity.Info, "Profil StageNow décompressé localement. Les champs non reconnus restent visibles dans le profil décompressé.");
    }

    // ---------------------------------------------------------------- Outils

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = pair.IndexOf('=');
            var k = Uri.UnescapeDataString((i < 0 ? pair : pair[..i]).Replace('+', ' '));
            var v = i < 0 ? string.Empty : Uri.UnescapeDataString(pair[(i + 1)..].Replace('+', ' '));
            d.TryAdd(k, v);
        }

        return d;
    }

    private static Dictionary<string, string> SplitKeyValues(string s)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in SplitUnescaped(s, ';'))
        {
            var i = part.IndexOf(':');
            if (i > 0)
            {
                d.TryAdd(part[..i], Unescape(part[(i + 1)..]));
            }
        }

        return d;
    }

    private static List<string> SplitUnescaped(string s, char sep)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                cur.Append(s[i]).Append(s[i + 1]);
                i++;
            }
            else if (s[i] == sep)
            {
                list.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(s[i]);
            }
        }

        list.Add(cur.ToString());
        return list;
    }

    private static string Unescape(string s) => Regex.Replace(s, @"\\([\\;,:""])", "$1");

    [GeneratedRegex(@"\r?\n[ \t]")]
    private static partial Regex UnfoldRegex();

    [GeneratedRegex(@"^([A-Za-z-]+)((?:;[^:]*)?):(.*)$")]
    private static partial Regex VCardLineRegex();

    [GeneratedRegex("TYPE=([^;:]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TypeParamRegex();
}

/// <summary>Vérification des IBAN (clé modulo 97).</summary>
public static class Iban
{
    /// <summary>Indique si l'IBAN a une structure et une clé valides.</summary>
    public static bool IsValid(string iban)
    {
        ArgumentNullException.ThrowIfNull(iban);
        var s = iban.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (!Regex.IsMatch(s, "^[A-Z]{2}[0-9]{2}[A-Z0-9]{8,30}$"))
        {
            return false;
        }

        var rearranged = s[4..] + s[..4];
        var mod = 0;
        foreach (var c in rearranged)
        {
            var v = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;
            mod = v >= 10 ? ((mod * 100) + v) % 97 : ((mod * 10) + v) % 97;
        }

        return mod == 1;
    }
}
