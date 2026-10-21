namespace Scanalyse.Core.Gs1;

/// <summary>
/// Préfixes GS1 : ils indiquent l'organisation GS1 qui a attribué le numéro,
/// pas le pays de fabrication du produit.
/// </summary>
public static class Gs1Prefixes
{
    private static readonly (int From, int To, string Label)[] Ranges =
    [
        (0, 19, "États-Unis / Canada"), (20, 29, "usage interne (poids ou prix variable, codes magasin)"),
        (30, 39, "États-Unis (médicaments)"), (40, 49, "usage interne restreint"), (50, 59, "coupons (États-Unis)"),
        (60, 139, "États-Unis / Canada"), (200, 299, "usage interne (poids ou prix variable, codes magasin)"),
        (300, 379, "France et Monaco"), (380, 380, "Bulgarie"), (383, 383, "Slovénie"), (385, 385, "Croatie"),
        (387, 387, "Bosnie-Herzégovine"), (389, 389, "Monténégro"), (400, 440, "Allemagne"), (450, 459, "Japon"),
        (460, 469, "Russie"), (471, 471, "Taïwan"), (474, 474, "Estonie"), (475, 475, "Lettonie"), (477, 477, "Lituanie"),
        (479, 479, "Sri Lanka"), (480, 480, "Philippines"), (481, 481, "Biélorussie"), (482, 482, "Ukraine"),
        (484, 484, "Moldavie"), (485, 485, "Arménie"), (486, 486, "Géorgie"), (487, 487, "Kazakhstan"),
        (489, 489, "Hong Kong"), (490, 499, "Japon"), (500, 509, "Royaume-Uni"), (520, 521, "Grèce"),
        (528, 528, "Liban"), (529, 529, "Chypre"), (531, 531, "Macédoine du Nord"), (535, 535, "Malte"),
        (539, 539, "Irlande"), (540, 549, "Belgique et Luxembourg"), (560, 560, "Portugal"), (569, 569, "Islande"),
        (570, 579, "Danemark"), (590, 590, "Pologne"), (594, 594, "Roumanie"), (599, 599, "Hongrie"),
        (600, 601, "Afrique du Sud"), (611, 611, "Maroc"), (613, 613, "Algérie"), (619, 619, "Tunisie"),
        (622, 622, "Égypte"), (628, 628, "Arabie saoudite"), (629, 629, "Émirats arabes unis"), (640, 649, "Finlande"),
        (690, 699, "Chine"), (700, 709, "Norvège"), (729, 729, "Israël"), (730, 739, "Suède"), (750, 750, "Mexique"),
        (754, 755, "Canada"), (760, 769, "Suisse et Liechtenstein"), (770, 771, "Colombie"), (773, 773, "Uruguay"),
        (775, 775, "Pérou"), (778, 779, "Argentine"), (780, 780, "Chili"), (789, 790, "Brésil"), (800, 839, "Italie"),
        (840, 849, "Espagne et Andorre"), (850, 850, "Cuba"), (858, 858, "Slovaquie"), (859, 859, "Tchéquie"),
        (860, 860, "Serbie"), (868, 869, "Turquie"), (870, 879, "Pays-Bas"), (880, 880, "Corée du Sud"),
        (885, 885, "Thaïlande"), (888, 888, "Singapour"), (890, 890, "Inde"), (893, 893, "Vietnam"),
        (899, 899, "Indonésie"), (900, 919, "Autriche"), (930, 939, "Australie"), (940, 949, "Nouvelle-Zélande"),
        (950, 950, "GS1 Global Office"), (955, 955, "Malaisie"), (958, 958, "Macao"),
        (960, 969, "GS1 (numéros GTIN-8 abrégés)"), (977, 977, "ISSN (presse, périodiques)"),
        (978, 979, "ISBN (livres) / ISMN (partitions)"), (980, 980, "bons de remboursement"),
        (981, 984, "coupons"), (990, 999, "coupons"),
    ];

    /// <summary>
    /// Organisation d'attribution d'un GTIN-13 (ou des 13 derniers chiffres d'un GTIN-14).
    /// </summary>
    public static string Lookup(ReadOnlySpan<char> gtin13)
    {
        if (gtin13.Length < 3 || !int.TryParse(gtin13[..3], out var p))
        {
            return "préfixe illisible";
        }

        foreach (var (from, to, label) in Ranges)
        {
            if (p >= from && p <= to)
            {
                return label;
            }
        }

        return "préfixe non attribué";
    }
}

/// <summary>Codes pays ISO 3166 numériques utilisés par les AI 422 à 426.</summary>
public static class Countries
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["004"] = "Afghanistan", ["008"] = "Albanie", ["012"] = "Algérie", ["032"] = "Argentine", ["036"] = "Australie",
        ["040"] = "Autriche", ["056"] = "Belgique", ["076"] = "Brésil", ["100"] = "Bulgarie", ["124"] = "Canada",
        ["152"] = "Chili", ["156"] = "Chine", ["170"] = "Colombie", ["191"] = "Croatie", ["196"] = "Chypre",
        ["203"] = "Tchéquie", ["208"] = "Danemark", ["233"] = "Estonie", ["246"] = "Finlande", ["250"] = "France",
        ["276"] = "Allemagne", ["300"] = "Grèce", ["344"] = "Hong Kong", ["348"] = "Hongrie", ["352"] = "Islande",
        ["356"] = "Inde", ["360"] = "Indonésie", ["372"] = "Irlande", ["376"] = "Israël", ["380"] = "Italie",
        ["392"] = "Japon", ["410"] = "Corée du Sud", ["428"] = "Lettonie", ["440"] = "Lituanie", ["442"] = "Luxembourg",
        ["458"] = "Malaisie", ["470"] = "Malte", ["484"] = "Mexique", ["504"] = "Maroc", ["528"] = "Pays-Bas",
        ["554"] = "Nouvelle-Zélande", ["578"] = "Norvège", ["604"] = "Pérou", ["608"] = "Philippines", ["616"] = "Pologne",
        ["620"] = "Portugal", ["642"] = "Roumanie", ["643"] = "Russie", ["682"] = "Arabie saoudite", ["702"] = "Singapour",
        ["703"] = "Slovaquie", ["704"] = "Vietnam", ["705"] = "Slovénie", ["710"] = "Afrique du Sud", ["724"] = "Espagne",
        ["752"] = "Suède", ["756"] = "Suisse", ["764"] = "Thaïlande", ["784"] = "Émirats arabes unis", ["788"] = "Tunisie",
        ["792"] = "Turquie", ["804"] = "Ukraine", ["818"] = "Égypte", ["826"] = "Royaume-Uni", ["840"] = "États-Unis",
        ["999"] = "pays non précisé",
    };

    /// <summary>Nom du pays, ou « code pays NNN » s'il est inconnu.</summary>
    public static string Name(string code) => Names.TryGetValue(code, out var n) ? n : $"code pays {code}";
}
