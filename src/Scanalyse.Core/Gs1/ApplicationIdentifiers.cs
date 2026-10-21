namespace Scanalyse.Core.Gs1;

/// <summary>Nature de la valeur portée par un identifiant d'application GS1.</summary>
public enum AiValueKind
{
    Text,
    Integer,
    Gtin,
    Sscc,
    Gln,
    Date,
    DateTime,
    Decimal,
    DecimalWithCurrency,
    Country,
    Countries,
}

/// <summary>Définition d'un identifiant d'application (AI) GS1.</summary>
/// <param name="Code">Code de l'AI (« 01 », « 3103 »…).</param>
/// <param name="Name">Libellé en français.</param>
/// <param name="Kind">Nature de la valeur.</param>
/// <param name="Unit">Unité éventuelle (kg, m…).</param>
public sealed record ApplicationIdentifier(string Code, string Name, AiValueKind Kind, string? Unit = null)
{
    /// <summary>Nombre de décimales pour les AI de mesure (dernier chiffre de l'AI), sinon 0.</summary>
    public int Decimals => Kind is AiValueKind.Decimal or AiValueKind.DecimalWithCurrency && Code.Length == 4
        ? Code[3] - '0'
        : 0;
}

/// <summary>Référentiel des identifiants d'application GS1 (Spécifications générales GS1).</summary>
public static class ApplicationIdentifiers
{
    private static readonly Dictionary<string, ApplicationIdentifier> Table = new ApplicationIdentifier[]
    {
        new("00", "SSCC (n° de colis ou palette)", AiValueKind.Sscc),
        new("01", "GTIN (article)", AiValueKind.Gtin),
        new("02", "GTIN des articles contenus", AiValueKind.Gtin),
        new("03", "GTIN de l'article de base", AiValueKind.Gtin),
        new("10", "Numéro de lot", AiValueKind.Text),
        new("11", "Date de production", AiValueKind.Date),
        new("12", "Date d'échéance", AiValueKind.Date),
        new("13", "Date d'emballage", AiValueKind.Date),
        new("15", "Date de durabilité minimale (DDM)", AiValueKind.Date),
        new("16", "Date limite de vente", AiValueKind.Date),
        new("17", "Date d'expiration (DLC)", AiValueKind.Date),
        new("20", "Variante de produit", AiValueKind.Text),
        new("21", "Numéro de série", AiValueKind.Text),
        new("22", "Version de produit (santé)", AiValueKind.Text),
        new("30", "Quantité variable", AiValueKind.Integer),
        new("37", "Nombre d'unités contenues", AiValueKind.Integer),
        new("235", "Extension de série tierce", AiValueKind.Text),
        new("240", "Référence article additionnelle", AiValueKind.Text),
        new("241", "Référence client", AiValueKind.Text),
        new("242", "Variante sur commande", AiValueKind.Text),
        new("243", "Code de composant d'emballage", AiValueKind.Text),
        new("250", "Numéro de série secondaire", AiValueKind.Text),
        new("251", "Référence de l'entité source", AiValueKind.Text),
        new("253", "GDTI (document)", AiValueKind.Text),
        new("254", "Extension de GLN", AiValueKind.Text),
        new("255", "GCN (coupon)", AiValueKind.Text),
        new("400", "N° de commande client", AiValueKind.Text),
        new("401", "GINC (n° d'envoi)", AiValueKind.Text),
        new("402", "GSIN (n° d'expédition)", AiValueKind.Text),
        new("403", "Code de routage", AiValueKind.Text),
        new("410", "Livrer à (GLN)", AiValueKind.Gln),
        new("411", "Facturer à (GLN)", AiValueKind.Gln),
        new("412", "Acheté auprès de (GLN)", AiValueKind.Gln),
        new("413", "Destinataire final (GLN)", AiValueKind.Gln),
        new("414", "Emplacement physique (GLN)", AiValueKind.Gln),
        new("415", "Facturant (GLN)", AiValueKind.Gln),
        new("416", "Lieu de production (GLN)", AiValueKind.Gln),
        new("417", "Partie (GLN)", AiValueKind.Gln),
        new("420", "Code postal de livraison", AiValueKind.Text),
        new("421", "Code postal de livraison avec pays", AiValueKind.Text),
        new("422", "Pays d'origine", AiValueKind.Country),
        new("423", "Pays de transformation initiale", AiValueKind.Countries),
        new("424", "Pays de transformation", AiValueKind.Country),
        new("425", "Pays de démontage", AiValueKind.Countries),
        new("426", "Pays de toute la chaîne de transformation", AiValueKind.Country),
        new("427", "Subdivision du pays d'origine", AiValueKind.Text),
        new("7001", "Code OTAN (NSN)", AiValueKind.Text),
        new("7002", "Classification de carcasse (UN/ECE)", AiValueKind.Text),
        new("7003", "Date et heure d'expiration", AiValueKind.DateTime),
        new("7004", "Puissance active", AiValueKind.Text),
        new("7005", "Zone de capture", AiValueKind.Text),
        new("7006", "Date de premier gel", AiValueKind.Date),
        new("7007", "Date de récolte", AiValueKind.Text),
        new("7008", "Espèce (pêche)", AiValueKind.Text),
        new("7009", "Type d'engin de pêche", AiValueKind.Text),
        new("7010", "Méthode de production", AiValueKind.Text),
        new("7240", "Protocole", AiValueKind.Text),
        new("8001", "Produit en rouleau (dimensions)", AiValueKind.Text),
        new("8002", "N° de téléphone mobile", AiValueKind.Text),
        new("8003", "GRAI (actif réutilisable)", AiValueKind.Text),
        new("8004", "GIAI (actif individuel)", AiValueKind.Text),
        new("8005", "Prix unitaire", AiValueKind.Text),
        new("8006", "ITIP (pièce d'un ensemble)", AiValueKind.Text),
        new("8007", "IBAN", AiValueKind.Text),
        new("8008", "Date et heure de production", AiValueKind.Text),
        new("8010", "CPID (identifiant de composant)", AiValueKind.Text),
        new("8011", "N° de série du composant", AiValueKind.Text),
        new("8012", "Version logicielle", AiValueKind.Text),
        new("8013", "GMN (modèle de produit)", AiValueKind.Text),
        new("8017", "GSRN prestataire", AiValueKind.Text),
        new("8018", "GSRN bénéficiaire", AiValueKind.Text),
        new("8019", "SRIN (instance de service)", AiValueKind.Text),
        new("8020", "N° de bulletin de paiement", AiValueKind.Text),
        new("8026", "ITIP des pièces contenues", AiValueKind.Text),
        new("8030", "Signature numérique", AiValueKind.Text),
        new("8200", "URL de l'emballage étendu", AiValueKind.Text),
        new("90", "Information convenue entre partenaires", AiValueKind.Text),
    }.ToDictionary(a => a.Code, StringComparer.Ordinal);

    private static readonly Dictionary<string, (string Name, string Unit)> Measures = new(StringComparer.Ordinal)
    {
        ["310"] = ("Poids net", "kg"),
        ["311"] = ("Longueur", "m"),
        ["312"] = ("Largeur", "m"),
        ["313"] = ("Hauteur", "m"),
        ["314"] = ("Surface", "m²"),
        ["315"] = ("Volume net", "l"),
        ["316"] = ("Volume net", "m³"),
        ["320"] = ("Poids net", "lb"),
        ["330"] = ("Poids brut", "kg"),
        ["331"] = ("Longueur logistique", "m"),
        ["332"] = ("Largeur logistique", "m"),
        ["333"] = ("Hauteur logistique", "m"),
        ["334"] = ("Surface logistique", "m²"),
        ["335"] = ("Volume brut", "l"),
        ["336"] = ("Volume logistique", "m³"),
        ["337"] = ("Masse surfacique", "kg/m²"),
        ["390"] = ("Montant à payer", "monnaie locale"),
        ["391"] = ("Montant à payer", "devise ISO"),
        ["392"] = ("Prix (article à prix variable)", "monnaie locale"),
        ["393"] = ("Prix (article à prix variable)", "devise ISO"),
        ["394"] = ("Remise du coupon", "%"),
        ["395"] = ("Montant par unité de mesure", "monnaie locale"),
    };

    /// <summary>
    /// Longueurs prédéfinies (données seules, sans l'AI) selon les deux premiers chiffres :
    /// ces champs n'ont jamais besoin de séparateur GS après eux.
    /// </summary>
    private static readonly Dictionary<string, int> PredefinedLengths = new(StringComparer.Ordinal)
    {
        ["00"] = 18, ["01"] = 14, ["02"] = 14, ["03"] = 14, ["04"] = 16,
        ["11"] = 6, ["12"] = 6, ["13"] = 6, ["14"] = 6, ["15"] = 6, ["16"] = 6, ["17"] = 6, ["18"] = 6, ["19"] = 6,
        ["20"] = 2,
        ["31"] = 6, ["32"] = 6, ["33"] = 6, ["34"] = 6, ["35"] = 6, ["36"] = 6,
        ["41"] = 13,
    };

    /// <summary>Recherche la définition d'un AI. Les AI de mesure (310n…) et internes (91-99) sont générés.</summary>
    public static ApplicationIdentifier? Find(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (Table.TryGetValue(code, out var ai))
        {
            return ai;
        }

        if (code.Length == 4 && Measures.TryGetValue(code[..3], out var m) && char.IsAsciiDigit(code[3]))
        {
            var kind = code.StartsWith("391", StringComparison.Ordinal) || code.StartsWith("393", StringComparison.Ordinal)
                ? AiValueKind.DecimalWithCurrency
                : AiValueKind.Decimal;
            return new ApplicationIdentifier(code, $"{m.Name} ({m.Unit})", kind, m.Unit);
        }

        if (code.Length == 2 && code[0] == '9' && code[1] is >= '1' and <= '9')
        {
            return new ApplicationIdentifier(code, "Information interne de l'entreprise", AiValueKind.Text);
        }

        if (code.Length == 4 && code.StartsWith("703", StringComparison.Ordinal))
        {
            return new ApplicationIdentifier(code, "N° d'approbation du transformateur", AiValueKind.Text);
        }

        return null;
    }

    /// <summary>Longueur de données prédéfinie pour un AI, ou <c>null</c> si le champ est variable (terminé par GS).</summary>
    public static int? PredefinedLength(string code) =>
        code.Length >= 2 && PredefinedLengths.TryGetValue(code[..2], out var n) ? n : null;

    /// <summary>
    /// Détermine combien de chiffres forment l'AI au début de <paramref name="data"/>.
    /// </summary>
    public static int AiLength(ReadOnlySpan<char> data)
    {
        foreach (var n in (ReadOnlySpan<int>)[4, 3, 2])
        {
            if (data.Length >= n && Table.ContainsKey(data[..n].ToString()))
            {
                return n;
            }
        }

        if (data.Length < 2)
        {
            return data.Length;
        }

        var two = data[..2].ToString();
        return two switch
        {
            "31" or "32" or "33" or "34" or "35" or "36" or "39" or "43" or "70" or "72" => 4,
            _ when two[0] == '8' => 4,
            "23" or "24" or "25" or "40" or "41" or "42" or "71" => 3,
            _ => 2,
        };
    }
}
