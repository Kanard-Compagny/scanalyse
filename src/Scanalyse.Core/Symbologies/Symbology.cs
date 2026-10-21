namespace Scanalyse.Core.Symbologies;

/// <summary>Famille de codes-barres (symbologie).</summary>
public enum Symbology
{
    Unknown = 0,

    // Matriciels (2D)
    QrCode,
    MicroQrCode,
    RmqrCode,
    DataMatrix,
    Aztec,
    Pdf417,
    MicroPdf417,
    MaxiCode,

    // Distribution
    Ean13,
    Ean8,
    UpcA,
    UpcE,
    Isbn,
    DataBar,
    DataBarExpanded,
    DataBarLimited,

    // Industriels
    Code128,
    Code39,
    Code93,
    Codabar,
    Itf,
    Itf14,
    Code32,
    Pzn,
}

/// <summary>Grande catégorie d'usage d'une symbologie.</summary>
public enum SymbologyFamily
{
    Matrix,
    Retail,
    Industrial,
    Stacked,
}

/// <summary>Fiche descriptive d'une symbologie.</summary>
/// <param name="Symbology">La symbologie décrite.</param>
/// <param name="Name">Nom usuel.</param>
/// <param name="Family">Catégorie.</param>
/// <param name="CharacterSet">Caractères qu'elle peut encoder.</param>
/// <param name="Capacity">Capacité maximale indicative.</param>
/// <param name="TypicalUses">Usages typiques.</param>
/// <param name="Standard">Norme de référence.</param>
/// <param name="SupportsGs1">Peut porter des données GS1 (avec FNC1).</param>
/// <param name="QuietZone">Zone de silence minimale (marge blanche).</param>
public sealed record SymbologyInfo(
    Symbology Symbology,
    string Name,
    SymbologyFamily Family,
    string CharacterSet,
    string Capacity,
    string TypicalUses,
    string Standard,
    bool SupportsGs1,
    string QuietZone);

/// <summary>Référentiel des symbologies prises en charge.</summary>
public static class Symbologies
{
    private static readonly Dictionary<Symbology, SymbologyInfo> Infos = new SymbologyInfo[]
    {
        new(Symbology.QrCode, "QR Code", SymbologyFamily.Matrix, "Numérique, alphanumérique, octets, kanji", "7 089 chiffres ou 2 953 octets (version 40, correction L)", "Liens web, Wi-Fi, paiement, GS1 Digital Link, traçabilité", "ISO/IEC 18004", true, "4 modules"),
        new(Symbology.MicroQrCode, "Micro QR Code", SymbologyFamily.Matrix, "Numérique, alphanumérique, octets", "35 chiffres (M4)", "Marquage de petites pièces", "ISO/IEC 18004", false, "2 modules"),
        new(Symbology.RmqrCode, "rMQR (QR rectangulaire)", SymbologyFamily.Matrix, "Numérique, alphanumérique, octets", "361 chiffres", "Surfaces étroites (bords de cartons, tubes)", "ISO/IEC 23941", true, "2 modules"),
        new(Symbology.DataMatrix, "Data Matrix (ECC 200)", SymbologyFamily.Matrix, "ASCII complet, octets", "3 116 chiffres ou 1 556 octets", "Pharmacie (sérialisation), industrie, marquage direct, santé (UDI)", "ISO/IEC 16022", true, "1 module"),
        new(Symbology.Aztec, "Aztec", SymbologyFamily.Matrix, "ASCII complet, octets", "3 832 chiffres ou 1 914 octets", "Billets de transport, cartes d'embarquement", "ISO/IEC 24778", true, "aucune requise"),
        new(Symbology.Pdf417, "PDF417", SymbologyFamily.Stacked, "Texte, numérique, octets", "1 850 caractères texte ou 1 108 octets", "Permis de conduire, cartes d'embarquement, profils de configuration (StageNow)", "ISO/IEC 15438", false, "2 modules"),
        new(Symbology.MicroPdf417, "MicroPDF417", SymbologyFamily.Stacked, "Texte, numérique, octets", "366 chiffres", "Petites étiquettes", "ISO/IEC 24728", false, "1 module"),
        new(Symbology.MaxiCode, "MaxiCode", SymbologyFamily.Matrix, "ASCII", "93 caractères", "Tri de colis (UPS)", "ISO/IEC 16023", false, "1 module"),
        new(Symbology.Ean13, "EAN-13", SymbologyFamily.Retail, "Chiffres", "13 chiffres (clé incluse)", "Articles en point de vente", "ISO/IEC 15420 · GS1", false, "11 modules à gauche, 7 à droite"),
        new(Symbology.Ean8, "EAN-8", SymbologyFamily.Retail, "Chiffres", "8 chiffres (clé incluse)", "Petits articles en point de vente", "ISO/IEC 15420 · GS1", false, "7 modules de chaque côté"),
        new(Symbology.UpcA, "UPC-A", SymbologyFamily.Retail, "Chiffres", "12 chiffres (clé incluse)", "Articles en point de vente (Amérique du Nord)", "ISO/IEC 15420 · GS1", false, "9 modules de chaque côté"),
        new(Symbology.UpcE, "UPC-E", SymbologyFamily.Retail, "Chiffres", "8 chiffres (forme compressée d'un UPC-A)", "Petits articles (Amérique du Nord)", "ISO/IEC 15420 · GS1", false, "9 modules à gauche, 7 à droite"),
        new(Symbology.Isbn, "EAN-13 (ISBN)", SymbologyFamily.Retail, "Chiffres", "13 chiffres", "Livres", "ISO 2108", false, "11 modules à gauche, 7 à droite"),
        new(Symbology.DataBar, "GS1 DataBar", SymbologyFamily.Retail, "Chiffres", "GTIN-14", "Fruits et légumes, petits articles", "ISO/IEC 24724", true, "aucune requise"),
        new(Symbology.DataBarExpanded, "GS1 DataBar Expanded", SymbologyFamily.Retail, "Chaîne GS1", "74 chiffres ou 41 caractères", "Produits frais à poids variable, coupons", "ISO/IEC 24724", true, "aucune requise"),
        new(Symbology.DataBarLimited, "GS1 DataBar Limited", SymbologyFamily.Retail, "Chiffres", "GTIN-14 (indicateur 0 ou 1)", "Petits articles de santé", "ISO/IEC 24724", true, "aucune requise"),
        new(Symbology.Code128, "Code 128", SymbologyFamily.Industrial, "ASCII complet (128 caractères)", "Variable, ~48 caractères en pratique", "Logistique, colis (GS1-128), étiquettes internes", "ISO/IEC 15417", true, "10 modules"),
        new(Symbology.Code39, "Code 39", SymbologyFamily.Industrial, "Majuscules, chiffres, - . espace $ / + %", "Variable, ~25 caractères en pratique", "Industrie, automobile (Odette), défense, badges", "ISO/IEC 16388", false, "10 modules"),
        new(Symbology.Code93, "Code 93", SymbologyFamily.Industrial, "ASCII complet", "Variable", "Industrie, postes canadiennes", "AIM USS-93", false, "10 modules"),
        new(Symbology.Codabar, "Codabar", SymbologyFamily.Industrial, "Chiffres, - $ : / . +", "Variable", "Bibliothèques, banques du sang, colis anciens", "AIM USS-Codabar", false, "10 modules"),
        new(Symbology.Itf, "ITF (2 parmi 5 entrelacé)", SymbologyFamily.Industrial, "Chiffres (nombre pair)", "Variable, pair", "Cartons, logistique, entrepôts", "ISO/IEC 16390", false, "10 modules"),
        new(Symbology.Itf14, "ITF-14", SymbologyFamily.Industrial, "14 chiffres", "GTIN-14", "Cartons d'expédition (unités logistiques)", "ISO/IEC 16390 · GS1", false, "10 modules (barre de protection recommandée)"),
        new(Symbology.Code32, "Code 32 (pharmacie italienne)", SymbologyFamily.Industrial, "Chiffres", "9 chiffres", "Médicaments en Italie", "Ministère de la santé italien", false, "10 modules"),
        new(Symbology.Pzn, "PZN (pharmacie allemande)", SymbologyFamily.Industrial, "Chiffres", "8 chiffres", "Médicaments en Allemagne", "IFA", false, "10 modules"),
    }.ToDictionary(i => i.Symbology);

    /// <summary>Fiche descriptive d'une symbologie, ou <c>null</c> si inconnue.</summary>
    public static SymbologyInfo? Describe(Symbology symbology) =>
        Infos.TryGetValue(symbology, out var info) ? info : null;

    /// <summary>Toutes les fiches connues.</summary>
    public static IReadOnlyCollection<SymbologyInfo> All => Infos.Values;

    /// <summary>Indique si la symbologie est matricielle (2D) ou empilée.</summary>
    public static bool IsTwoDimensional(Symbology symbology) =>
        Describe(symbology)?.Family is SymbologyFamily.Matrix or SymbologyFamily.Stacked;
}
