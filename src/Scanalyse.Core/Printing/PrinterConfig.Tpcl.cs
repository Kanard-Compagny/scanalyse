using System.Globalization;
using System.Text.RegularExpressions;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

public static partial class PrinterConfig
{
    // ------------------------------------------------------------------ TPCL (Toshiba TEC)

    /// <summary>Capteur de support TPCL (paramètre des commandes T et XS).</summary>
    internal static string TpclSensor(char c) => c switch
    {
        '0' => "aucun capteur (support continu)",
        '1' => "capteur réfléchissant (marque noire)",
        '2' => "capteur transmissif (espace entre étiquettes)",
        '3' => "capteur transmissif (étiquettes pré-imprimées)",
        '4' => "capteur réfléchissant (marque pré-imprimée)",
        _ => string.Empty,
    };

    /// <summary>Mode de sortie TPCL : C continu (détachage), D prédécollage avec recul, E prédécollage.</summary>
    internal static string TpclMode(char c) => char.ToUpperInvariant(c) switch
    {
        'C' => "continu (détachage ou coupe)",
        'D' => "prédécollage avec recul",
        'E' => "prédécollage",
        'F' => "prédécollage (sans attente)",
        _ => string.Empty,
    };

    /// <summary>Vitesse TPCL : un chiffre (pouces par seconde) ; A = 10, B = 12, C = 14 sur les modèles rapides.</summary>
    internal static double? TpclSpeed(char c) => char.ToUpperInvariant(c) switch
    {
        >= '1' and <= '9' => c - '0',
        'A' => 10,
        'B' => 12,
        'C' => 14,
        _ => null,
    };

    internal static string TpclRibbon(char c) => c switch
    {
        '0' => "sans ruban (thermique direct)",
        '1' => "avec ruban (économiseur de ruban)",
        '2' => "avec ruban (transfert thermique)",
        _ => string.Empty,
    };

    /// <summary>
    /// Ligne TPCL : une ou plusieurs commandes entre accolades, terminées par « |} » (ex. {D0508,0760,0468|}).
    /// Taille d'étiquette, réglages fins, noirceur, avance et impression contrôlés ; le contenu d'étiquette est expliqué.
    /// </summary>
    private static void Tpcl(string line, int n, Ctx ctx)
    {
        var rest = line;
        foreach (Match m in TpclCommand().Matches(line))
        {
            rest = rest.Replace(m.Value, string.Empty, StringComparison.Ordinal);
            TpclOne(m.Value, m.Groups[1].Value, n, ctx);
        }

        if (rest.Trim().Length > 0)
        {
            ctx.Item(n, "TPCL", Short(rest.Trim()), "Commande TPCL mal fermée", "Système");
            ctx.Find(n, Severity.Error, $"TPCL : « {Short(rest.Trim())} » n'est pas une commande complète. Chaque commande s'écrit entre accolades et se termine par « |}} », ex. {{D0508,0760,0468|}}.");
        }
    }

    private static void TpclOne(string raw, string body, int n, Ctx ctx)
    {
        var code = body.Length >= 2 && char.IsAsciiLetterUpper(body[1]) ? body[..2] : body[..1];
        var args = body[code.Length..].TrimStart(';');
        switch (code)
        {
            case "C":
                ctx.Item(n, "TPCL", raw, "Effacement de la mémoire d'image", "Étiquette");
                break;
            case "D":
            {
                var p = args.Split(',');
                var ok = p.Length >= 3 && p.Take(3).All(x => x.Length == 4 && x.All(char.IsAsciiDigit));
                ctx.Item(n, "TPCL", raw, ok
                    ? string.Format(CultureInfo.InvariantCulture, "Taille d'étiquette : pas {0} mm, largeur {1} mm, longueur {2} mm", Mm10(p[0]), Mm10(p[1]), Mm10(p[2]))
                    : "Taille d'étiquette", "Support");
                if (!ok)
                {
                    ctx.Find(n, Severity.Error, "{D…|} : trois nombres de 4 chiffres attendus, en dixièmes de mm : pas de l'étiquette (étiquette + espace), largeur, longueur. Ex. {D0508,0760,0468|}.");
                }
                else if (int.Parse(p[0], CultureInfo.InvariantCulture) < int.Parse(p[2], CultureInfo.InvariantCulture))
                {
                    ctx.Find(n, Severity.Error, "{D…|} : le pas (étiquette + espace) est plus petit que la longueur d'étiquette.");
                }
                else
                {
                    ctx.Set("TPCL:SIZE", "Taille d'étiquette", $"{Mm10(p[1])} × {Mm10(p[2])} mm", n);
                }

                break;
            }

            case "AX":
            {
                var m = TpclAx().Match(args);
                ctx.Item(n, "TPCL", raw, m.Success
                    ? string.Format(CultureInfo.InvariantCulture, "Réglages fins : avance {0} mm, position de coupe ou de détachage {1} mm, recul {2} mm", Signed10(m.Groups[1].Value), Signed10(m.Groups[2].Value), Signed10(m.Groups[3].Value))
                    : "Réglages fins (avance, coupe, recul)", "Support");
                if (!m.Success)
                {
                    ctx.Find(n, Severity.Error, "{AX;…|} : forme attendue {AX;+000,+000,+00|} (avance, position de coupe ou de détachage, recul, en dixièmes de mm, avec le signe).");
                }

                break;
            }

            case "AY":
            {
                var m = TpclAy().Match(args);
                if (!m.Success)
                {
                    ctx.Item(n, "TPCL", raw, "Réglage de noirceur", "Impression");
                    ctx.Find(n, Severity.Error, "{AY;…|} : forme attendue {AY;+05,0|} (ajustement de -10 à +10, puis 0 transfert thermique ou 1 thermique direct).");
                    break;
                }

                var value = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                ctx.Item(n, "TPCL", raw, $"Ajustement de noirceur : {value:+0;-0;0} ({(m.Groups[2].Value == "1" ? "thermique direct" : "transfert thermique")})", "Impression");
                ctx.Set("TPCL:AY", "Ajustement de noirceur", value.ToString(CultureInfo.InvariantCulture), n);
                if (value is < -10 or > 10)
                {
                    ctx.Find(n, Severity.Error, $"{{AY;…|}} : {value} hors limites (-10 à +10).");
                }

                break;
            }

            case "T":
                TpclFeed(raw, args, n, ctx, issue: false);
                break;
            case "XS":
            {
                var p = args.Split(',');
                if (p.Length >= 3 && p[0] == "I")
                {
                    TpclFeed(raw, p[2].Length > 3 ? p[2][3..] : string.Empty, n, ctx, issue: true, copies: p[1].TrimStart('0'));
                }
                else
                {
                    ctx.Item(n, "TPCL", raw, "Impression", "Étiquette");
                    ctx.Find(n, Severity.Error, "{XS;…|} : forme attendue {XS;I,0001,0002C3000|} (quantité, intervalle de coupe, capteur, mode, vitesse, ruban…).");
                }

                break;
            }

            case "WS":
                ctx.Item(n, "TPCL", raw, "Demande d'état", "Lecture");
                break;
            case "PC" or "RC" or "XB" or "RB" or "LC" or "PV" or "RV" or "XR" or "SG" or "PG":
                ctx.Item(n, "TPCL", raw, "Contenu d'étiquette", "Étiquette");
                break;
            default:
                ctx.Item(n, "TPCL", raw, $"Commande TPCL {code}", "Système");
                ctx.Find(n, Severity.Info, $"{{{code}…|}} : commande TPCL non détaillée par Scanalyse ; vérifie-la dans le manuel TPCL du modèle.");
                break;
        }
    }

    /// <summary>Paramètres d'avance ou d'impression : capteur, coupe, mode, vitesse, ruban (ex. 20C30).</summary>
    private static void TpclFeed(string raw, string p, int n, Ctx ctx, bool issue, string? copies = null)
    {
        if (p.Length < 5)
        {
            ctx.Item(n, "TPCL", raw, issue ? "Impression" : "Avance d'une étiquette", issue ? "Étiquette" : "Support");
            ctx.Find(n, Severity.Error, issue
                ? "{XS;I,…|} : après la quantité, attendu intervalle de coupe (3 chiffres), capteur, mode, vitesse, ruban (ex. 0002C3000)."
                : "{T…|} : capteur, coupe, mode, vitesse et ruban attendus (ex. {T20C30|}).");
            return;
        }

        var (sensor, cut, mode, speed, ribbon) = issue ? (p[0], '0', p[1], p[2], p[3]) : (p[0], p[1], p[2], p[3], p[4]);
        var parts = new List<string>();
        var errors = new List<string>();
        void Check(string text, string error)
        {
            if (text.Length == 0)
            {
                errors.Add(error);
            }
            else
            {
                parts.Add(text);
            }
        }

        Check(TpclSensor(sensor), $"capteur « {sensor} » (0 à 4)");
        if (!issue)
        {
            Check(cut == '1' ? "avec coupe" : cut == '0' ? "sans coupe" : string.Empty, $"coupe « {cut} » (0 ou 1)");
        }

        Check(TpclMode(mode), $"mode « {mode} » (C, D ou E)");
        Check(TpclSpeed(speed) is { } ips ? $"{ips.ToString(CultureInfo.InvariantCulture)} pouces par seconde" : string.Empty, $"vitesse « {speed} »");
        Check(TpclRibbon(ribbon), $"ruban « {ribbon} » (0, 1 ou 2)");
        var what = issue ? $"Impression{(string.IsNullOrEmpty(copies) ? string.Empty : $" de {copies} étiquette(s)")}" : "Avance d'une étiquette";
        ctx.Item(n, "TPCL", raw, what + (parts.Count > 0 ? " : " + string.Join(", ", parts) : string.Empty), issue ? "Étiquette" : "Support");
        if (errors.Count > 0)
        {
            ctx.Find(n, Severity.Error, $"{raw} : valeur inconnue pour {string.Join(", ", errors)}.");
        }

        if (!issue)
        {
            ctx.Find(n, Severity.Info, "{T…|} fait avancer une étiquette : capteur, mode, vitesse et ruban sont appliqués à cette occasion.");
        }

        if (TpclSpeed(speed) is { } s)
        {
            ctx.Set("TPCL:SPEED", "Vitesse d'impression", s.ToString(CultureInfo.InvariantCulture), n);
        }
    }

    private static string Mm10(string tenths) =>
        (int.Parse(tenths, CultureInfo.InvariantCulture) / 10.0).ToString("0.#", CultureInfo.InvariantCulture);

    private static string Signed10(string v) =>
        (int.Parse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) / 10.0).ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\{([A-Z][^{}|]*)\|\}", RegexOptions.CultureInvariant)]
    private static partial Regex TpclCommand();

    [GeneratedRegex(@"^([+-]\d{3}),([+-]\d{3}),([+-]\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex TpclAx();

    [GeneratedRegex(@"^([+-]\d{2}),([01])$", RegexOptions.CultureInvariant)]
    private static partial Regex TpclAy();
}
