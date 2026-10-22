namespace Scanalyse.Core.Scanners;

/// <summary>
/// Code 128 (jeu B) avec, au besoin, le caractère FNC3 en tête : c'est ainsi que sont faits les codes de programmation
/// Honeywell, que les générateurs courants ne savent pas produire.
/// </summary>
public static class Code128
{
    // Largeurs barre / espace / barre / espace / barre / espace des valeurs 0 à 105, puis l'arrêt (106, sept éléments).
    private static readonly string[] Patterns =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
    ];

    private const int StartB = 104;
    private const int Fnc3 = 96;
    private const int Stop = 106;

    /// <summary>Valeurs des symboles (départ, données, clé, arrêt).</summary>
    /// <param name="text">Texte ASCII imprimable (espace à ~).</param>
    /// <param name="fnc3">Ajouter FNC3 en tête (code de programmation de lecteur).</param>
    public static IReadOnlyList<int> Symbols(string text, bool fnc3)
    {
        ArgumentNullException.ThrowIfNull(text);
        var values = new List<int> { StartB };
        if (fnc3)
        {
            values.Add(Fnc3);
        }

        foreach (var c in text)
        {
            if (c is < ' ' or > '~')
            {
                throw new ArgumentException($"Caractère non pris en charge en Code 128 B : U+{(int)c:X4}.", nameof(text));
            }

            values.Add(c - ' ');
        }

        var sum = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            sum += values[i] * i;
        }

        values.Add(sum % 103);
        values.Add(Stop);
        return values;
    }

    /// <summary>Largeurs successives (barre, espace, barre…) en modules, sans zones de silence.</summary>
    /// <param name="text">Texte ASCII imprimable.</param>
    /// <param name="fnc3">Ajouter FNC3 en tête.</param>
    public static IReadOnlyList<int> Widths(string text, bool fnc3) =>
        [.. Symbols(text, fnc3).SelectMany(v => Patterns[v].Select(ch => ch - '0'))];

    /// <summary>Le même code en SVG (une unité par module, zones de silence de 10 modules).</summary>
    /// <param name="text">Texte ASCII imprimable.</param>
    /// <param name="fnc3">Ajouter FNC3 en tête.</param>
    /// <param name="height">Hauteur des barres en modules.</param>
    public static string Svg(string text, bool fnc3, int height = 50)
    {
        var widths = Widths(text, fnc3);
        var total = widths.Sum() + 20;
        var sb = new System.Text.StringBuilder();
        sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {total} {height}\" width=\"{total}\" height=\"{height}\" shape-rendering=\"crispEdges\"><rect width=\"{total}\" height=\"{height}\" fill=\"#fff\"/>");
        var x = 10;
        for (var i = 0; i < widths.Count; i++)
        {
            if (i % 2 == 0)
            {
                sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"<rect x=\"{x}\" width=\"{widths[i]}\" height=\"{height}\"/>");
            }

            x += widths[i];
        }

        return sb.Append("</svg>").ToString();
    }
}

/// <summary>Marque de lecteur pour laquelle des codes de configuration sont proposés.</summary>
public enum ScannerBrand
{
    Honeywell,
    Datalogic,
}

/// <summary>Code de configuration à scanner.</summary>
/// <param name="Brand">Marque.</param>
/// <param name="Group">Rubrique (« Clavier », « Suffixe »…).</param>
/// <param name="Title">Effet du code.</param>
/// <param name="Data">Contenu (sans FNC3).</param>
/// <param name="Fnc3">Code Honeywell : FNC3 en tête.</param>
/// <param name="Hint">Quand l'utiliser (écart vu au banc d'essai…).</param>
public sealed record ConfigCode(ScannerBrand Brand, string Group, string Title, string Data, bool Fnc3, string Hint);

/// <summary>
/// Codes de configuration documentés par les fabricants. Honeywell : commandes de menu (manuels Voyager / Xenon), en Code 128
/// avec FNC3, terminées par « . » (enregistrées). Datalogic : chaînes « $+ … $- » des guides QuickScan / Gryphon.
/// </summary>
public static class ScannerConfigCodes
{
    public static IReadOnlyList<ConfigCode> All { get; } =
    [
        new(ScannerBrand.Honeywell, "Réglages", "Revenir aux réglages par défaut", "DEFALT.", true,
            "À scanner d'abord quand la douchette a été modifiée et qu'on ne sait plus comment."),
        new(ScannerBrand.Honeywell, "Clavier", "Clavier français (AZERTY)", "KBDCTY3.", true,
            "Chiffres lus comme « &é\"'(… » ou lettres A/Q, Z/W inversées au banc d'essai."),
        new(ScannerBrand.Honeywell, "Clavier", "Clavier américain (QWERTY)", "KBDCTY0.", true,
            "Poste configuré en QWERTY US."),
        new(ScannerBrand.Honeywell, "Clavier", "Caractères de contrôle transmis (Ctrl + X, mode Windows)", "KBDCAS2.", true,
            "Séparateur GS1 (<GS>) perdu en émulation clavier : il est alors envoyé comme Ctrl + ]."),
        new(ScannerBrand.Honeywell, "Clavier", "Caractères de contrôle non transmis", "KBDCAS0.", true,
            "Réglage d'usine : les caractères invisibles ne sont pas envoyés."),
        new(ScannerBrand.Honeywell, "Suffixe", "Ajouter Entrée (CR) après chaque lecture", "VSUFCR.", true,
            "Le logiciel attend une validation après chaque code."),
        new(ScannerBrand.Honeywell, "Suffixe", "Supprimer tous les suffixes", "SUFCA2.", true,
            "Retour à la ligne ou caractère en trop après chaque lecture."),
        new(ScannerBrand.Honeywell, "Préfixe", "Supprimer tous les préfixes", "PRECA2.", true,
            "Caractères en trop avant les données (préfixe, identifiant de code)."),
        new(ScannerBrand.Honeywell, "GS1", "Transmettre les codes de fonction (FNC1 → GS)", "RMVFNC0.", true,
            "Séparateurs GS1 absents des données lues."),
        new(ScannerBrand.Honeywell, "GS1", "Activer la lecture GS1-128", "GS1ENA1.", true,
            "Les étiquettes logistiques GS1-128 ne sont pas lues."),
        new(ScannerBrand.Honeywell, "Symbologies", "Activer la lecture Code 128", "128ENA1.", true,
            "Les codes Code 128 ne sont pas lus."),
        new(ScannerBrand.Datalogic, "Réglages", "Revenir aux réglages par défaut", "$+$*", false,
            "À scanner seul : la douchette revient à sa configuration d'usine."),
        new(ScannerBrand.Datalogic, "Programmation", "Entrer en mode programmation", "$+", false,
            "Avant tout code de réglage Datalogic tiré du guide du modèle."),
        new(ScannerBrand.Datalogic, "Programmation", "Quitter en enregistrant", "$-", false,
            "Après les codes de réglage : la configuration est sauvegardée."),
    ];
}
