using System.Globalization;
using System.Text;
using Scanalyse.Core.Content;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Printing;

/// <summary>Sens d'un champ ZPL : N normal, R 90° horaire, I 180°, B 270° (lecture de bas en haut).</summary>
public enum ZplOrientation
{
    N,
    R,
    I,
    B,
}

/// <summary>Élément placé sur l'étiquette (coordonnées en points, origine en haut à gauche).</summary>
/// <param name="X">Position horizontale (^LH compris).</param>
/// <param name="Y">Position verticale (^LH compris).</param>
/// <param name="Typeset">Position ^FT (ligne de base / bas du champ) plutôt que ^FO (coin haut gauche).</param>
/// <param name="Orientation">Sens du champ.</param>
/// <param name="Line">Ligne du programme où le champ se termine (^FS).</param>
public abstract record ZplElement(int X, int Y, bool Typeset, ZplOrientation Orientation, int Line);

/// <summary>Texte.</summary>
/// <param name="X">Position horizontale.</param>
/// <param name="Y">Position verticale.</param>
/// <param name="Typeset">Position ^FT.</param>
/// <param name="Orientation">Sens.</param>
/// <param name="Line">Ligne du programme.</param>
/// <param name="Font">Police ZPL (« 0 » = police vectorielle, « A » à « Z » = polices bitmap).</param>
/// <param name="Height">Hauteur des caractères en points.</param>
/// <param name="Width">Largeur des caractères en points.</param>
/// <param name="Text">Texte tel que l'imprimante l'imprime (jeu de caractères appliqué).</param>
/// <param name="Reverse">Champ inversé (^FR).</param>
/// <param name="BlockWidth">Largeur du bloc de texte (^FB), 0 sans bloc.</param>
/// <param name="BlockAlign">Alignement du bloc : L, C, R ou J.</param>
public sealed record ZplText(int X, int Y, bool Typeset, ZplOrientation Orientation, int Line,
    string Font, int Height, int Width, string Text, bool Reverse, int BlockWidth = 0, char BlockAlign = 'L')
    : ZplElement(X, Y, Typeset, Orientation, Line);

/// <summary>Cadre, trait ou rectangle plein (^GB).</summary>
/// <param name="X">Position horizontale.</param>
/// <param name="Y">Position verticale.</param>
/// <param name="Typeset">Position ^FT.</param>
/// <param name="Line">Ligne du programme.</param>
/// <param name="Width">Largeur en points.</param>
/// <param name="Height">Hauteur en points.</param>
/// <param name="Thickness">Épaisseur du trait.</param>
/// <param name="White">Trait blanc (couleur W).</param>
/// <param name="Rounding">Arrondi des angles (0 à 8).</param>
/// <param name="Reverse">Champ inversé (^FR).</param>
public sealed record ZplBox(int X, int Y, bool Typeset, int Line, int Width, int Height, int Thickness, bool White, int Rounding, bool Reverse)
    : ZplElement(X, Y, Typeset, ZplOrientation.N, Line);

/// <summary>Code-barres.</summary>
/// <param name="X">Position horizontale.</param>
/// <param name="Y">Position verticale.</param>
/// <param name="Typeset">Position ^FT.</param>
/// <param name="Orientation">Sens.</param>
/// <param name="Line">Ligne du programme.</param>
/// <param name="Command">Commande ZPL (« ^BC »…).</param>
/// <param name="Symbology">Nom de la symbologie.</param>
/// <param name="Format">Format pour le dessin (« Code128 », « DataMatrix »…), vide si non dessinable.</param>
/// <param name="FieldData">Données du champ, telles qu'écrites dans le programme (^FH décodé).</param>
/// <param name="Content">Ce qu'une douchette lira (caractères GS réels pour GS1).</param>
/// <param name="Readable">Texte lisible imprimé (forme « (00)… » pour GS1).</param>
/// <param name="Gs1">Code GS1 (FNC1).</param>
/// <param name="ModuleWidth">Largeur d'un module en points (^BY ou grossissement 2D).</param>
/// <param name="Ratio">Rapport barres larges / étroites (^BY).</param>
/// <param name="Height">Hauteur des barres (1D) ou d'une ligne (PDF417), en points.</param>
/// <param name="Interpretation">Texte lisible imprimé.</param>
/// <param name="InterpretationAbove">Texte lisible au-dessus du code.</param>
/// <param name="Parameters">Réglages lus, pour l'affichage.</param>
/// <param name="Notes">Remarques sur ce code.</param>
public sealed record ZplBarcode(int X, int Y, bool Typeset, ZplOrientation Orientation, int Line,
    string Command, string Symbology, string Format, string FieldData, string Content, string Readable, bool Gs1,
    int ModuleWidth, double Ratio, int Height, bool Interpretation, bool InterpretationAbove,
    IReadOnlyList<(string Label, string Value)> Parameters, IReadOnlyList<Alert> Notes)
    : ZplElement(X, Y, Typeset, Orientation, Line);

/// <summary>Étiquette lue dans un programme ZPL.</summary>
/// <param name="WidthDots">Largeur (^PW), si le programme la fixe.</param>
/// <param name="LengthDots">Longueur (^LL), si le programme la fixe.</param>
/// <param name="Inverted">Impression tête-bêche (^POI).</param>
/// <param name="Copies">Exemplaires (^PQ).</param>
/// <param name="Utf8">Jeu de caractères UTF-8 (^CI28) ; sinon page de codes 850.</param>
/// <param name="Elements">Champs, dans l'ordre du programme.</param>
/// <param name="Issues">Constats sur le programme.</param>
/// <param name="Labels">Nombre d'étiquettes (^XA … ^XZ) dans le texte ; seule la première est lue.</param>
public sealed record ZplLabel(int? WidthDots, int? LengthDots, bool Inverted, int Copies, bool Utf8,
    IReadOnlyList<ZplElement> Elements, IReadOnlyList<Alert> Issues, int Labels)
{
    /// <summary>Codes-barres de l'étiquette.</summary>
    public IEnumerable<ZplBarcode> Barcodes => Elements.OfType<ZplBarcode>();

    /// <summary>Textes de l'étiquette.</summary>
    public IEnumerable<ZplText> Texts => Elements.OfType<ZplText>();
}

/// <summary>
/// Lit un programme ZPL II (celui d'un client, d'un logiciel d'étiquetage…) : champs, codes-barres et ce qu'ils contiennent,
/// réglages, et ce qui risque de mal s'imprimer. Couvre les commandes de mise en page courantes ; les autres sont signalées.
/// </summary>
public static class ZplReader
{
    /// <summary>Le texte ressemble à un programme ZPL.</summary>
    public static bool LooksLikeZpl(string text) =>
        text.Contains("^XA", StringComparison.OrdinalIgnoreCase) && (text.Contains("^FD", StringComparison.OrdinalIgnoreCase) || text.Contains("^GB", StringComparison.OrdinalIgnoreCase));

    // Commandes connues sans effet sur le dessin (réglages d'impression, de média…).
    private static readonly HashSet<string> Settings = new(StringComparer.Ordinal)
    {
        "XA", "XZ", "FS", "MN", "MT", "MM", "MD", "PR", "LT", "LS", "JM", "JU", "PM", "PF", "PH", "PP", "MC", "MF", "MP", "MU", "JZ", "JB", "JE",
        "SD", "SZ", "DF", "XF", "XB", "FN", "FX", "PA", "TO", "WD", "HH", "HS", "HM", "HI", "JA", "JS", "JT", "KN", "KP", "LR", "SN", "SF", "ID", "IL", "IS", "IM",
        "FC", "FP", "CW", "CC", "CD", "CT", "JD", "JX", "JL", "NC", "SC", "SL", "ST", "SX", "TA", "EG", "DG", "DY", "XG", "IM", "FM", "FL", "PS",
    };

    private sealed class Field
    {
        public int X;
        public int Y;
        public bool Typeset;
        public bool HasOrigin;
        public string? Font;
        public int FontH;
        public int FontW;
        public ZplOrientation? FontOrientation;
        public string? Barcode;
        public string[] BarcodeParams = [];
        public string? Data;
        public char? Hex;
        public bool Reverse;
        public string[]? Box;
        public int BlockWidth;
        public char BlockAlign = 'L';
        public int OriginLine;
    }

    public static ZplLabel Read(string program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var issues = new List<Alert>();
        var elements = new List<ZplElement>();
        var unknown = new SortedSet<string>(StringComparer.Ordinal);
        int? pw = null, ll = null;
        bool inverted = false, utf8 = false, started = false, ended = false;
        var copies = 1;
        int lhx = 0, lhy = 0;
        string defFont = "A";
        int defH = 9, defW = 5;
        var fw = ZplOrientation.N;
        int byW = 2, byH = 10;
        double byR = 3.0;
        var field = new Field();
        var labels = 0;
        var nonAscii = new SortedSet<char>();

        var i = 0;
        var line = 1;
        var s = program;
        while (i < s.Length)
        {
            var c = s[i];
            if (c != '^' && c != '~')
            {
                if (c == '\n')
                {
                    line++;
                }

                i++;
                continue;
            }

            var prefix = c;
            if (i + 1 >= s.Length)
            {
                break;
            }

            var code = s.Substring(i + 1, Math.Min(2, s.Length - i - 1)).ToUpperInvariant();
            var start = i + 1 + code.Length;
            var isFont = prefix == '^' && code.Length == 2 && code[0] == 'A' && code != "A@";
            if (isFont)
            {
                start = i + 3;
            }

            // Paramètres : jusqu'au prochain ^ ou ~ (^FD : jusqu'au prochain ^, le ~ pouvant faire partie des données).
            var dataCommand = prefix == '^' && code is "FD" or "FV";
            var end = start;
            while (end < s.Length && s[end] != '^' && (dataCommand || s[end] != '~'))
            {
                end++;
            }

            var raw = s[start..end];
            var lines = raw.Count(ch => ch == '\n');
            var param = dataCommand ? raw.TrimEnd('\r', '\n') : raw.Replace("\r", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal);
            var p = param.Split(',');
            string P(int k) => k < p.Length ? p[k].Trim() : string.Empty;
            int N(int k, int def) => int.TryParse(P(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : def;

            if (prefix == '~')
            {
                // Commandes de contrôle (~SD densité, ~TA décalage, ~JA…) : sans effet sur la mise en page.
                i = end;
                line += lines;
                continue;
            }

            if (ended && code != "XA")
            {
                i = end;
                line += lines;
                continue;
            }

            switch (code)
            {
                case "XA":
                    labels++;
                    if (labels == 1)
                    {
                        started = true;
                    }
                    else
                    {
                        ended = true;
                    }

                    break;
                case "XZ":
                    if (started)
                    {
                        ended = true;
                    }

                    break;
                case "PW":
                    pw = N(0, 0) is > 0 and var w ? w : null;
                    break;
                case "LL":
                    ll = N(0, 0) is > 0 and var l ? l : null;
                    break;
                case "PO":
                    inverted = P(0).StartsWith('I');
                    break;
                case "PQ":
                    copies = Math.Max(1, N(0, 1));
                    break;
                case "CI":
                    utf8 = N(0, 0) is 28 or 29 or 30;
                    break;
                case "LH":
                    lhx = N(0, 0);
                    lhy = N(1, 0);
                    break;
                case "CF":
                    if (P(0).Length > 0)
                    {
                        defFont = P(0)[..1].ToUpperInvariant();
                    }

                    defH = N(1, defH);
                    defW = N(2, P(1).Length > 0 ? defH : defW);
                    break;
                case "FW":
                    fw = Orient(P(0), fw);
                    break;
                case "BY":
                    byW = Math.Clamp(N(0, byW), 1, 10);
                    byR = double.TryParse(P(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && r is >= 2 and <= 3 ? r : byR;
                    byH = N(2, byH);
                    break;
                case "FO" or "FT":
                    field.X = N(0, 0) + lhx;
                    field.Y = N(1, 0) + lhy;
                    field.Typeset = code == "FT";
                    field.HasOrigin = true;
                    field.OriginLine = line;
                    break;
                case "FH":
                    field.Hex = P(0).Length > 0 ? P(0)[0] : '_';
                    break;
                case "FR":
                    field.Reverse = true;
                    break;
                case "FB":
                    field.BlockWidth = N(0, 0);
                    field.BlockAlign = P(3).Length > 0 ? char.ToUpperInvariant(P(3)[0]) : 'L';
                    break;
                case "FD" or "FV":
                    field.Data = param;
                    break;
                case "GB":
                    field.Box = p;
                    break;
                case "FS":
                    Finish(field);
                    field = new Field();
                    break;
                default:
                    if (isFont)
                    {
                        field.Font = code[1..].ToUpperInvariant();
                        var rest = param;
                        if (rest.Length > 0 && "NRIB".Contains(char.ToUpperInvariant(rest[0])))
                        {
                            field.FontOrientation = Orient(rest[..1], fw);
                            rest = rest[1..];
                        }

                        var fp = rest.TrimStart(',').Split(',');
                        field.FontH = int.TryParse(fp.ElementAtOrDefault(0), out var fh) && fh > 0 ? fh : defH;
                        field.FontW = int.TryParse(fp.ElementAtOrDefault(1), out var fwid) && fwid > 0 ? fwid : field.FontH;
                    }
                    else if (code.Length == 2 && code[0] == 'B' && code != "BY")
                    {
                        field.Barcode = code;
                        field.BarcodeParams = p;
                    }
                    else if (!Settings.Contains(code))
                    {
                        unknown.Add("^" + code);
                    }

                    break;
            }

            i = end;
            line += lines;
        }

        void Finish(Field f)
        {
            var at = f.HasOrigin ? f.OriginLine : line;
            if (f.Box is { } b)
            {
                int B(int k, int def) => int.TryParse(b.ElementAtOrDefault(k)?.Trim(), out var v) ? v : def;
                var t = Math.Max(1, B(2, 1));
                var w = Math.Max(t, B(0, t));
                var h = Math.Max(t, B(1, t));
                var col = (b.ElementAtOrDefault(3)?.Trim() ?? "B").StartsWith('W');
                elements.Add(new ZplBox(f.X, f.Y, f.Typeset, line, w, h, t, col, Math.Clamp(B(4, 0), 0, 8), f.Reverse));
                return;
            }

            if (f.Data is null)
            {
                return;
            }

            var data = f.Hex is { } hx ? DecodeHex(f.Data, hx) : f.Data;
            foreach (var ch in data.Where(ch => ch > '~'))
            {
                nonAscii.Add(ch);
            }

            var printed = utf8 ? data : ToCp850(data);
            if (!f.HasOrigin)
            {
                issues.Add(new Alert(Severity.Warning, $"Ligne {line} : champ sans position (^FO ou ^FT) : il s'imprime en haut à gauche."));
            }

            if (f.Barcode is { } bc)
            {
                elements.Add(MakeBarcode(f, bc, data, fw, byW, byR, byH, at));
            }
            else
            {
                var font = f.Font ?? defFont;
                var h = f.Font is null ? defH : f.FontH;
                var w = f.Font is null ? defW : f.FontW;
                elements.Add(new ZplText(f.X, f.Y, f.Typeset, f.FontOrientation ?? fw, at, font, h, w, printed, f.Reverse, f.BlockWidth, f.BlockAlign));
            }
        }

        // ------------------------------------------------------------------ Constats

        if (labels == 0)
        {
            issues.Add(new Alert(Severity.Error, "Pas de ^XA : l'imprimante n'interprète pas ce texte comme une étiquette."));
        }
        else if (!ended)
        {
            issues.Add(new Alert(Severity.Error, "Pas de ^XZ à la fin : l'étiquette n'est jamais imprimée (l'imprimante attend la suite)."));
        }

        if (labels > 1)
        {
            issues.Add(new Alert(Severity.Info, $"{labels} étiquettes dans le programme : seule la première est affichée."));
        }

        if (pw is null || ll is null)
        {
            var missing = pw is null && ll is null ? "ni la largeur (^PW) ni la longueur (^LL)" : pw is null ? "pas la largeur (^PW)" : "pas la longueur (^LL)";
            issues.Add(new Alert(Severity.Info, $"Le programme ne fixe {missing} : ce sont les réglages de l'imprimante qui s'appliquent. L'aperçu s'ajuste au contenu."));
        }

        if (inverted)
        {
            issues.Add(new Alert(Severity.Info, "^POI : l'étiquette est imprimée tête-bêche (tournée de 180°). L'aperçu la montre telle qu'elle a été dessinée."));
        }

        if (copies > 1)
        {
            issues.Add(new Alert(Severity.Info, $"^PQ{copies} : {copies} exemplaires à chaque envoi."));
        }

        if (nonAscii.Count > 0)
        {
            if (utf8)
            {
                issues.Add(new Alert(Severity.Info, "Accents et caractères spéciaux en UTF-8 (^CI28) : le fichier doit être envoyé encodé en UTF-8."));
            }
            else
            {
                var map = string.Join(", ", nonAscii.Take(8).Select(ch => ToCp850(ch.ToString()) == ch.ToString() ? $"« {ch} »" : $"« {ch} » s'imprime « {ToCp850(ch.ToString())} »"));
                issues.Add(new Alert(Severity.Warning,
                    $"Caractères accentués sans ^CI28 : l'imprimante utilise sa page de codes 850. {map}. Pour imprimer les accents tels quels, ajoute ^CI28 après ^XA et envoie le fichier en UTF-8."));
            }
        }

        foreach (var e in elements)
        {
            if (pw is { } w && e.X > w)
            {
                issues.Add(new Alert(Severity.Warning, $"Ligne {e.Line} : champ placé à x = {e.X}, au-delà de la largeur ^PW{w} : il ne s'imprime pas."));
            }

            if (ll is { } l && e.Y > l + 2)
            {
                issues.Add(new Alert(Severity.Warning, $"Ligne {e.Line} : champ placé à y = {e.Y}, au-delà de la longueur ^LL{l}."));
            }
        }

        var texts = elements.OfType<ZplText>().ToList();
        var padded = texts.Count(t => t.Text.Length > 0 && t.Text.Trim().Length == 0);
        if (padded > 0)
        {
            issues.Add(new Alert(Severity.Info, $"{padded} champ{(padded > 1 ? "s" : string.Empty)} texte vide{(padded > 1 ? "s" : string.Empty)} (que des espaces) : donnée absente côté logiciel ?"));
        }

        if (unknown.Count > 0)
        {
            issues.Add(new Alert(Severity.Info, $"Commandes non dessinées dans l'aperçu : {string.Join(" ", unknown.Take(12))}."));
        }

        return new ZplLabel(pw, ll, inverted, copies, utf8, elements, issues, labels);
    }

    /// <summary>Description de l'étiquette pour le dessin par l'Atelier (JSON, coordonnées en points).</summary>
    public static string RenderJson(ZplLabel label)
    {
        ArgumentNullException.ThrowIfNull(label);
        var items = new List<Dictionary<string, object?>>();
        foreach (var e in label.Elements)
        {
            var d = new Dictionary<string, object?> { ["x"] = e.X, ["y"] = e.Y, ["ft"] = e.Typeset, ["o"] = e.Orientation.ToString() };
            switch (e)
            {
                case ZplBox b:
                    d["k"] = "b";
                    d["w"] = b.Width;
                    d["h"] = b.Height;
                    d["t"] = b.Thickness;
                    d["white"] = b.White;
                    d["rev"] = b.Reverse;
                    break;
                case ZplText t:
                    d["k"] = "t";
                    d["font"] = t.Font;
                    d["h"] = t.Height;
                    d["w"] = t.Width;
                    d["text"] = t.Text;
                    d["rev"] = t.Reverse;
                    d["bw"] = t.BlockWidth;
                    d["ba"] = t.BlockAlign.ToString();
                    break;
                case ZplBarcode c:
                    d["k"] = "c";
                    d["format"] = c.Format;
                    d["draw"] = c.Gs1 && Gs1Parser.Parse(c.Content) is { Elements.Count: > 0 } g
                        ? string.Concat(g.Elements.Select(x => $"({x.Ai}){x.Value}"))
                        : c.Content;
                    d["gs1"] = c.Gs1;
                    d["module"] = c.ModuleWidth;
                    d["height"] = c.Height;
                    d["hri"] = c.Interpretation;
                    d["above"] = c.InterpretationAbove;
                    d["readable"] = c.Readable;
                    d["rev"] = false;
                    break;
            }

            items.Add(d);
        }

        return System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["width"] = label.WidthDots,
            ["height"] = label.LengthDots,
            ["elements"] = items,
        });
    }

    private static ZplOrientation Orient(string v, ZplOrientation def) => (v.Length > 0 ? char.ToUpperInvariant(v[0]) : ' ') switch
    {
        'N' => ZplOrientation.N,
        'R' => ZplOrientation.R,
        'I' => ZplOrientation.I,
        'B' => ZplOrientation.B,
        _ => def,
    };

    /// <summary>^FH : « _1D » → caractère 0x1D.</summary>
    internal static string DecodeHex(string data, char indicator)
    {
        var sb = new StringBuilder(data.Length);
        for (var k = 0; k < data.Length; k++)
        {
            if (data[k] == indicator && IsHex(data, k + 1) && IsHex(data, k + 2))
            {
                sb.Append((char)Convert.ToInt32(data.Substring(k + 1, 2), 16));
                k += 2;
            }
            else
            {
                sb.Append(data[k]);
            }
        }

        return sb.ToString();
    }

    private static bool IsHex(string s, int k) => k < s.Length && Uri.IsHexDigit(s[k]);

    // Page de codes 850 (jeu par défaut des Zebra, ^CI0) : octets 0x80 à 0xFF.
    private const string Cp850 =
        "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜø£Ø×ƒáíóúñÑªº¿®¬½¼¡«»░▒▓│┤ÁÂÀ©╣║╗╝¢¥┐└┴┬├─┼ãÃ╚╔╩╦╠═╬¤ðÐÊËÈıÍÎÏ┘┌█▄¦Ì▀ÓßÔÒõÕµþÞÚÛÙýÝ¯´­±‗¾¶§÷¸°¨·¹³²■ ";

    /// <summary>
    /// Ce que l'imprimante imprime pour un texte envoyé octet par octet (Windows-1252 / Latin-1) sans ^CI28 :
    /// chaque octet est lu dans la page de codes 850.
    /// </summary>
    public static string ToCp850(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            sb.Append(ch is >= '\u0080' and <= 'ÿ' ? Cp850[ch - 0x80] : ch);
        }

        return sb.ToString();
    }

    // ------------------------------------------------------------------ Codes-barres

    private static ZplBarcode MakeBarcode(Field f, string cmd, string data, ZplOrientation fw, int byW, double byR, int byH, int line)
    {
        var bp = f.BarcodeParams;
        string Q(int k) => k < bp.Length ? bp[k].Trim() : string.Empty;
        int Num(int k, int def) => int.TryParse(Q(k), out var v) && v > 0 ? v : def;
        bool Yes(int k, bool def) => Q(k).Length == 0 ? def : char.ToUpperInvariant(Q(k)[0]) == 'Y';
        var o = Orient(Q(0), fw);
        var notes = new List<Alert>();
        var pars = new List<(string, string)>();
        string name, format = string.Empty, content = data, readable = data;
        var gs1 = false;
        var module = byW;
        var height = byH;
        bool hri = true, above = false;

        switch (cmd)
        {
            case "BC":
            {
                name = "Code 128";
                format = "Code128";
                height = Num(1, byH);
                hri = Yes(2, true);
                above = Yes(3, false);
                var mode = Q(5).Length > 0 ? char.ToUpperInvariant(Q(5)[0]) : 'N';
                pars.Add(("Mode", mode switch { 'D' => "D (UCC/EAN : GS1 automatique)", 'A' => "A (automatique)", 'U' => "U (UCC Case Mode)", _ => "N (normal, sous-ensembles par >…)" }));
                if (mode == 'D' || mode == 'U')
                {
                    gs1 = true;
                    name = "GS1-128";
                    var compact = new string([.. data.Where(ch => ch != ' ')]);
                    if (mode == 'U')
                    {
                        var digits = new string([.. compact.Where(char.IsAsciiDigit)]);
                        compact = "(00)" + (digits.Length >= 19 ? digits[..19] : digits) + (digits.Length >= 19 ? CheckDigit.Compute(digits.AsSpan(0, 19)).ToString(CultureInfo.InvariantCulture) : string.Empty);
                    }

                    var msg = Gs1Parser.Parse(compact.StartsWith('(') ? compact : "(" + compact[..Math.Min(2, compact.Length)] + ")" + compact[Math.Min(2, compact.Length)..]);
                    if (msg is { Elements.Count: > 0 })
                    {
                        content = msg.ToTransmitted();
                        readable = data;
                        foreach (var e in msg.Elements.Where(e => !e.IsValid))
                        {
                            notes.Add(new Alert(Severity.Error, $"({e.Ai}) {e.Name} : {string.Join(" ; ", e.Issues)}"));
                        }
                    }
                    else
                    {
                        notes.Add(new Alert(Severity.Error, "Mode D : les données ne forment pas une chaîne GS1 valide « (AI)valeur »."));
                        content = compact;
                    }

                    if (data.Contains(' ', StringComparison.Ordinal))
                    {
                        notes.Add(new Alert(Severity.Info, "Les espaces servent seulement à aérer le texte lisible : ils ne sont pas encodés."));
                    }
                }
                else if (mode == 'N')
                {
                    (content, gs1) = Code128Invocations(data);
                    readable = content.Replace(ControlCharacters.GroupSeparator.ToString(), string.Empty, StringComparison.Ordinal);
                    if (gs1)
                    {
                        name = "GS1-128";
                        notes.Add(new Alert(Severity.Info, ">8 en tête : FNC1, le code est un GS1-128."));
                    }
                }

                if (Yes(4, false))
                {
                    notes.Add(new Alert(Severity.Info, "Caractère de contrôle UCC (mod 103) demandé en plus : rarement utile."));
                }

                break;
            }

            case "BE" or "B8" or "BU" or "B9":
            {
                (name, format, var body) = cmd switch
                {
                    "BE" => ("EAN-13", "EAN13", 12),
                    "B8" => ("EAN-8", "EAN8", 7),
                    "BU" => ("UPC-A", "UPCA", 11),
                    _ => ("UPC-E", "UPCE", 7),
                };
                height = Num(1, byH);
                hri = Yes(2, true);
                above = Yes(3, false);
                var digits = new string([.. data.Where(char.IsAsciiDigit)]);
                if (digits.Length != data.Trim().Length)
                {
                    notes.Add(new Alert(Severity.Error, $"{name} : seuls les chiffres sont permis."));
                }

                if (digits.Length >= body)
                {
                    var key = CheckDigit.Compute(digits.AsSpan(0, body));
                    content = digits[..body] + key.ToString(CultureInfo.InvariantCulture);
                    if (digits.Length > body && digits[body] - '0' != key)
                    {
                        notes.Add(new Alert(Severity.Warning, $"Clé saisie {digits[body]}, l'imprimante imprime la clé calculée {key}."));
                    }
                    else
                    {
                        notes.Add(new Alert(Severity.Ok, $"Clé de contrôle {key} calculée par l'imprimante."));
                    }
                }
                else
                {
                    notes.Add(new Alert(Severity.Error, $"{name} : {body} chiffres attendus, {digits.Length} reçus : l'imprimante complète ou refuse le code."));
                }

                readable = content;
                break;
            }

            case "B3":
                name = "Code 39";
                format = "Code39";
                height = Num(2, byH);
                hri = Yes(3, true);
                above = Yes(4, false);
                if (Yes(1, false))
                {
                    notes.Add(new Alert(Severity.Info, "Clé modulo 43 ajoutée par l'imprimante (elle fait partie des données lues si la douchette ne la contrôle pas)."));
                }

                break;
            case "BA":
                name = "Code 93";
                format = "Code93";
                height = Num(1, byH);
                hri = Yes(2, true);
                above = Yes(3, false);
                break;
            case "B2":
                name = "ITF (2 parmi 5 entrelacé)";
                format = "ITF";
                height = Num(1, byH);
                hri = Yes(2, true);
                above = Yes(3, false);
                if (data.Length % 2 != 0)
                {
                    notes.Add(new Alert(Severity.Warning, "Nombre impair de chiffres : l'imprimante ajoute un 0 devant."));
                }

                break;
            case "BK":
                name = "Codabar";
                format = "Codabar";
                height = Num(2, byH);
                hri = Yes(3, true);
                above = Yes(4, false);
                break;
            case "BQ":
            {
                name = "QR Code";
                format = "QRCode";
                module = Math.Clamp(Num(2, 2), 1, 100);
                hri = false;
                pars.Add(("Grossissement", module.ToString(CultureInfo.InvariantCulture)));
                var m = System.Text.RegularExpressions.Regex.Match(data, "^([HQML]?)([AM]),");
                if (m.Success)
                {
                    var ec = m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : "M";
                    pars.Add(("Correction d'erreur", ec + ec switch { "H" => " (30 %)", "Q" => " (25 %)", "L" => " (7 %)", _ => " (15 %)" }));
                    content = data[m.Length..];
                    if (m.Groups[2].Value == "M" && content.Length > 0)
                    {
                        content = content[0] is 'B' or 'b' && content.Length > 5 ? content[5..] : content[1..];
                    }
                }
                else
                {
                    notes.Add(new Alert(Severity.Warning, "Données sans préfixe « QA, » / « MA, » : les premiers caractères sont pris pour des réglages et n'apparaissent pas dans le code."));
                    content = data.Length > 3 ? data[3..] : string.Empty;
                }

                if (o != ZplOrientation.N)
                {
                    notes.Add(new Alert(Severity.Info, "^BQ ne se tourne pas : le QR est imprimé à l'endroit."));
                    o = ZplOrientation.N;
                }

                readable = content;
                break;
            }

            case "BX":
            {
                name = "Data Matrix";
                format = "DataMatrix";
                module = Math.Clamp(Num(1, byW), 1, 100);
                hri = false;
                pars.Add(("Taille d'un module", module + " points"));
                var quality = Num(2, 0);
                if (quality is > 0 and < 200)
                {
                    notes.Add(new Alert(Severity.Warning, $"Qualité {quality} : ancien Data Matrix ECC 000-140, que beaucoup de lecteurs refusent. Utilise 200 (ECC 200)."));
                }

                var esc = Q(6);
                if (esc.Length > 0)
                {
                    var e = esc[0];
                    var fnc1 = e + "1";
                    if (data.StartsWith(fnc1, StringComparison.Ordinal))
                    {
                        gs1 = true;
                        name = "GS1 DataMatrix";
                        content = data[2..].Replace(fnc1, ControlCharacters.GroupSeparator.ToString(), StringComparison.Ordinal);
                    }
                    else
                    {
                        content = data.Replace(fnc1, ControlCharacters.GroupSeparator.ToString(), StringComparison.Ordinal);
                    }
                }

                readable = gs1 && Gs1Parser.Parse(content) is { Elements.Count: > 0 } dm
                    ? string.Concat(dm.Elements.Select(x => $"({x.Ai}){x.Value}"))
                    : content;
                if (gs1 && Gs1Parser.Parse(content) is { } g)
                {
                    foreach (var x in g.Elements.Where(x => !x.IsValid))
                    {
                        notes.Add(new Alert(Severity.Error, $"({x.Ai}) {x.Name} : {string.Join(" ; ", x.Issues)}"));
                    }
                }

                break;
            }

            case "B7":
                name = "PDF417";
                format = "PDF417";
                height = Num(1, Math.Max(byW * 3, 6));
                hri = false;
                pars.Add(("Hauteur de ligne", height + " points"));
                break;
            case "B0" or "BO":
                name = "Aztec";
                format = "Aztec";
                module = Math.Clamp(Num(1, 2), 1, 100);
                hri = false;
                pars.Add(("Grossissement", module.ToString(CultureInfo.InvariantCulture)));
                break;
            case "BD":
                name = "MaxiCode";
                hri = false;
                notes.Add(new Alert(Severity.Info, "MaxiCode : contenu lu, mais pas dessiné dans l'aperçu."));
                break;
            case "BR":
                name = "GS1 DataBar";
                format = "DataBar";
                hri = false;
                break;
            default:
                name = "Code ^" + cmd;
                hri = false;
                notes.Add(new Alert(Severity.Info, $"^{cmd} : symbologie lue mais pas dessinée dans l'aperçu."));
                break;
        }

        if (format.Length > 0 && module > 0 && cmd is not ("BQ" or "BX" or "B0" or "BO"))
        {
            pars.Insert(0, ("Module (^BY)", $"{byW} point{(byW > 1 ? "s" : string.Empty)}"));
            pars.Add(("Hauteur", height + " points"));
        }

        if (content.Any(ch => ch < ' ') && !gs1)
        {
            notes.Add(new Alert(Severity.Info, $"Contient des caractères invisibles : {ControlCharacters.Visualize(new string([.. content.Where(ch => ch < ' ').Distinct()]))}."));
        }

        return new ZplBarcode(f.X, f.Y, f.Typeset, o, line, "^" + cmd, name, format, data, content, readable, gs1,
            module, byR, height, hri, above, pars, notes);
    }

    /// <summary>^BC mode N : codes d'invocation « >… » (changement de sous-ensemble, FNC1…).</summary>
    internal static (string Content, bool Gs1) Code128Invocations(string data)
    {
        var sb = new StringBuilder();
        var gs1 = false;
        for (var k = 0; k < data.Length; k++)
        {
            if (data[k] == '>' && k + 1 < data.Length)
            {
                var n = data[k + 1];
                k++;
                switch (n)
                {
                    case '8':
                        if (sb.Length == 0)
                        {
                            gs1 = true;
                        }
                        else
                        {
                            sb.Append(ControlCharacters.GroupSeparator);
                        }

                        break;
                    case '<':
                        sb.Append('<');
                        break;
                    case '0':
                        sb.Append('>');
                        break;
                    case '=':
                        sb.Append('~');
                        break;
                    default:
                        // >9 >: >; (départ A/B/C), >5 >6 >7 (changement), >1 >2 >3 >4 (FNC) : non transmis.
                        break;
                }
            }
            else
            {
                sb.Append(data[k]);
            }
        }

        return (sb.ToString(), gs1);
    }
}
