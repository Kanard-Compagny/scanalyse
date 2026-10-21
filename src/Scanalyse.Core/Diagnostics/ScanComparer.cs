using Scanalyse.Core.Content;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Symbologies;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Diagnostics;

/// <summary>Constat du diagnostic.</summary>
/// <param name="Code">Code stable du constat (pour les tests et l'historique).</param>
/// <param name="Severity">Gravité.</param>
/// <param name="Title">Résumé court.</param>
/// <param name="Explanation">Ce qui se passe et pourquoi.</param>
/// <param name="Fix">Réglage à corriger, le cas échéant.</param>
public sealed record Finding(string Code, Severity Severity, string Title, string Explanation, string? Fix = null);

/// <summary>Résultat de la comparaison entre le contenu du code et ce que l'application a reçu.</summary>
/// <param name="Identical">Les deux chaînes sont strictement identiques.</param>
/// <param name="DataMatches">Les données utiles sont identiques une fois retirés préfixes et suffixes.</param>
/// <param name="Findings">Constats, du plus important au moins important.</param>
public sealed record ComparisonResult(bool Identical, bool DataMatches, IReadOnlyList<Finding> Findings)
{
    /// <summary>Gravité la plus élevée parmi les constats.</summary>
    public Severity WorstSeverity => Findings.Count == 0 ? Severity.Ok : Findings.Max(f => f.Severity);
}

/// <summary>
/// Compare le contenu réel d'un code (lu sur l'image ou connu) avec ce qu'un scanner a transmis,
/// et explique chaque écart : réglage clavier, séparateur GS1, préfixe, suffixe, troncature…
/// </summary>
public static class ScanComparer
{
    private static readonly char[] TrailingControls = ['\r', '\n', '\t', '\u0003', '\u0004'];

    /// <summary>Compare <paramref name="expected"/> (contenu du code) et <paramref name="received"/> (données reçues).</summary>
    /// <remarks>Les deux textes acceptent la notation lisible &lt;GS&gt;, &lt;CR&gt;…</remarks>
    public static ComparisonResult Compare(string expected, string received)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(received);
        var e = ControlCharacters.FromVisualized(expected);
        var r = ControlCharacters.FromVisualized(received);
        var findings = new List<Finding>();

        // Contenu GS1 saisi sous sa forme lisible « (01)…(10)… » : on le compare à sa forme transmise.
        if (e.StartsWith('(') && Gs1Parser.Parse(e) is { Notation: Gs1Notation.Parenthesized } readable)
        {
            e = readable.ToTransmitted();
        }

        if (e == r)
        {
            return new ComparisonResult(true, true, [new Finding("IDENTICAL", Severity.Ok, "Lecture conforme",
                "Les données reçues sont exactement celles du code.")]);
        }

        if (r.Length == 0)
        {
            return new ComparisonResult(false, false, [new Finding("NOTHING_RECEIVED", Severity.Error, "Aucune donnée reçue",
                "Le code n'a rien transmis. Causes probables : symbologie désactivée dans le scanner, longueur hors des limites autorisées, ou application qui n'a pas le focus.",
                "Vérifie que la symbologie est activée dans le scanner (DataWedge, EZConfig, Aladdin…) et que ses longueurs minimale et maximale acceptent ce code.")]);
        }

        // 1. Identifiant de symbologie AIM ajouté en tête.
        if (!e.StartsWith(']') && SymbologyIdentifier.TryParsePrefix(r) is { } aim)
        {
            findings.Add(new Finding("AIM_PREFIX", Severity.Info, $"Identifiant de symbologie ajouté : {aim.Value}",
                $"Le scanner ajoute « {aim.Value} » ({aim.Meaning}) devant les données. C'est utile si l'application s'en sert pour reconnaître le type de code.",
                "Si l'application ne l'attend pas, désactive la transmission de l'identifiant AIM / Code ID dans le scanner."));
            r = r[3..];
        }

        // 2. Préfixe STX et suffixes de contrôle (Entrée, tabulation…).
        if (r.Length > 0 && r[0] == '\u0002' && (e.Length == 0 || e[0] != '\u0002'))
        {
            findings.Add(new Finding("PREFIX_STX", Severity.Info, "Préfixe <STX> ajouté",
                "Le scanner ajoute le caractère de début de texte devant les données.",
                "Retire ce préfixe dans le scanner si l'application ne l'attend pas."));
            r = r[1..];
        }

        var suffix = TrailingSuffix(e, r);
        if (suffix.Length > 0)
        {
            r = r[..^suffix.Length];
            var visual = ControlCharacters.Visualize(suffix);
            findings.Add(new Finding("SUFFIX", Severity.Info, $"Suffixe ajouté : {visual}",
                suffix.Contains('\r') || suffix.Contains('\n')
                    ? "Le scanner ajoute « Entrée » après les données pour valider automatiquement la saisie. C'est le réglage le plus courant."
                    : "Le scanner ajoute un caractère de fin après les données.",
                "Si l'application valide deux fois ou passe au champ suivant à tort, retire ce suffixe dans le scanner."));
        }

        if (e == r)
        {
            return new ComparisonResult(false, true, findings);
        }

        // 3. Disposition clavier (AZERTY / QWERTY).
        if (KeyboardLayouts.ScannerUsOnPcFr(e) == r)
        {
            findings.Insert(0, new Finding("KEYBOARD_US_ON_FR", Severity.Error, "Mauvaise langue de clavier (scanner en QWERTY, PC en AZERTY)",
                "Le scanner « tape » comme sur un clavier américain alors que le PC est réglé en français : les chiffres deviennent &é\"'(-è_çà et A/Q, Z/W, M sont inversés.",
                "Règle la langue du clavier du scanner sur « Français (AZERTY) », ou utilise l'émulation par codes ALT / le mode HID POS qui ne dépend pas de la langue."));
            return new ComparisonResult(false, false, findings);
        }

        if (KeyboardLayouts.ScannerFrOnPcUs(e) == r)
        {
            findings.Insert(0, new Finding("KEYBOARD_FR_ON_US", Severity.Error, "Mauvaise langue de clavier (scanner en AZERTY, PC en QWERTY)",
                "Le scanner « tape » comme sur un clavier français alors que le PC est réglé en anglais : les chiffres deviennent !@#$%^&*() et A/Q, Z/W sont inversés.",
                "Règle la langue du clavier du scanner sur celle du PC, ou passe par le mode HID POS / codes ALT."));
            return new ComparisonResult(false, false, findings);
        }

        // 4. Séparateur GS1 (GS / FNC1).
        if (e.Contains(ControlCharacters.GroupSeparator))
        {
            var parts = e.Split(ControlCharacters.GroupSeparator);
            if (r == string.Concat(parts))
            {
                findings.Insert(0, new Finding("GS_LOST", Severity.Error, "Séparateur GS1 (GS) supprimé",
                    "Le code contient des séparateurs GS entre les champs variables (lot, série…). Ils ont disparu à la transmission : l'application ne peut plus savoir où finit un champ et où commence le suivant.",
                    "En mode clavier, le GS ne passe pas tel quel : configure le scanner pour le remplacer par un caractère que l'application sait traiter, ou utilise le mode HID POS / intent qui transmet les octets bruts."));
                return new ComparisonResult(false, false, findings);
            }

            var token = FindReplacement(parts, r);
            if (token is not null)
            {
                findings.Insert(0, new Finding("GS_REPLACED", Severity.Warning, $"Séparateur GS1 remplacé par « {ControlCharacters.Visualize(token)} »",
                    "Les champs GS1 sont bien séparés, mais par un autre caractère que GS. C'est correct seulement si l'application attend exactement ce caractère.",
                    "Vérifie que l'application (ou Loftware, ou le WMS) utilise le même remplaçant ; sinon aligne le réglage du scanner."));
                return new ComparisonResult(false, true, findings);
            }
        }

        // 5. Clé de contrôle et conversions UPC / EAN.
        if (e.All(char.IsAsciiDigit))
        {
            if (CheckDigit.LooksLikeGtin(e) && CheckDigit.IsValid(e) && r == e[..^1])
            {
                findings.Insert(0, new Finding("CHECK_DIGIT_DROPPED", Severity.Warning, "Clé de contrôle non transmise",
                    $"Le dernier chiffre ({e[^1]}) est la clé de contrôle : le scanner la lit mais ne l'envoie pas.",
                    "Active « Transmettre le chiffre de contrôle » pour cette symbologie dans le scanner."));
                return new ComparisonResult(false, false, findings);
            }

            if (e.Length == 12 && r == "0" + e)
            {
                findings.Insert(0, new Finding("UPCA_TO_EAN13", Severity.Info, "UPC-A converti en EAN-13",
                    "Le scanner ajoute un 0 devant le code UPC-A pour l'envoyer au format EAN-13. Les deux formes désignent le même article.",
                    "Désactive « Convertir UPC-A en EAN-13 » si l'application attend 12 chiffres."));
                return new ComparisonResult(false, true, findings);
            }

            if (e.Length == 13 && e[0] == '0' && r == e[1..])
            {
                findings.Insert(0, new Finding("EAN13_TO_UPCA", Severity.Info, "EAN-13 transmis au format UPC-A",
                    "Le 0 de tête de cet EAN-13 n'est pas transmis : le scanner l'envoie comme un UPC-A de 12 chiffres.",
                    "Active la transmission du chiffre système / la conversion vers EAN-13 si l'application attend 13 chiffres."));
                return new ComparisonResult(false, true, findings);
            }
        }

        if (r.Length < e.Length && r.Length > 0 && e.TrimStart('0') == r)
        {
            findings.Insert(0, new Finding("LEADING_ZEROS_REMOVED", Severity.Warning, "Zéros de tête supprimés",
                "Les zéros au début du code ont disparu. Ce n'est presque jamais le scanner : c'est l'application qui convertit le code en nombre (Excel, champ numérique…).",
                "Déclare le champ en texte dans l'application, ou format de cellule « Texte » dans Excel."));
            return new ComparisonResult(false, false, findings);
        }

        // 6. Caractères de début / fin du Code 39.
        if (r == "*" + e + "*")
        {
            findings.Insert(0, new Finding("CODE39_START_STOP", Severity.Info, "Caractères * du Code 39 transmis",
                "Les étoiles de début et de fin du Code 39 sont transmises avec les données.",
                "Désactive « Transmettre start/stop » pour le Code 39 dans le scanner."));
            return new ComparisonResult(false, true, findings);
        }

        // 7. Lecture en double.
        if (r == e + e)
        {
            findings.Insert(0, new Finding("DOUBLE_READ", Severity.Warning, "Code reçu deux fois",
                "Les données arrivent deux fois de suite : double déclenchement, mode de lecture continue ou délai entre lectures identiques trop court.",
                "Augmente le délai « même code » (same symbol timeout) ou passe en lecture déclenchée."));
            return new ComparisonResult(false, false, findings);
        }

        // 8. Troncature.
        if (r.Length < e.Length && e.StartsWith(r, StringComparison.Ordinal))
        {
            findings.Insert(0, new Finding("TRUNCATED", Severity.Error, $"Données tronquées : {r.Length} caractères reçus sur {e.Length}",
                "La fin du code manque. Causes probables : longueur maximale configurée pour cette symbologie, champ de saisie limité, ou transmission interrompue.",
                $"Vérifie la longueur maximale de la symbologie dans le scanner et la taille du champ de l'application (au moins {e.Length} caractères)."));
            return new ComparisonResult(false, false, findings);
        }

        // 9. Casse.
        if (string.Equals(e, r, StringComparison.OrdinalIgnoreCase))
        {
            findings.Insert(0, new Finding("CASE_CHANGED", Severity.Warning, "Majuscules et minuscules modifiées",
                "Les lettres sont identiques mais pas leur casse : verrouillage majuscules actif sur le PC, ou conversion de casse réglée dans le scanner.",
                "Désactive Verr. Maj, ou la conversion de casse du scanner. Certains scanners ont un réglage « ignorer Verr. Maj »."));
            return new ComparisonResult(false, false, findings);
        }

        // 10. Préfixe / suffixe quelconques autour des données.
        var at = r.IndexOf(e, StringComparison.Ordinal);
        if (at >= 0)
        {
            var pre = ControlCharacters.Visualize(r[..at]);
            var post = ControlCharacters.Visualize(r[(at + e.Length)..]);
            findings.Insert(0, new Finding("PREFIX_SUFFIX", Severity.Warning, "Données encadrées par des caractères ajoutés",
                $"Les données sont intactes mais entourées de caractères ajoutés{(pre.Length > 0 ? $" : préfixe « {pre} »" : string.Empty)}{(post.Length > 0 ? $"{(pre.Length > 0 ? "," : " :")} suffixe « {post} »" : string.Empty)}.",
                "Retire ces préfixes et suffixes dans le scanner, ou configure l'application pour les ignorer."));
            return new ComparisonResult(false, true, findings);
        }

        // 11. Accents et encodage.
        if (e.Length == r.Length && e.Any(c => c > 127))
        {
            var allDiffsAreNonAscii = true;
            for (var i = 0; i < e.Length; i++)
            {
                if (e[i] != r[i] && e[i] <= 127)
                {
                    allDiffsAreNonAscii = false;
                    break;
                }
            }

            if (allDiffsAreNonAscii)
            {
                findings.Insert(0, new Finding("ENCODING", Severity.Error, "Caractères accentués mal transmis",
                    "Seuls les caractères accentués ou spéciaux diffèrent : le jeu de caractères n'est pas le même entre le code, le scanner et l'application (UTF-8, Latin-1…).",
                    "Règle l'encodage du scanner (ECI / page de code) ou active la saisie par codes ALT / Unicode."));
                return new ComparisonResult(false, false, findings);
            }
        }

        findings.Insert(0, GenericDifference(e, r));
        return new ComparisonResult(false, false, findings);
    }

    private static string TrailingSuffix(string expected, string received)
    {
        var n = 0;
        while (n < received.Length && TrailingControls.Contains(received[received.Length - 1 - n]))
        {
            n++;
        }

        // Ne retire que ce qui n'est pas déjà dans le contenu attendu.
        var m = 0;
        while (m < expected.Length && TrailingControls.Contains(expected[expected.Length - 1 - m]))
        {
            m++;
        }

        return n > m ? received[^(n - m)..] : string.Empty;
    }

    private static string? FindReplacement(string[] parts, string received)
    {
        if (parts.Length < 2 || !received.StartsWith(parts[0], StringComparison.Ordinal))
        {
            return null;
        }

        var rest = received[parts[0].Length..];
        var next = parts[1];
        for (var len = 1; len <= Math.Min(6, rest.Length); len++)
        {
            var token = rest[..len];
            if (next.Length > 0 && !rest[len..].StartsWith(next, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Join(token, parts) == received)
            {
                return token;
            }
        }

        return null;
    }

    private static Finding GenericDifference(string e, string r)
    {
        var first = 0;
        while (first < Math.Min(e.Length, r.Length) && e[first] == r[first])
        {
            first++;
        }

        var expectedPart = ControlCharacters.Visualize(e[first..Math.Min(e.Length, first + 12)]);
        var receivedPart = ControlCharacters.Visualize(r[first..Math.Min(r.Length, first + 12)]);
        return new Finding("DIFFERENT", Severity.Error, "Données différentes",
            $"Premier écart à la position {first + 1} : attendu « {expectedPart} », reçu « {receivedPart} ». Longueurs : {e.Length} attendus, {r.Length} reçus.",
            "Vérifie que le bon code a été scanné, puis les règles de transformation du scanner (remplacement de caractères, modification des données).");
    }
}
