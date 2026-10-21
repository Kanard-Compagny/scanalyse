namespace Scanalyse.Core.Symbologies;

/// <summary>
/// Identifiant de symbologie AIM / ISO 15424 (« ]C1 », « ]d2 », « ]Q3 »…),
/// que la plupart des scanners peuvent transmettre en préfixe.
/// </summary>
/// <param name="Value">Identifiant complet, par exemple « ]C1 ».</param>
/// <param name="Symbology">Symbologie correspondante.</param>
/// <param name="Meaning">Signification de l'identifiant.</param>
/// <param name="IsGs1">Le code porte des données GS1 (FNC1 en première position).</param>
public sealed record SymbologyIdentifier(string Value, Symbology Symbology, string Meaning, bool IsGs1)
{
    private static readonly Dictionary<string, (Symbology Symbology, string Meaning, bool Gs1)> Known = new(StringComparer.Ordinal)
    {
        ["]Q0"] = (Symbology.QrCode, "QR Code modèle 1", false),
        ["]Q1"] = (Symbology.QrCode, "QR Code modèle 2", false),
        ["]Q2"] = (Symbology.QrCode, "QR Code modèle 2 avec ECI", false),
        ["]Q3"] = (Symbology.QrCode, "GS1 QR Code (FNC1 en 1re position)", true),
        ["]Q4"] = (Symbology.QrCode, "GS1 QR Code avec ECI", true),
        ["]Q5"] = (Symbology.QrCode, "QR Code avec FNC1 en 2e position (AIM)", false),
        ["]Q6"] = (Symbology.QrCode, "QR Code avec FNC1 en 2e position et ECI", false),
        ["]d0"] = (Symbology.DataMatrix, "Data Matrix ECC 000-140", false),
        ["]d1"] = (Symbology.DataMatrix, "Data Matrix ECC 200", false),
        ["]d2"] = (Symbology.DataMatrix, "GS1 DataMatrix (FNC1 en 1re position)", true),
        ["]d3"] = (Symbology.DataMatrix, "Data Matrix avec FNC1 en 2e position", false),
        ["]d4"] = (Symbology.DataMatrix, "Data Matrix avec ECI", false),
        ["]d5"] = (Symbology.DataMatrix, "GS1 DataMatrix avec ECI", true),
        ["]d6"] = (Symbology.DataMatrix, "Data Matrix avec FNC1 en 2e position et ECI", false),
        ["]C0"] = (Symbology.Code128, "Code 128 standard", false),
        ["]C1"] = (Symbology.Code128, "GS1-128 (FNC1 en 1re position)", true),
        ["]C2"] = (Symbology.Code128, "Code 128 avec FNC1 en 2e position", false),
        ["]C4"] = (Symbology.Code128, "Code 128 concaténé", false),
        ["]E0"] = (Symbology.Ean13, "EAN-13 ou UPC-A", false),
        ["]E1"] = (Symbology.Ean13, "EAN-13 + supplément 2 chiffres", false),
        ["]E2"] = (Symbology.Ean13, "EAN-13 + supplément 5 chiffres", false),
        ["]E3"] = (Symbology.Ean13, "EAN-13 avec supplément (transmission combinée)", false),
        ["]E4"] = (Symbology.Ean8, "EAN-8", false),
        ["]A0"] = (Symbology.Code39, "Code 39 sans contrôle", false),
        ["]A1"] = (Symbology.Code39, "Code 39 avec clé vérifiée et transmise", false),
        ["]A3"] = (Symbology.Code39, "Code 39 avec clé vérifiée, non transmise", false),
        ["]A4"] = (Symbology.Code39, "Code 39 ASCII étendu", false),
        ["]G0"] = (Symbology.Code93, "Code 93", false),
        ["]F0"] = (Symbology.Codabar, "Codabar", false),
        ["]I0"] = (Symbology.Itf, "ITF sans contrôle", false),
        ["]I1"] = (Symbology.Itf, "ITF avec clé vérifiée et transmise", false),
        ["]I3"] = (Symbology.Itf, "ITF avec clé vérifiée, non transmise", false),
        ["]e0"] = (Symbology.DataBar, "GS1 DataBar", true),
        ["]L0"] = (Symbology.Pdf417, "PDF417", false),
        ["]L1"] = (Symbology.Pdf417, "PDF417 (interprétation étendue)", false),
        ["]L2"] = (Symbology.Pdf417, "PDF417 (interprétation de base)", false),
        ["]z0"] = (Symbology.Aztec, "Aztec", false),
        ["]z1"] = (Symbology.Aztec, "GS1 Aztec (FNC1 en 1re position)", true),
        ["]z3"] = (Symbology.Aztec, "Aztec avec ECI", false),
        ["]U0"] = (Symbology.MaxiCode, "MaxiCode mode 4 ou 5", false),
        ["]U1"] = (Symbology.MaxiCode, "MaxiCode mode 2 ou 3", false),
    };

    /// <summary>
    /// Reconnaît un identifiant au début d'un texte. Renvoie <c>null</c> si le texte ne commence pas par « ]Xm ».
    /// </summary>
    public static SymbologyIdentifier? TryParsePrefix(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length < 3 || text[0] != ']')
        {
            return null;
        }

        var id = text[..3];
        if (Known.TryGetValue(id, out var k))
        {
            return new SymbologyIdentifier(id, k.Symbology, k.Meaning, k.Gs1);
        }

        return char.IsLetter(id[1]) && char.IsLetterOrDigit(id[2])
            ? new SymbologyIdentifier(id, Symbology.Unknown, "Identifiant de symbologie AIM non répertorié", false)
            : null;
    }

    /// <summary>Description d'un identifiant connu (« ]C1 » → « GS1-128… »).</summary>
    public static SymbologyIdentifier? Describe(string identifier) => TryParsePrefix(identifier ?? string.Empty);
}
