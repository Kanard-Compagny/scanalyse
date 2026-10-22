using System.Globalization;
using System.Text;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Printing;

/// <summary>Langage d'imprimante d'étiquettes.</summary>
public enum PrinterLanguage
{
    /// <summary>ZPL II : Zebra, et la plupart des imprimantes en émulation ZPL (Honeywell, SATO, TSC, Citizen…).</summary>
    Zpl,

    /// <summary>TSPL / TSPL2 : TSC, et de nombreuses imprimantes d'entrée de gamme.</summary>
    Tspl,

    /// <summary>EPL2 : anciennes Zebra (LP/TLP 2844…) ; codes 1D uniquement ici.</summary>
    Epl2,

    /// <summary>DPL : Honeywell / Datamax (séries I, H, M, E, PD, PM…).</summary>
    Dpl,
}

/// <summary>Symbologie à imprimer.</summary>
public enum LabelSymbology
{
    Code128,
    Gs1_128,
    Ean13,
    Ean8,
    UpcA,
    Code39,
    Itf,
    QrCode,
    DataMatrix,
    Gs1DataMatrix,
    Pdf417,
    Aztec,
}

/// <summary>Étiquette à générer : un code, et un texte libre facultatif.</summary>
/// <param name="Data">Contenu du code, brut (caractères GS réels) ou en notation lisible (&lt;GS&gt;, « (01)… » pour GS1).</param>
/// <param name="Symbology">Symbologie.</param>
/// <param name="WidthMm">Largeur de l'étiquette.</param>
/// <param name="HeightMm">Hauteur de l'étiquette.</param>
/// <param name="Dpi">Résolution de la tête (203, 300, 600).</param>
/// <param name="XMm">Position horizontale du code depuis le bord gauche.</param>
/// <param name="YMm">Position verticale du code depuis le haut.</param>
/// <param name="ModuleDots">Largeur d'un module (barre la plus fine, carré 2D) en points.</param>
/// <param name="HeightMmBars">Hauteur des barres (codes 1D) ou des lignes (PDF417).</param>
/// <param name="HumanReadable">Texte lisible sous le code (codes 1D).</param>
/// <param name="Rotation">0, 90, 180 ou 270 degrés.</param>
/// <param name="Copies">Nombre d'exemplaires.</param>
/// <param name="Caption">Texte libre imprimé sous le code (facultatif).</param>
/// <param name="CaptionYMm">Position verticale du texte libre ; par défaut sous les barres.</param>
public sealed record LabelJob(
    string Data,
    LabelSymbology Symbology,
    double WidthMm = 100,
    double HeightMm = 50,
    int Dpi = 203,
    double XMm = 5,
    double YMm = 5,
    int ModuleDots = 2,
    double HeightMmBars = 15,
    bool HumanReadable = true,
    int Rotation = 0,
    int Copies = 1,
    string? Caption = null,
    double? CaptionYMm = null);

/// <summary>Code imprimante généré.</summary>
/// <param name="Code">Programme à envoyer à l'imprimante.</param>
/// <param name="Supported">La combinaison langage / symbologie est prise en charge.</param>
/// <param name="Warnings">Points d'attention (clé recalculée, caractères échappés…).</param>
public sealed record PrinterOutput(string Code, bool Supported, IReadOnlyList<string> Warnings);

/// <summary>
/// Génère le programme d'une étiquette simple (un code + un texte) en ZPL II, TSPL ou EPL2.
/// Les commandes sont celles documentées par les fabricants ; le résultat reste à valider sur l'imprimante cible
/// (firmware, émulation, réglages de média).
/// </summary>
public static class LabelCode
{
    /// <summary>Extension de fichier usuelle.</summary>
    public static string Extension(PrinterLanguage language) => language switch
    {
        PrinterLanguage.Zpl => ".zpl",
        PrinterLanguage.Tspl => ".prn",
        PrinterLanguage.Dpl => ".dpl",
        _ => ".epl",
    };

    public static PrinterOutput Generate(PrinterLanguage language, LabelJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var warnings = new List<string>();
        var data = Prepare(job, warnings, out var error);
        if (error is not null)
        {
            return new PrinterOutput(string.Empty, false, [error]);
        }

        return language switch
        {
            PrinterLanguage.Zpl => Zpl(job, data, warnings),
            PrinterLanguage.Tspl => Tspl(job, data, warnings),
            PrinterLanguage.Dpl => Dpl(job, data, warnings),
            _ => Epl(job, data, warnings),
        };
    }

    /// <summary>Points d'imprimante pour une distance en millimètres.</summary>
    public static int Dots(double mm, int dpi) => (int)Math.Round(mm * dpi / 25.4);

    // ------------------------------------------------------------------ Préparation des données

    private sealed record Prepared(string Raw, string? Gs1Readable, IReadOnlyList<Gs1Element>? Gs1);

    private static Prepared Prepare(LabelJob job, List<string> warnings, out string? error)
    {
        error = null;
        var raw = ControlCharacters.FromVisualized(job.Data).TrimEnd('\r', '\n');
        if (raw.Length == 0)
        {
            error = "Le contenu du code est vide.";
            return new Prepared(string.Empty, null, null);
        }

        switch (job.Symbology)
        {
            case LabelSymbology.Gs1_128 or LabelSymbology.Gs1DataMatrix:
            {
                var msg = Gs1Parser.Parse(raw.StartsWith(']') && raw.Length > 3 ? raw[3..] : raw);
                if (msg is null || msg.Elements.Count == 0)
                {
                    error = "Contenu GS1 attendu : « (01)03453120000011(10)LOT42 » ou la forme transmise avec <GS>.";
                    return new Prepared(raw, null, null);
                }

                foreach (var e in msg.Elements.Where(e => !e.IsValid))
                {
                    warnings.Add($"AI {e.Ai} : {string.Join(" ; ", e.Issues)}");
                }

                var readable = string.Concat(msg.Elements.Select(e => $"({e.Ai}){e.Value}"));
                return new Prepared(msg.ToTransmitted(), readable, msg.Elements);
            }

            case LabelSymbology.Ean13 or LabelSymbology.Ean8 or LabelSymbology.UpcA:
            {
                var body = job.Symbology switch { LabelSymbology.Ean13 => 12, LabelSymbology.Ean8 => 7, _ => 11 };
                var name = job.Symbology switch { LabelSymbology.Ean13 => "EAN-13", LabelSymbology.Ean8 => "EAN-8", _ => "UPC-A" };
                if (!raw.All(char.IsAsciiDigit) || (raw.Length != body && raw.Length != body + 1))
                {
                    error = $"{name} : {body} chiffres attendus (ou {body + 1} avec la clé), {raw.Length} caractères reçus.";
                    return new Prepared(raw, null, null);
                }

                if (raw.Length == body + 1 && !CheckDigit.IsValid(raw))
                {
                    warnings.Add($"Clé de contrôle fausse ({raw[^1]}) : l'imprimante calculera la bonne ({CheckDigit.Compute(raw.AsSpan(0, body))}).");
                }

                // Les imprimantes calculent elles-mêmes la clé : on n'envoie que le corps du numéro.
                return new Prepared(raw[..body], null, null);
            }

            case LabelSymbology.Itf:
                if (!raw.All(char.IsAsciiDigit) || raw.Length % 2 != 0)
                {
                    error = "ITF (2 parmi 5 entrelacé) : un nombre pair de chiffres est attendu.";
                }

                return new Prepared(raw, null, null);

            case LabelSymbology.Code39:
                var upper = raw.ToUpperInvariant();
                if (upper != raw)
                {
                    warnings.Add("Code 39 standard : minuscules converties en majuscules.");
                }

                if (upper.Any(c => !(char.IsAsciiLetterOrDigit(c) || "-. $/+%".Contains(c))))
                {
                    error = "Code 39 : seuls A-Z, 0-9 et - . espace $ / + % sont permis.";
                }

                return new Prepared(upper, null, null);

            default:
                return new Prepared(raw, null, null);
        }
    }

    private static bool Is2D(LabelSymbology s) => s is LabelSymbology.QrCode or LabelSymbology.DataMatrix or LabelSymbology.Gs1DataMatrix
        or LabelSymbology.Pdf417 or LabelSymbology.Aztec;

    // ------------------------------------------------------------------ ZPL II

    private static PrinterOutput Zpl(LabelJob job, Prepared p, List<string> warnings)
    {
        var d = job.Dpi;
        var x = Dots(job.XMm, d);
        var y = Dots(job.YMm, d);
        var h = Math.Max(10, Dots(job.HeightMmBars, d));
        var m = Math.Clamp(job.ModuleDots, 1, 10);
        var o = job.Rotation switch { 90 => "R", 180 => "I", 270 => "B", _ => "N" };
        var hri = job.HumanReadable ? "Y" : "N";
        var sb = new StringBuilder();
        sb.Append("^XA\r\n^CI28\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"^PW{Dots(job.WidthMm, d)}\r\n^LL{Dots(job.HeightMm, d)}\r\n^LH0,0\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"^FO{x},{y}\r\n^BY{m},3,{h}\r\n");
        string fieldData;
        switch (job.Symbology)
        {
            case LabelSymbology.Code128:
                sb.Append(CultureInfo.InvariantCulture, $"^BC{o},{h},{hri},N,N,A\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.Gs1_128:
                // Mode D (UCC/EAN) : FNC1 et séparateurs ajoutés par l'imprimante, parenthèses imprimées seulement dans le texte lisible.
                sb.Append(CultureInfo.InvariantCulture, $"^BC{o},{h},{hri},N,N,D\r\n");
                fieldData = p.Gs1Readable!;
                break;
            case LabelSymbology.Ean13:
                sb.Append(CultureInfo.InvariantCulture, $"^BE{o},{h},{hri},N\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.Ean8:
                sb.Append(CultureInfo.InvariantCulture, $"^B8{o},{h},{hri},N\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.UpcA:
                sb.Append(CultureInfo.InvariantCulture, $"^BU{o},{h},{hri},N,Y\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.Code39:
                sb.Append(CultureInfo.InvariantCulture, $"^B3{o},N,{h},{hri},N\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.Itf:
                sb.Append(CultureInfo.InvariantCulture, $"^B2{o},{h},{hri},N,N\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.QrCode:
                if (o != "N")
                {
                    warnings.Add("ZPL : le QR Code (^BQ) ne se tourne pas ; il est imprimé à l'endroit.");
                }

                sb.Append(CultureInfo.InvariantCulture, $"^BQN,2,{m}\r\n");
                fieldData = "MA," + p.Raw; // correction M, saisie automatique
                break;
            case LabelSymbology.DataMatrix:
                sb.Append(CultureInfo.InvariantCulture, $"^BX{o},{m},200\r\n");
                fieldData = p.Raw;
                break;
            case LabelSymbology.Gs1DataMatrix:
                // « _1 » = FNC1 : en tête (GS1), puis après chaque champ de longueur variable.
                if (p.Raw.Contains('_', StringComparison.Ordinal))
                {
                    warnings.Add("Le contenu contient « _ », caractère d'échappement choisi pour FNC1 : vérifie le code imprimé.");
                }

                sb.Append(CultureInfo.InvariantCulture, $"^BX{o},{m},200,,,,_\r\n");
                fieldData = "_1" + p.Raw.Replace(ControlCharacters.GroupSeparator.ToString(), "_1", StringComparison.Ordinal);
                break;
            case LabelSymbology.Pdf417:
                sb.Append(CultureInfo.InvariantCulture, $"^B7{o},{Math.Max(2, m * 3)},5,,,N\r\n");
                fieldData = p.Raw;
                break;
            default:
                sb.Append(CultureInfo.InvariantCulture, $"^B0{o},{Math.Max(2, m)},N,0,N,1\r\n");
                fieldData = p.Raw;
                break;
        }

        sb.Append("^FH\\^FD").Append(ZplEscape(fieldData)).Append("^FS\r\n");
        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            var fh = Dots(3.5, d);
            sb.Append(CultureInfo.InvariantCulture, $"^FO{x},{CaptionY(job, d)}^A0N,{fh},{fh}^FH\\^FD").Append(ZplEscape(job.Caption)).Append("^FS\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"^PQ{Math.Max(1, job.Copies)}\r\n^XZ\r\n");
        if (p.Raw.Any(c => c < ' '))
        {
            warnings.Add("Caractères de contrôle encodés en hexadécimal (^FH) : ils sont bien transmis au code.");
        }

        return new PrinterOutput(sb.ToString(), true, warnings);
    }

    /// <summary>^FH\ : caractères de contrôle et caractères réservés (^ ~ \) en hexadécimal.</summary>
    private static string ZplEscape(string s)
    {
        var sb = new StringBuilder(s.Length + 8);
        foreach (var c in s)
        {
            if (c < ' ' || c is '^' or '~' or '\\' or '\u007F')
            {
                sb.Append('\\').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    private static int CaptionY(LabelJob job, int dpi) => job.CaptionYMm is { } cy ? Dots(cy, dpi) : Dots(job.YMm + job.HeightMmBars + (job.HumanReadable && !Is2D(job.Symbology) ? 5 : 3), dpi);

    // ------------------------------------------------------------------ TSPL

    private static PrinterOutput Tspl(LabelJob job, Prepared p, List<string> warnings)
    {
        var d = job.Dpi;
        var x = Dots(job.XMm, d);
        var y = Dots(job.YMm, d);
        var h = Math.Max(10, Dots(job.HeightMmBars, d));
        var m = Math.Clamp(job.ModuleDots, 1, 10);
        var rot = job.Rotation is 90 or 180 or 270 ? job.Rotation : 0;
        if (job.Symbology is LabelSymbology.Gs1_128 or LabelSymbology.Gs1DataMatrix or LabelSymbology.Aztec)
        {
            return new PrinterOutput(string.Empty, false,
                [$"{Name(job.Symbology)} n'est pas généré en TSPL : la gestion du FNC1 et des codes Aztec varie selon les firmwares. Utilise le ZPL (la plupart des TSC le comprennent en émulation)."]);
        }

        if (p.Raw.Any(c => c < ' '))
        {
            return new PrinterOutput(string.Empty, false, ["TSPL : les caractères de contrôle (<GS>, <RS>…) ne sont pas transmis de façon fiable. Utilise le ZPL pour ce contenu."]);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SIZE {Mm(job.WidthMm)} mm,{Mm(job.HeightMm)} mm\r\nGAP 2 mm,0 mm\r\nDIRECTION 1\r\nREFERENCE 0,0\r\nCODEPAGE UTF-8\r\nCLS\r\n");
        var q = TsplQuote(p.Raw);
        var human = job.HumanReadable ? 1 : 0;
        var type = job.Symbology switch
        {
            LabelSymbology.Code128 => "128",
            LabelSymbology.Ean13 => "EAN13",
            LabelSymbology.Ean8 => "EAN8",
            LabelSymbology.UpcA => "UPCA",
            LabelSymbology.Code39 => "39",
            LabelSymbology.Itf => "25",
            _ => null,
        };
        if (type is not null)
        {
            var wide = job.Symbology is LabelSymbology.Code39 or LabelSymbology.Itf ? m * 2 + m / 2 : m;
            sb.Append(CultureInfo.InvariantCulture, $"BARCODE {x},{y},\"{type}\",{h},{human},{rot},{m},{wide},{q}\r\n");
        }
        else if (job.Symbology == LabelSymbology.QrCode)
        {
            sb.Append(CultureInfo.InvariantCulture, $"QRCODE {x},{y},M,{m},A,{rot},{q}\r\n");
        }
        else if (job.Symbology == LabelSymbology.DataMatrix)
        {
            var area = Dots(Math.Min(job.WidthMm - job.XMm, job.HeightMm - job.YMm), d);
            sb.Append(CultureInfo.InvariantCulture, $"DMATRIX {x},{y},{area},{area},x{m},{q}\r\n");
        }
        else
        {
            var w = Dots(job.WidthMm - job.XMm * 2, d);
            sb.Append(CultureInfo.InvariantCulture, $"PDF417 {x},{y},{w},{h},{rot},{q}\r\n");
        }

        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            sb.Append(CultureInfo.InvariantCulture, $"TEXT {x},{CaptionY(job, d)},\"3\",0,1,1,{TsplQuote(job.Caption)}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"PRINT 1,{Math.Max(1, job.Copies)}\r\n");
        return new PrinterOutput(sb.ToString(), true, warnings);
    }

    /// <summary>Chaîne TSPL entre guillemets ; un guillemet s'écrit \["].</summary>
    private static string TsplQuote(string s) => "\"" + s.Replace("\"", "\\[\"]", StringComparison.Ordinal) + "\"";

    private static string Mm(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ EPL2

    private static PrinterOutput Epl(LabelJob job, Prepared p, List<string> warnings)
    {
        var type = job.Symbology switch
        {
            LabelSymbology.Code128 => "1",
            LabelSymbology.Ean13 => "E30",
            LabelSymbology.Ean8 => "E80",
            LabelSymbology.UpcA => "UA0",
            LabelSymbology.Code39 => "3",
            LabelSymbology.Itf => "2",
            _ => null,
        };
        if (type is null)
        {
            return new PrinterOutput(string.Empty, false,
                [$"{Name(job.Symbology)} n'est pas généré en EPL2 (langage ancien, 2D et GS1 selon le modèle). Utilise le ZPL : les Zebra récentes ne parlent plus que ZPL."]);
        }

        if (p.Raw.Any(c => c < ' '))
        {
            return new PrinterOutput(string.Empty, false, ["EPL2 : les caractères de contrôle ne sont pas pris en charge ici. Utilise le ZPL."]);
        }

        var d = job.Dpi;
        var m = Math.Clamp(job.ModuleDots, 1, 10);
        var wide = job.Symbology is LabelSymbology.Code39 or LabelSymbology.Itf ? m * 2 + m / 2 : m * 2;
        var rot = job.Rotation switch { 90 => 1, 180 => 2, 270 => 3, _ => 0 };
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"\r\nN\r\nq{Dots(job.WidthMm, d)}\r\nQ{Dots(job.HeightMm, d)},24\r\n");
        sb.Append(CultureInfo.InvariantCulture, $"B{Dots(job.XMm, d)},{Dots(job.YMm, d)},{rot},{type},{m},{wide},{Math.Max(10, Dots(job.HeightMmBars, d))},{(job.HumanReadable ? "B" : "N")},{EplQuote(p.Raw)}\r\n");
        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            sb.Append(CultureInfo.InvariantCulture, $"A{Dots(job.XMm, d)},{CaptionY(job, d)},0,3,1,1,N,{EplQuote(job.Caption)}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"P{Math.Max(1, job.Copies)}\r\n");
        return new PrinterOutput(sb.ToString(), true, warnings);
    }

    private static string EplQuote(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    // ------------------------------------------------------------------ DPL (Honeywell / Datamax)

    /// <summary>
    /// DPL en unités métriques (0,1 mm). Origine en bas à gauche de l'étiquette : la ligne (row) se compte depuis le bas.
    /// Enregistrement : rotation, identifiant (police ou code), largeurs, hauteur, ligne, colonne, données.
    /// </summary>
    private static PrinterOutput Dpl(LabelJob job, Prepared p, List<string> warnings)
    {
        if (job.Symbology is LabelSymbology.Gs1_128 or LabelSymbology.Gs1DataMatrix or LabelSymbology.Pdf417 or LabelSymbology.Aztec)
        {
            return new PrinterOutput(string.Empty, false,
                [$"{Name(job.Symbology)} n'est pas généré en DPL (FNC1 et paramètres propres à chaque firmware). Utilise le ZPL : les Honeywell PM/PD/PX le comprennent en émulation."]);
        }

        if (p.Raw.Any(c => c < ' '))
        {
            return new PrinterOutput(string.Empty, false, ["DPL : les caractères de contrôle (<GS>, <RS>…) ne sont pas transmis de façon fiable. Utilise le ZPL pour ce contenu."]);
        }

        static string Tenth(double mm, int digits) => Math.Clamp((int)Math.Round(mm * 10), 0, (int)Math.Pow(10, digits) - 1).ToString("D" + digits, CultureInfo.InvariantCulture);
        static char Mult(int v) => v < 10 ? (char)('0' + v) : (char)('A' + v - 10);

        var m = Math.Clamp(job.ModuleDots, 1, 9);
        var moduleMm = m * 25.4 / job.Dpi;
        var rotation = job.Rotation switch { 90 => '2', 180 => '3', 270 => '4', _ => '1' };
        var hri = job.HumanReadable;
        string record;
        double heightMm;
        switch (job.Symbology)
        {
            case LabelSymbology.QrCode or LabelSymbology.DataMatrix:
            {
                // W1d = QR automatique (modèle 2, correction M) ; W1c = Data Matrix ECC 200. Taille d'un module en 0,1 mm.
                var cell = Math.Clamp((int)Math.Round(moduleMm * 10), 1, 35);
                var modules = job.Symbology == LabelSymbology.QrCode ? QrModules(p.Raw.Length) : DataMatrixModules(p.Raw.Length);
                heightMm = modules * cell / 10.0;
                var id = job.Symbology == LabelSymbology.QrCode ? "W1d" : "W1c";
                var row = Tenth(job.HeightMm - job.YMm - heightMm, 4);
                record = $"{rotation}{id}{Mult(cell)}{Mult(cell)}000{row}{Tenth(job.XMm, 4)}{p.Raw}";
                if (job.Symbology == LabelSymbology.QrCode)
                {
                    record += "\r"; // QR automatique : données terminées par deux retours chariot.
                }

                warnings.Add($"Taille du code estimée à {heightMm.ToString("0.#", CultureInfo.InvariantCulture)} mm pour le placer depuis le bas de l'étiquette (origine DPL).");
                break;
            }

            default:
            {
                var id = job.Symbology switch
                {
                    LabelSymbology.Code39 => 'A',
                    LabelSymbology.UpcA => 'B',
                    LabelSymbology.Itf => 'D',
                    LabelSymbology.Code128 => 'E',
                    LabelSymbology.Ean13 => 'F',
                    _ => 'G', // EAN-8
                };
                if (!hri)
                {
                    id = char.ToLowerInvariant(id);
                }

                var wide = job.Symbology is LabelSymbology.Code39 or LabelSymbology.Itf ? Math.Min(9, m * 3) : m;
                heightMm = Math.Min(99.9, job.HeightMmBars);
                var row = Tenth(job.HeightMm - job.YMm - heightMm - (hri ? 3 : 0), 4);
                record = $"{rotation}{id}{Mult(wide)}{Mult(m)}{Tenth(heightMm, 3)}{row}{Tenth(job.XMm, 4)}{p.Raw}";
                break;
            }
        }

        var sb = new StringBuilder();
        sb.Append("\u0002L\r\n");
        sb.Append("m\r\n");     // unités métriques : 0,1 mm
        sb.Append("D11\r\n");   // taille de point 1 × 1
        sb.Append(record).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(job.Caption))
        {
            // Police interne 2, taille 1 × 1 : texte libre sous le code.
            var captionTop = job.CaptionYMm ?? job.YMm + heightMm + (hri && !Is2D(job.Symbology) ? 5 : 3);
            sb.Append(CultureInfo.InvariantCulture, $"1211000{Tenth(job.HeightMm - captionTop - 3, 4)}{Tenth(job.XMm, 4)}{job.Caption.Trim()}\r\n");
        }

        sb.Append(CultureInfo.InvariantCulture, $"Q{Math.Clamp(job.Copies, 1, 9999):D4}\r\nE\r\n");
        if (job.Rotation != 0)
        {
            warnings.Add("DPL : la rotation tourne le code autour de son point d'ancrage ; vérifie sa position sur l'étiquette.");
        }

        warnings.Add("DPL généré d'après le manuel Honeywell / Datamax (unités 0,1 mm, origine en bas à gauche) : valide l'étiquette sur l'imprimante.");
        return new PrinterOutput(sb.ToString(), true, warnings);
    }

    /// <summary>Côté d'un QR Code (modèle 2, correction M, octets) selon la longueur des données.</summary>
    internal static int QrModules(int length)
    {
        int[] capacity = [14, 26, 42, 62, 84, 106, 122, 152, 180, 213, 251, 287, 331, 362, 412, 450, 504, 560, 624, 666];
        var version = Array.FindIndex(capacity, c => c >= length) + 1;
        return 17 + 4 * (version <= 0 ? 20 : version);
    }

    /// <summary>Côté d'un Data Matrix carré (ECC 200, caractères ASCII) selon la longueur des données.</summary>
    internal static int DataMatrixModules(int length)
    {
        (int Size, int Capacity)[] sizes = [(10, 3), (12, 5), (14, 8), (16, 12), (18, 18), (20, 22), (22, 30), (24, 36), (26, 44), (32, 62), (36, 86), (40, 114), (44, 144), (48, 174), (52, 204), (64, 280), (72, 368), (80, 456)];
        foreach (var (size, cap) in sizes)
        {
            if (cap >= length)
            {
                return size;
            }
        }

        return 88;
    }

    /// <summary>Nom affiché d'une symbologie.</summary>
    public static string Name(LabelSymbology s) => s switch
    {
        LabelSymbology.Code128 => "Code 128",
        LabelSymbology.Gs1_128 => "GS1-128",
        LabelSymbology.Ean13 => "EAN-13",
        LabelSymbology.Ean8 => "EAN-8",
        LabelSymbology.UpcA => "UPC-A",
        LabelSymbology.Code39 => "Code 39",
        LabelSymbology.Itf => "ITF",
        LabelSymbology.QrCode => "QR Code",
        LabelSymbology.DataMatrix => "Data Matrix",
        LabelSymbology.Gs1DataMatrix => "GS1 DataMatrix",
        LabelSymbology.Pdf417 => "PDF417",
        _ => "Aztec",
    };

    /// <summary>
    /// Symbologie la plus proche d'un format lu (« EAN-13 », « Data Matrix »…), en tenant compte d'un contenu GS1.
    /// </summary>
    public static LabelSymbology FromFormat(string? format, string content)
    {
        var f = (format ?? string.Empty).ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
        var raw = ControlCharacters.FromVisualized(content);
        var gs1 = raw.StartsWith('(') || (raw.Contains(ControlCharacters.GroupSeparator) && Gs1Parser.Parse(raw) is { Elements.Count: > 0 } m && m.IsValid);
        return f switch
        {
            _ when f.Contains("DATAMATRIX", StringComparison.Ordinal) => gs1 ? LabelSymbology.Gs1DataMatrix : LabelSymbology.DataMatrix,
            _ when f.Contains("QR", StringComparison.Ordinal) => LabelSymbology.QrCode,
            _ when f.Contains("PDF417", StringComparison.Ordinal) => LabelSymbology.Pdf417,
            _ when f.Contains("AZTEC", StringComparison.Ordinal) => LabelSymbology.Aztec,
            _ when f.Contains("EAN13", StringComparison.Ordinal) || f.Contains("ISBN", StringComparison.Ordinal) => LabelSymbology.Ean13,
            _ when f.Contains("EAN8", StringComparison.Ordinal) => LabelSymbology.Ean8,
            _ when f.Contains("UPCA", StringComparison.Ordinal) => LabelSymbology.UpcA,
            _ when f.Contains("CODE39", StringComparison.Ordinal) => LabelSymbology.Code39,
            _ when f.Contains("ITF", StringComparison.Ordinal) => LabelSymbology.Itf,
            _ when f.Contains("128", StringComparison.Ordinal) => gs1 ? LabelSymbology.Gs1_128 : LabelSymbology.Code128,
            _ => raw.Length > 40 || raw.Any(c => c < ' ') ? (gs1 ? LabelSymbology.Gs1DataMatrix : LabelSymbology.DataMatrix)
                : gs1 ? LabelSymbology.Gs1_128 : LabelSymbology.Code128,
        };
    }
}
