namespace Scanalyse.Core.Printing;

/// <summary>Catégorie d'un morceau de programme d'imprimante, pour la coloration.</summary>
public enum SyntaxKind
{
    Plain,

    /// <summary>Début et fin d'étiquette, fin de champ (^XA ^XZ ^FS).</summary>
    Structure,

    /// <summary>Position d'un champ (^FO ^FT ^LH ^FB).</summary>
    Position,

    /// <summary>Police et sens du texte (^A ^CF ^FW ^CI).</summary>
    Text,

    /// <summary>Code-barres (^B…).</summary>
    Barcode,

    /// <summary>Graphique (^GB ^GC ^GF…).</summary>
    Graphic,

    /// <summary>Autre réglage (^PQ ^PW ^LL ^MD…).</summary>
    Setting,

    /// <summary>Paramètres d'une commande.</summary>
    Parameter,

    /// <summary>Données imprimées (^FD … ou chaîne entre guillemets).</summary>
    Data,

    /// <summary>Commentaire (^FX).</summary>
    Comment,
}

/// <summary>Morceau de programme coloré.</summary>
/// <param name="Start">Position du premier caractère.</param>
/// <param name="Length">Nombre de caractères.</param>
/// <param name="Kind">Catégorie.</param>
public readonly record struct SyntaxToken(int Start, int Length, SyntaxKind Kind);

/// <summary>Découpe un programme ZPL, SBPL (commandes ESC) ou TSPL / EPL2 / Fingerprint (ligne par ligne) pour le colorer.</summary>
public static class ProgramSyntax
{
    public static IReadOnlyList<SyntaxToken> Tokenize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains('^', StringComparison.Ordinal))
        {
            return Zpl(text);
        }

        return text.Contains('\u001B', StringComparison.Ordinal) || text.Contains("<ESC>", StringComparison.OrdinalIgnoreCase) ? Sbpl(text) : Lines(text);
    }

    // Commandes SBPL, les plus longues d'abord, et leur catégorie.
    private static readonly (string Code, SyntaxKind Kind, bool Data)[] SbplCodes =
    [
        ("2D10", SyntaxKind.Barcode, false), ("2D11", SyntaxKind.Barcode, false), ("2D20", SyntaxKind.Barcode, false), ("2D30", SyntaxKind.Barcode, false),
        ("2D31", SyntaxKind.Barcode, false), ("2D50", SyntaxKind.Barcode, false), ("2D51", SyntaxKind.Barcode, false),
        ("A1", SyntaxKind.Setting, false), ("A3", SyntaxKind.Position, false), ("BD", SyntaxKind.Barcode, true), ("BG", SyntaxKind.Barcode, true),
        ("BI", SyntaxKind.Barcode, true), ("BC", SyntaxKind.Barcode, true), ("DN", SyntaxKind.Data, true), ("DS", SyntaxKind.Data, true),
        ("FW", SyntaxKind.Graphic, false), ("XU", SyntaxKind.Text, true), ("XS", SyntaxKind.Text, true), ("XM", SyntaxKind.Text, true),
        ("XL", SyntaxKind.Text, true), ("XB", SyntaxKind.Text, true), ("WB", SyntaxKind.Text, true), ("WL", SyntaxKind.Text, true),
        ("OA", SyntaxKind.Text, true), ("OB", SyntaxKind.Text, true), ("$=", SyntaxKind.Text, true), ("$", SyntaxKind.Text, false),
        ("A", SyntaxKind.Structure, false), ("Z", SyntaxKind.Structure, false), ("H", SyntaxKind.Position, false), ("V", SyntaxKind.Position, false),
        ("%", SyntaxKind.Position, false), ("L", SyntaxKind.Text, false), ("P", SyntaxKind.Text, false), ("U", SyntaxKind.Text, true),
        ("S", SyntaxKind.Text, true), ("M", SyntaxKind.Text, true), ("B", SyntaxKind.Barcode, true), ("D", SyntaxKind.Barcode, true),
        ("(", SyntaxKind.Graphic, false), ("Q", SyntaxKind.Setting, false),
    ];

    /// <summary>SBPL : ESC (ou « &lt;ESC&gt; ») et sa commande, puis paramètres ou données jusqu'au ESC suivant.</summary>
    private static List<SyntaxToken> Sbpl(string s)
    {
        var list = new List<SyntaxToken>();
        var i = 0;
        while (i < s.Length)
        {
            var esc = s[i] == '\u001B' ? 1 : string.Compare(s, i, "<ESC>", 0, 5, StringComparison.OrdinalIgnoreCase) == 0 ? 5 : 0;
            if (esc == 0)
            {
                var j = i;
                while (j < s.Length && s[j] != '\u001B' && string.Compare(s, j, "<ESC>", 0, 5, StringComparison.OrdinalIgnoreCase) != 0)
                {
                    j++;
                }

                list.Add(new SyntaxToken(i, j - i, SyntaxKind.Plain));
                i = j;
                continue;
            }

            var (code, kind, data) = SbplCodes.FirstOrDefault(c => string.CompareOrdinal(s, i + esc, c.Code, 0, c.Code.Length) == 0);
            code ??= string.Empty;
            var cmd = esc + code.Length;
            list.Add(new SyntaxToken(i, cmd, code.Length == 0 ? SyntaxKind.Setting : kind));
            var start = i + cmd;
            var end = start;
            while (end < s.Length && s[end] != '\u001B' && s[end] != '\r' && s[end] != '\n' && string.Compare(s, end, "<ESC>", 0, 5, StringComparison.OrdinalIgnoreCase) != 0)
            {
                end++;
            }

            if (end > start)
            {
                list.Add(new SyntaxToken(start, end - start, data ? SyntaxKind.Data : SyntaxKind.Parameter));
            }

            i = end;
        }

        return list;
    }

    private static List<SyntaxToken> Zpl(string s)
    {
        var list = new List<SyntaxToken>();
        var i = 0;
        var first = s.IndexOfAny(['^', '~']);
        if (first < 0)
        {
            return [new SyntaxToken(0, s.Length, SyntaxKind.Plain)];
        }

        if (first > 0)
        {
            list.Add(new SyntaxToken(0, first, SyntaxKind.Plain));
        }

        i = first;
        while (i < s.Length)
        {
            var code = s.Substring(i + 1, Math.Min(2, s.Length - i - 1)).ToUpperInvariant();
            var cmdLength = 1 + code.Length;
            var kind = Classify(s[i], code);
            list.Add(new SyntaxToken(i, cmdLength, kind));
            var start = i + cmdLength;
            var data = s[i] == '^' && code is "FD" or "FV";
            var end = start;
            while (end < s.Length && s[end] != '^' && (data || s[end] != '~'))
            {
                end++;
            }

            if (end > start)
            {
                var pk = code == "FX" ? SyntaxKind.Comment : data ? SyntaxKind.Data : SyntaxKind.Parameter;
                list.Add(new SyntaxToken(start, end - start, pk));
            }

            i = end;
        }

        return list;
    }

    private static SyntaxKind Classify(char prefix, string code)
    {
        if (prefix == '~')
        {
            return SyntaxKind.Setting;
        }

        return code switch
        {
            "XA" or "XZ" or "FS" => SyntaxKind.Structure,
            "FO" or "FT" or "LH" or "FB" or "FP" => SyntaxKind.Position,
            "CF" or "FW" or "CI" => SyntaxKind.Text,
            "FD" or "FV" => SyntaxKind.Data,
            "FX" => SyntaxKind.Comment,
            _ when code.Length > 0 && code[0] == 'A' => SyntaxKind.Text,
            _ when code.Length > 0 && code[0] == 'B' => SyntaxKind.Barcode,
            _ when code.Length > 0 && code[0] == 'G' => SyntaxKind.Graphic,
            _ => SyntaxKind.Setting,
        };
    }

    /// <summary>TSPL / EPL2 : commande en tête de ligne, chaînes entre guillemets, paramètres.</summary>
    private static List<SyntaxToken> Lines(string s)
    {
        var list = new List<SyntaxToken>();
        var i = 0;
        while (i < s.Length)
        {
            var eol = s.IndexOf('\n', i);
            var lineEnd = eol < 0 ? s.Length : eol + 1;
            var k = i;
            while (k < lineEnd && char.IsAsciiLetter(s[k]))
            {
                k++;
            }

            if (k > i)
            {
                list.Add(new SyntaxToken(i, k - i, SyntaxKind.Structure));
            }

            var p = k;
            while (p < lineEnd)
            {
                if (s[p] == '"')
                {
                    var q = p + 1;
                    while (q < lineEnd && (s[q] != '"' || s[q - 1] == '\\'))
                    {
                        q++;
                    }

                    q = Math.Min(lineEnd, q + 1);
                    list.Add(new SyntaxToken(p, q - p, SyntaxKind.Data));
                    p = q;
                }
                else
                {
                    var q = p;
                    while (q < lineEnd && s[q] != '"')
                    {
                        q++;
                    }

                    list.Add(new SyntaxToken(p, q - p, SyntaxKind.Parameter));
                    p = q;
                }
            }

            i = lineEnd;
        }

        return list;
    }
}
