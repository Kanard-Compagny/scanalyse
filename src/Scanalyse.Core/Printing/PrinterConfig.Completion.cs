using System.Globalization;
using System.Text.RegularExpressions;

namespace Scanalyse.Core.Printing;

/// <summary>Commande proposée pendant la saisie d'un fichier de configuration (bibliothèque intégrée).</summary>
/// <param name="Language">Langage (ZPL, SGD, TSPL, DPL, Fingerprint, SBPL, EPL2).</param>
/// <param name="Label">Ce qui s'affiche dans la liste (ex. « ^MD », « print.tone »).</param>
/// <param name="Text">Texte inséré à la place de ce qui est tapé.</param>
/// <param name="CaretOffset">Position du curseur dans <paramref name="Text"/> après insertion.</param>
/// <param name="Description">Rôle de la commande et valeurs acceptées.</param>
public sealed record ConfigSnippet(string Language, string Label, string Text, int CaretOffset, string Description);

/// <summary>Propositions pour la position du curseur : remplacer les <paramref name="ReplaceLength"/> caractères avant le curseur.</summary>
/// <param name="ReplaceLength">Nombre de caractères tapés, juste avant le curseur, remplacés par la proposition choisie.</param>
/// <param name="Items">Propositions, les plus proches d'abord.</param>
public sealed record ConfigCompletion(int ReplaceLength, IReadOnlyList<ConfigSnippet> Items);

public static partial class PrinterConfig
{
    private const string Caret = "{|}";

    private static ConfigSnippet Snip(string language, string label, string insert, string description)
    {
        var at = insert.IndexOf(Caret, StringComparison.Ordinal);
        var text = at < 0 ? insert : insert.Remove(at, Caret.Length);
        return new ConfigSnippet(language, label, text, at < 0 ? text.Length : at, description);
    }

    /// <summary>Bibliothèque des commandes de configuration proposées à la saisie (hors noms SGD, tirés de la table des réglages).</summary>
    public static IReadOnlyList<ConfigSnippet> Library { get; } =
    [
        // Zebra SGD (début de ligne)
        Snip("SGD", "! U1 setvar", "! U1 setvar \"{|}\" \"\"", "Change un réglage Zebra : ! U1 setvar \"nom\" \"valeur\""),
        Snip("SGD", "! U1 getvar", "! U1 getvar \"{|}\"", "Lit un réglage Zebra : ! U1 getvar \"nom\""),
        Snip("SGD", "! U1 do", "! U1 do \"{|}\" \"\"", "Lance une action Zebra (redémarrage, retour usine…) : ! U1 do \"nom\" \"\""),
        Snip("SGD", "! U1 getvar \"allcv\"", "! U1 getvar \"allcv\"", "Lit tous les réglages de l'imprimante avec leurs valeurs acceptées"),
        Snip("SGD", "{}{\"…\":\"…\"}", "{}{\"{|}\":\"\"}", "Réglages Zebra au format JSON : {}{\"nom\":\"valeur\",…}"),

        // ZPL
        Snip("ZPL", "^XA", "^XA", "Début de format"),
        Snip("ZPL", "^XZ", "^XZ", "Fin de format (les commandes sont appliquées)"),
        Snip("ZPL", "~SD", "~SD{|}", "Noirceur (contraste) : 00 à 30"),
        Snip("ZPL", "^MD", "^MD{|}", "Ajustement de noirceur, relatif au réglage ~SD : -30 à 30"),
        Snip("ZPL", "^PR", "^PR{|}", "Vitesse d'impression (pouces par seconde) : 1 à 14, puis vitesses d'avance et de recul"),
        Snip("ZPL", "^MN", "^MN{|}", "Suivi du support : N continu, Y ou W espace, M marque noire, A automatique, V continu variable"),
        Snip("ZPL", "^MT", "^MT{|}", "Méthode d'impression : T transfert thermique (ruban), D thermique direct"),
        Snip("ZPL", "^MM", "^MM{|}", "Mode de sortie : T détachage, P prédécollage, R enroulement, A applicateur, C coupe, D coupe différée, K kiosque"),
        Snip("ZPL", "^PW", "^PW{|}", "Largeur d'impression (points)"),
        Snip("ZPL", "^LL", "^LL{|}", "Longueur d'étiquette (points)"),
        Snip("ZPL", "^LS", "^LS{|}", "Décalage horizontal de l'étiquette (points) : -9999 à 9999"),
        Snip("ZPL", "^LT", "^LT{|}", "Décalage vertical de l'étiquette (points) : -120 à 120"),
        Snip("ZPL", "~TA", "~TA{|}", "Position de détachage (points) : -120 à 120"),
        Snip("ZPL", "^LH", "^LH{|}", "Origine de l'étiquette (x, y en points)"),
        Snip("ZPL", "^PO", "^PO{|}", "Orientation : N normale, I retournée (180°)"),
        Snip("ZPL", "^MF", "^MF{|}", "Action à la mise sous tension, puis à la fermeture de la tête : F avance, C calibrage, L longueur, N aucun mouvement, S calibrage court"),
        Snip("ZPL", "^JUS", "^JUS", "Enregistre les réglages (conservés à l'extinction) : à placer juste avant ^XZ"),
        Snip("ZPL", "^JUR", "^JUR", "Recharge les derniers réglages enregistrés"),
        Snip("ZPL", "^JUF", "^JUF", "Retour aux réglages d'usine (hors réseau)"),
        Snip("ZPL", "^JUN", "^JUN", "Retour aux réglages réseau d'usine"),
        Snip("ZPL", "^JUA", "^JUA", "Retour aux réglages d'usine (y compris réseau)"),
        Snip("ZPL", "~JC", "~JC", "Calibrage du capteur d'étiquettes"),
        Snip("ZPL", "~JL", "~JL", "Mesure de la longueur d'étiquette"),
        Snip("ZPL", "~WC", "~WC", "Impression de l'étiquette de configuration"),
        Snip("ZPL", "~HS", "~HS", "Demande d'état à l'imprimante"),
        Snip("ZPL", "~JA", "~JA", "Annulation de tous les travaux en attente"),
        Snip("ZPL", "^KN", "^KN{|},", "Nom de l'imprimante (16 caractères) puis description"),
        Snip("ZPL", "^KP", "^KP{|}", "Mot de passe du panneau de commande (4 chiffres)"),
        Snip("ZPL", "^NS", "^NS{|},,,", "Réseau filaire : attribution (A auto, P fixe…), IP, masque, passerelle"),
        Snip("ZPL", "^ND", "^ND{|},,,", "Réseau Wi-Fi : attribution, IP, masque, passerelle"),
        Snip("ZPL", "^SC", "^SC{|},8,N,1", "Port série : bauds, bits, parité, bits d'arrêt"),
        Snip("ZPL", "^CI", "^CI{|}", "Jeu de caractères (28 = UTF-8)"),
        Snip("ZPL", "^SZ", "^SZ2", "Mode ZPL II (^SZ1 = ZPL I)"),
        Snip("ZPL", "^JZ", "^JZ{|}", "Réimpression après une erreur : Y oui, N non"),
        Snip("ZPL", "^FX", "^FX{|}", "Commentaire"),

        // TSC TSPL
        Snip("TSPL", "SIZE", "SIZE {|} mm, mm", "Taille d'étiquette : largeur, hauteur (mm ou pouces)"),
        Snip("TSPL", "GAP", "GAP {|} mm, 0 mm", "Espace entre étiquettes et décalage (0, 0 = support continu)"),
        Snip("TSPL", "BLINE", "BLINE {|} mm, 0 mm", "Marque noire : hauteur et décalage"),
        Snip("TSPL", "SPEED", "SPEED {|}", "Vitesse d'impression (pouces par seconde), selon le modèle : 2, 3, 4, 5, 6…"),
        Snip("TSPL", "DENSITY", "DENSITY {|}", "Noirceur : 0 à 15"),
        Snip("TSPL", "DIRECTION", "DIRECTION {|},0", "Sens d'impression (0 ou 1), puis miroir (0 ou 1)"),
        Snip("TSPL", "REFERENCE", "REFERENCE {|},0", "Origine de l'étiquette (x, y en points)"),
        Snip("TSPL", "OFFSET", "OFFSET {|} mm", "Décalage d'arrêt après impression (prédécollage, coupe)"),
        Snip("TSPL", "SHIFT", "SHIFT {|}", "Décalage vertical d'impression (points)"),
        Snip("TSPL", "CODEPAGE", "CODEPAGE {|}", "Jeu de caractères (ex. 1252, UTF-8)"),
        Snip("TSPL", "COUNTRY", "COUNTRY {|}", "Pays (clavier, caractères) : ex. 033 France"),
        Snip("TSPL", "LIMITFEED", "LIMITFEED {|} mm", "Longueur maximale avant erreur de détection"),
        Snip("TSPL", "SET CUTTER", "SET CUTTER {|}", "Massicot : OFF, BATCH ou toutes les n étiquettes"),
        Snip("TSPL", "SET PARTIAL_CUTTER", "SET PARTIAL_CUTTER {|}", "Coupe partielle : OFF, BATCH ou toutes les n étiquettes"),
        Snip("TSPL", "SET BACK", "SET BACK {|}", "Recul après coupe : ON ou OFF"),
        Snip("TSPL", "SET TEAR", "SET TEAR {|}", "Position de détachage : ON ou OFF"),
        Snip("TSPL", "SET PEEL", "SET PEEL {|}", "Prédécollage : ON ou OFF"),
        Snip("TSPL", "SET GAP", "SET GAP {|}", "Détection de l'espace : AUTO, OFF, 0, REVERSE…"),
        Snip("TSPL", "SET HEAD", "SET HEAD {|}", "Contrôle d'ouverture de la tête : ON ou OFF"),
        Snip("TSPL", "SET RIBBON", "SET RIBBON {|}", "Ruban : ON transfert thermique, OFF thermique direct"),
        Snip("TSPL", "SET REWIND", "SET REWIND {|}", "Enrouleur : ON, OFF ou RS232"),
        Snip("TSPL", "SET REPRINT", "SET REPRINT {|}", "Réimpression après une erreur : ON ou OFF"),
        Snip("TSPL", "SET PRINTKEY", "SET PRINTKEY {|}", "Impression par la touche FEED : ON, OFF, AUTO ou n"),
        Snip("TSPL", "SET COM1", "SET COM1 {|},N,8,1", "Port série : bauds, parité, bits, arrêt"),
        Snip("TSPL", "INITIALPRINTER", "INITIALPRINTER", "Retour aux réglages d'usine"),
        Snip("TSPL", "SELFTEST", "SELFTEST", "Impression de l'étiquette d'autotest (configuration)"),
        Snip("TSPL", "GAPDETECT", "GAPDETECT", "Calibrage du capteur d'espace"),
        Snip("TSPL", "BLINEDETECT", "BLINEDETECT", "Calibrage du capteur de marque noire"),
        Snip("TSPL", "AUTODETECT", "AUTODETECT", "Calibrage automatique du capteur"),

        // EPL2
        Snip("EPL2", "q", "q{|}", "Largeur d'étiquette (points)"),
        Snip("EPL2", "Q", "Q{|},24", "Longueur d'étiquette et espace (points)"),
        Snip("EPL2", "S", "S{|}", "Vitesse d'impression (code 1 à 6)"),
        Snip("EPL2", "D", "D{|}", "Noirceur : 0 à 15"),
        Snip("EPL2", "OD", "OD", "Options matérielles : thermique direct"),
        Snip("EPL2", "ZT", "ZT", "Impression depuis le haut"),
        Snip("EPL2", "ZB", "ZB", "Impression depuis le bas (retournée)"),
        Snip("EPL2", "JF", "JF", "Recul avant impression activé"),
        Snip("EPL2", "JB", "JB", "Recul avant impression désactivé"),
        Snip("EPL2", "xa", "xa", "Calibrage automatique du capteur"),
        Snip("EPL2", "U", "U", "Demande de configuration"),

        // Honeywell / Datamax DPL
        Snip("DPL", "<STX>e", "<STX>e", "Capteur d'espace (étiquettes séparées)"),
        Snip("DPL", "<STX>r", "<STX>r", "Capteur réfléchissant (marque noire)"),
        Snip("DPL", "<STX>c", "<STX>c{|}", "Support continu, longueur (0,01 pouce)"),
        Snip("DPL", "<STX>M", "<STX>M{|}", "Longueur maximale d'étiquette (0,01 pouce, 4 chiffres)"),
        Snip("DPL", "<STX>O", "<STX>O{|}", "Décalage du début d'impression (4 chiffres)"),
        Snip("DPL", "<STX>f", "<STX>f{|}", "Position d'arrêt (détachage) (3 chiffres)"),
        Snip("DPL", "<STX>m", "<STX>m", "Unités métriques (0,1 mm)"),
        Snip("DPL", "<STX>n", "<STX>n", "Unités en pouces (0,01 pouce)"),
        Snip("DPL", "<STX>Kc", "<STX>Kc{|}", "Modification de la configuration enregistrée (voir le manuel DPL du modèle)"),
        Snip("DPL", "<STX>Z", "<STX>Z", "Demande de la configuration"),
        Snip("DPL", "<STX>L", "<STX>L", "Début d'un format d'étiquette"),
        Snip("DPL", "H", "H{|}", "Chaleur (noirceur) du format : 00 à 30"),
        Snip("DPL", "D11", "D11", "Taille des points : 1 × 1"),
        Snip("DPL", "P", "P{|}", "Vitesse d'impression (code)"),
        Snip("DPL", "E", "E", "Fin du format (impression)"),
        Snip("DPL", "<SOH>#", "<SOH>#", "Arrêt immédiat (annulation)"),

        // Honeywell / Intermec Fingerprint
        Snip("Fingerprint", "SETUP \"MEDIA\",\"MEDIA TYPE\"", "SETUP \"MEDIA\",\"MEDIA TYPE\",\"{|}\"", "Type de support : LABEL (W GAPS), TICKET (W MARK), TICKET (W GAPS), FIX LENGTH STRIP, VAR LENGTH STRIP"),
        Snip("Fingerprint", "SETUP \"MEDIA\",\"PAPER TYPE\"", "SETUP \"MEDIA\",\"PAPER TYPE\",\"{|}\"", "Méthode d'impression : DIRECT THERMAL ou TRANSFER"),
        Snip("Fingerprint", "SETUP \"MEDIA\",\"CONTRAST\"", "SETUP \"MEDIA\",\"CONTRAST\",\"{|}\"", "Noirceur : -10% à +10%"),
        Snip("Fingerprint", "SETUP \"MEDIA\",\"LENGTH (SLOT)\"", "SETUP \"MEDIA\",\"LENGTH (SLOT)\",{|}", "Longueur d'étiquette (points)"),
        Snip("Fingerprint", "SETUP \"PRINT DEFS\",\"PRINT SPEED\"", "SETUP \"PRINT DEFS\",\"PRINT SPEED\",{|}", "Vitesse d'impression (mm/s, ex. 100, 150, 200)"),
        Snip("Fingerprint", "SETUP \"FEEDADJ\",\"STARTADJ\"", "SETUP \"FEEDADJ\",\"STARTADJ\",{|}", "Décalage du début d'impression (points)"),
        Snip("Fingerprint", "SETUP \"FEEDADJ\",\"STOPADJ\"", "SETUP \"FEEDADJ\",\"STOPADJ\",{|}", "Position d'arrêt (détachage) (points)"),
        Snip("Fingerprint", "SETUP \"NETWORK\",\"IP SELECTION\"", "SETUP \"NETWORK\",\"IP SELECTION\",\"{|}\"", "Attribution de l'adresse IP : DHCP, BOOTP, MANUAL"),
        Snip("Fingerprint", "SETUP \"NETWORK\",\"IP ADDRESS\"", "SETUP \"NETWORK\",\"IP ADDRESS\",\"{|}\"", "Adresse IP (filaire)"),

        // SATO SBPL
        Snip("SBPL", "<ESC>A", "<ESC>A", "Début de bloc"),
        Snip("SBPL", "<ESC>Z", "<ESC>Z", "Fin de bloc (exécution)"),
        Snip("SBPL", "<ESC>CS", "<ESC>CS{|}", "Vitesse d'impression (code propre au modèle)"),
        Snip("SBPL", "<ESC>#E", "<ESC>#E{|}", "Noirceur : niveau 1 à 5, éventuellement suivi d'une lettre A à F"),
        Snip("SBPL", "<ESC>IG", "<ESC>IG{|}", "Capteur : 1 réflexion (marque noire), 2 transmission (espace), 3 ignoré"),
        Snip("SBPL", "<ESC>A3", "<ESC>A3V+{|}H+000", "Correction du point de départ (non enregistrée)"),
        Snip("SBPL", "<ESC>PG", "<ESC>PG{|}", "Enregistrement des réglages dans la mémoire de l'imprimante (voir le manuel SBPL du modèle)"),
    ];

    /// <summary>Bibliothèque complète : commandes détaillées ci-dessus, puis toutes les autres commandes ZPL du catalogue.</summary>
    private static readonly Lazy<IReadOnlyList<ConfigSnippet>> AllSnippets = new(BuildSnippets);

    private static List<ConfigSnippet> BuildSnippets() =>
        Library.Concat(ZplCatalog
                .Where(kv => !Library.Any(s => s.Language == "ZPL" && s.Label == kv.Key))
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => Snip("ZPL", kv.Key, kv.Key + Caret, kv.Value.Meaning)))
            .ToList();

    /// <summary>Noms SGD proposés à la saisie : réglages connus, avec les valeurs acceptées.</summary>
    private static IEnumerable<(string Key, SgdDef Def)> SgdKeys => SgdTable.Select(kv => (kv.Key, kv.Value));

    /// <summary>Valeurs acceptées d'un réglage SGD, en clair (pour la liste de propositions).</summary>
    private static string Allowed(SgdDef d) => d.Kind switch
    {
        Kind.Number or Kind.Integer => string.Format(CultureInfo.InvariantCulture, "{0} à {1}", d.Min, d.Max),
        Kind.Choice => string.Join(" | ", d.Choices ?? []),
        Kind.OnOff => "on | off",
        Kind.Ip => "adresse IP",
        Kind.Do => "action",
        Kind.ReadOnly => "lecture seule",
        _ => d.Choices is { } usual ? "ex. " + string.Join(" | ", usual) : string.Empty,
    };

    /// <summary>
    /// Propositions pour la ligne en cours de saisie (<paramref name="linePrefix"/> = texte de la ligne jusqu'au curseur) :
    /// nom ou valeur SGD entre guillemets, commande ZPL après ^ ou ~, ou commande en début de ligne (tous langages).
    /// <paramref name="force"/> (Ctrl + Espace) propose toute la bibliothèque même sans rien de tapé. Avec les réglages lus
    /// sur l'imprimante (<paramref name="printer"/>), les noms et valeurs SGD proposés sont les siens.
    /// </summary>
    public static ConfigCompletion Suggest(string linePrefix, bool force = false, IReadOnlyDictionary<string, PrinterValue>? printer = null)
    {
        ArgumentNullException.ThrowIfNull(linePrefix);
        printer = printer is { Count: > 0 } ? printer : null;

        // Valeur SGD : ! U1 setvar "nom" "…
        var value = SgdValueRegex().Match(linePrefix);
        if (value.Success)
        {
            var key = value.Groups[1].Value.ToLowerInvariant();
            var typed = value.Groups[2].Value;
            SgdTable.TryGetValue(key, out var known);

            // Imprimante lue : ses choix annoncés (liste, pas une plage de nombres).
            if (printer is not null && printer.TryGetValue(key, out var onPrinter) && onPrinter.Choices is { } announced && !RangeChoice().IsMatch(announced.Trim()))
            {
                var fromPrinter = announced.Split(',')
                    .Select(c => c.Trim())
                    .Where(c => c.Length > 0 && c.Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .Select(c => new ConfigSnippet("SGD", c, c + "\"", c.Length + 1, (known?.Label ?? key) + " (valeur acceptée par l'imprimante)"))
                    .ToList();
                return new ConfigCompletion(typed.Length, fromPrinter);
            }

            // Valeurs proposées : choix fermés, on / off, ou valeurs usuelles d'un réglage libre (pays Wi-Fi, bandes…).
            if (SgdTable.TryGetValue(key, out var def) && (def.Kind is Kind.Choice or Kind.OnOff || def.Choices is not null))
            {
                var choices = def.Kind == Kind.OnOff ? ["on", "off"] : def.Choices ?? [];
                var items = choices
                    .Where(c => c.Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .Select(c => new ConfigSnippet("SGD", c, c + "\"", c.Length + 1, def.Label))
                    .ToList();
                return new ConfigCompletion(typed.Length, items);
            }

            return new ConfigCompletion(0, []);
        }

        // Nom SGD : ! U1 setvar "…
        var name = SgdNameRegex().Match(linePrefix);
        if (name.Success)
        {
            var verb = name.Groups[1].Value.ToLowerInvariant();
            var typed = name.Groups[2].Value;
            if (printer is not null && verb != "do")
            {
                var fromPrinter = printer
                    .Where(kv => verb != "setvar" || !(SgdTable.TryGetValue(kv.Key, out var d) && d.Kind == Kind.ReadOnly))
                    .Where(kv => typed.Length == 0 || kv.Key.Contains(typed, StringComparison.OrdinalIgnoreCase)
                        || (SgdTable.TryGetValue(kv.Key, out var d2) && d2.Label.Contains(typed, StringComparison.OrdinalIgnoreCase)))
                    .OrderBy(kv => kv.Key.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : kv.Key.Contains(typed, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .Take(400)
                    .Select(kv =>
                    {
                        var label = SgdTable.TryGetValue(kv.Key, out var d3) ? d3.Label : "Réglage de l'imprimante";
                        var description = $"{label} · imprimante : « {kv.Value.Value} »" + (kv.Value.Choices is { Length: > 0 } c ? $" · accepte : {c}" : string.Empty);
                        var text = verb == "getvar" ? kv.Key + "\"" : kv.Key + "\" \"\"";
                        return new ConfigSnippet("SGD", kv.Key, text, verb == "getvar" ? text.Length : text.Length - 1, description);
                    })
                    .ToList();
                return new ConfigCompletion(typed.Length, fromPrinter);
            }

            var items = SgdKeys
                .Where(k => (verb == "do") == (k.Def.Kind == Kind.Do) || verb == "getvar")
                .Where(k => verb != "setvar" || k.Def.Kind != Kind.ReadOnly)
                .Where(k => typed.Length == 0 || k.Key.Contains(typed, StringComparison.OrdinalIgnoreCase) || k.Def.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
                .OrderBy(k => k.Key.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : k.Key.Contains(typed, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
                .ThenBy(k => k.Key, StringComparer.Ordinal)
                .Select(k =>
                {
                    var allowed = Allowed(k.Def);
                    var description = allowed.Length == 0 ? k.Def.Label : k.Def.Label + " : " + allowed;
                    var text = verb == "getvar" ? k.Key + "\"" : k.Key + "\" \"\"";
                    return new ConfigSnippet("SGD", k.Key, text, verb == "getvar" ? text.Length : text.Length - 1, description);
                })
                .ToList();
            return new ConfigCompletion(typed.Length, items);
        }

        // Commande ZPL au milieu d'une ligne : ^M…, ~S…
        var zpl = ZplWordRegex().Match(linePrefix);
        if (zpl.Success)
        {
            var typed = zpl.Value;
            var items = AllSnippets.Value
                .Where(s => s.Language == "ZPL" && s.Label.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .Where(s => !string.Equals(s.Text, typed, StringComparison.Ordinal))
                .ToList();
            if (items.Count > 0 || typed.Length > 1)
            {
                return new ConfigCompletion(typed.Length, items);
            }
        }

        // Début de ligne : toute la bibliothèque
        var start = linePrefix.TrimStart();
        if (start.Length == 0 && !force)
        {
            return new ConfigCompletion(0, []);
        }

        var matches = AllSnippets.Value
            .Select(s => (s, rank: s.Label.StartsWith(start, StringComparison.OrdinalIgnoreCase) ? 0
                : start.Length >= 3 && s.Description.Contains(start, StringComparison.OrdinalIgnoreCase) ? 1 : -1))
            .Where(x => x.rank >= 0)
            .OrderBy(x => x.rank)
            .ThenBy(x => x.s.Label.Length)
            .Select(x => x.s)
            .ToList();

        // Un mot entier déjà tapé tel quel (ex. « ^XA ») n'a plus rien à proposer.
        if (matches.Count == 1 && string.Equals(matches[0].Text, start, StringComparison.Ordinal))
        {
            matches.Clear();
        }

        return new ConfigCompletion(start.Length, matches);
    }

    [GeneratedRegex("""^\s*!\s*U1\s+(setvar|getvar|do)\s+"([^"\s]*)$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SgdNameRegex();

    [GeneratedRegex("""^\s*!\s*U1\s+(?:setvar|do)\s+"([^"]+)"\s+"([^"]*)$""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SgdValueRegex();

    [GeneratedRegex(@"[\^~][A-Za-z0-9]{0,2}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZplWordRegex();
}
