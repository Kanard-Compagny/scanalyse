using System.Globalization;
using System.Text.RegularExpressions;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Printing;

/// <summary>Langage reconnu dans un fichier d'impression.</summary>
public enum ProgramKind
{
    Unknown,
    Zpl,
    Tspl,
    Epl2,

    /// <summary>DPL (Datamatrix / Honeywell) : commence par STX.</summary>
    Dpl,

    /// <summary>SBPL (SATO) : commandes ESC A … ESC Z.</summary>
    Sbpl,

    /// <summary>Fingerprint / Direct Protocol (Intermec, Honeywell).</summary>
    Fingerprint,
}

/// <summary>Code-barres trouvé dans un programme TSPL ou EPL2.</summary>
/// <param name="Line">Ligne du programme (à partir de 1).</param>
/// <param name="Command">Commande (« BARCODE », « QRCODE », « B »…).</param>
/// <param name="Symbology">Nom de la symbologie.</param>
/// <param name="X">Position horizontale en points.</param>
/// <param name="Y">Position verticale en points.</param>
/// <param name="Data">Données écrites dans le programme.</param>
/// <param name="Gs1">Données GS1 (forme « (01)… » ou code EAN128).</param>
public sealed record ProgramBarcode(int Line, string Command, string Symbology, int X, int Y, string Data, bool Gs1);

/// <summary>Reconnaît le langage d'un fichier d'impression et, pour le TSPL et l'EPL2, liste ses codes-barres.</summary>
public static partial class ProgramFile
{
    public static ProgramKind Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Contains("^XA", StringComparison.OrdinalIgnoreCase))
        {
            return ProgramKind.Zpl;
        }

        var t = text.TrimStart('\uFEFF', '\r', '\n', ' ');
        if (t.StartsWith('\u0002'))
        {
            return ProgramKind.Dpl;
        }

        if (t.Contains("\u001BA", StringComparison.Ordinal) && t.Contains("\u001BZ", StringComparison.Ordinal))
        {
            return ProgramKind.Sbpl;
        }

        var lines = Lines(text).Select(l => l.Text.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count(l => TsplLine().IsMatch(l)) >= 2)
        {
            return ProgramKind.Tspl;
        }

        if (lines.Any(l => l is "N" or "P1") || lines.Count(l => EplLine().IsMatch(l)) >= 2)
        {
            return ProgramKind.Epl2;
        }

        if (lines.Any(l => FingerprintLine().IsMatch(l)))
        {
            return ProgramKind.Fingerprint;
        }

        return ProgramKind.Unknown;
    }

    /// <summary>
    /// Lit les octets d'un fichier d'impression avec le bon encodage : UTF-8 (avec ou sans BOM), UTF-16 (« Unicode » du Bloc-notes,
    /// avec ou sans BOM), sinon octet par octet (Windows-1252 / Latin-1, comme l'imprimante les reçoit).
    /// </summary>
    /// <returns>Le texte et le nom de l'encodage reconnu.</returns>
    public static (string Text, string Encoding) Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new System.Text.UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3), "UTF-8");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (System.Text.Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 (Unicode)");
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (System.Text.Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "UTF-16 big-endian");
        }

        // UTF-16 sans BOM : un octet nul sur deux.
        if (bytes.Length >= 8)
        {
            var pairs = bytes.Length / 2;
            int oddZero = 0, evenZero = 0;
            for (var i = 0; i + 1 < bytes.Length; i += 2)
            {
                evenZero += bytes[i] == 0 ? 1 : 0;
                oddZero += bytes[i + 1] == 0 ? 1 : 0;
            }

            if (oddZero > pairs * 0.4 && evenZero < pairs * 0.05)
            {
                return (System.Text.Encoding.Unicode.GetString(bytes), "UTF-16 (Unicode)");
            }

            if (evenZero > pairs * 0.4 && oddZero < pairs * 0.05)
            {
                return (System.Text.Encoding.BigEndianUnicode.GetString(bytes), "UTF-16 big-endian");
            }
        }

        try
        {
            return (new System.Text.UTF8Encoding(false, true).GetString(bytes), "UTF-8");
        }
        catch (System.Text.DecoderFallbackException)
        {
            return (System.Text.Encoding.Latin1.GetString(bytes), "Windows-1252 / Latin-1");
        }
    }

    /// <summary>Nom affiché du langage.</summary>
    public static string Name(ProgramKind kind) => kind switch
    {
        ProgramKind.Zpl => "ZPL II",
        ProgramKind.Tspl => "TSPL",
        ProgramKind.Epl2 => "EPL2",
        ProgramKind.Dpl => "DPL",
        ProgramKind.Sbpl => "SBPL (SATO)",
        ProgramKind.Fingerprint => "Fingerprint / Direct Protocol",
        _ => "langage non reconnu",
    };

    /// <summary>Codes-barres d'un programme TSPL ou EPL2 (les autres langages renvoient une liste vide).</summary>
    public static IReadOnlyList<ProgramBarcode> Barcodes(string text, ProgramKind kind)
    {
        var list = new List<ProgramBarcode>();
        foreach (var (n, raw) in Lines(text))
        {
            var line = raw.Trim();
            if (kind == ProgramKind.Tspl)
            {
                var m = TsplCode().Match(line);
                if (!m.Success)
                {
                    continue;
                }

                var cmd = m.Groups[1].Value.ToUpperInvariant();
                var args = Split(m.Groups[2].Value);
                var data = Unquote(args.LastOrDefault() ?? string.Empty, tspl: true);
                var type = cmd == "BARCODE" && args.Count > 2 ? Unquote(args[2], tspl: true).ToUpperInvariant() : string.Empty;
                var name = cmd switch
                {
                    "QRCODE" => "QR Code",
                    "DMATRIX" => "Data Matrix",
                    "PDF417" => "PDF417",
                    "AZTEC" => "Aztec",
                    "MAXICODE" => "MaxiCode",
                    _ => TsplType(type),
                };
                var gs1 = type is "EAN128" or "EAN128M" || data.StartsWith('(');
                list.Add(new ProgramBarcode(n, cmd, name, Int(args, 0), Int(args, 1), data, gs1));
            }
            else if (kind == ProgramKind.Dpl)
            {
                // Enregistrement DPL : rotation, identifiant (lettre ou W1x), largeurs, hauteur, ligne, colonne, données.
                var m = DplCode().Match(line.TrimStart('\u0002'));
                if (!m.Success)
                {
                    continue;
                }

                var id = m.Groups[2].Value;
                var name = DplType(id);
                if (name is null)
                {
                    continue;
                }

                var data = m.Groups[8].Value.TrimEnd('\r');
                var gs1 = id is "Q" or "q" or "R" or "r" || data.StartsWith('(');
                list.Add(new ProgramBarcode(n, id, name, int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture), data, gs1));
            }
            else if (kind == ProgramKind.Epl2)
            {
                var m = EplCode().Match(line);
                if (!m.Success)
                {
                    continue;
                }

                var two = m.Groups[1].Value == "b";
                var args = Split(m.Groups[2].Value);
                var data = Unquote(args.LastOrDefault() ?? string.Empty, tspl: false);
                var type = !two && args.Count > 3 ? args[3].Trim().ToUpperInvariant() : string.Empty;
                var name = two
                    ? (args.Count > 2 ? args[2].Trim().ToUpperInvariant() : string.Empty) switch { "Q" => "QR Code", "D" => "Data Matrix", "P" => "PDF417", "A" => "Aztec", "M" => "MaxiCode", var x => "Code 2D " + x }
                    : EplType(type);
                list.Add(new ProgramBarcode(n, two ? "b" : "B", name, Int(args, 0), Int(args, 1), data, type is "1E" || data.StartsWith('(')));
            }
        }

        return list;
    }

    /// <summary>Ce qu'une douchette lira, pour les cas courants (GS1 « (01)… » converti en forme transmise).</summary>
    public static string ScannerContent(ProgramBarcode code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var raw = ControlCharacters.FromVisualized(code.Data);
        if (code.Gs1 && Gs1Parser.Parse(raw) is { Elements.Count: > 0 } g)
        {
            return g.ToTransmitted();
        }

        return raw;
    }

    private static string TsplType(string t) => t switch
    {
        "128" or "128M" => "Code 128",
        "EAN128" or "EAN128M" => "GS1-128",
        "39" or "39C" or "39S" => "Code 39",
        "93" => "Code 93",
        "EAN13" or "EAN13+2" or "EAN13+5" => "EAN-13",
        "EAN8" => "EAN-8",
        "UPCA" => "UPC-A",
        "UPCE" => "UPC-E",
        "25" or "25C" => "ITF",
        "ITF14" => "ITF-14",
        "CODA" => "Codabar",
        _ => "Code " + t,
    };

    /// <summary>Identifiants de codes-barres DPL (majuscule : avec texte lisible, minuscule : sans).</summary>
    private static string? DplType(string id) => id.Length == 3
        ? char.ToLowerInvariant(id[2]) switch { 'c' => "Data Matrix", 'd' => "QR Code", 'f' => "Aztec", var x => "Code W1" + x }
        : char.ToUpperInvariant(id[0]) switch
        {
            'A' => "Code 39",
            'B' => "UPC-A",
            'C' => "UPC-E",
            'D' => "ITF",
            'E' => "Code 128",
            'F' => "EAN-13",
            'G' => "EAN-8",
            'I' => "Codabar",
            'J' => "ITF",
            'O' => "Code 93",
            'Q' or 'R' => "GS1-128",
            'U' => "MaxiCode",
            'Z' => "PDF417",
            _ => null, // chiffres : polices de texte ; X : traits et cadres
        };

    private static string EplType(string t) => t switch
    {
        "1" or "1A" or "1B" or "1C" => "Code 128",
        "1E" => "GS1-128",
        "3" or "3C" => "Code 39",
        "9" => "Code 93",
        "E30" or "E32" or "E35" => "EAN-13",
        "E80" or "E82" or "E85" => "EAN-8",
        "UA0" or "UA2" or "UA5" => "UPC-A",
        "UE0" or "UE2" or "UE5" => "UPC-E",
        "2" or "2C" or "2D" or "2G" => "ITF",
        "K" => "Codabar",
        _ => "Code " + t,
    };

    private static int Int(List<string> a, int k) => k < a.Count && int.TryParse(a[k].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    /// <summary>Découpe les arguments sur les virgules, en respectant les chaînes entre guillemets.</summary>
    private static List<string> Split(string s)
    {
        var list = new List<string>();
        var cur = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\'))
            {
                quoted = !quoted;
            }

            if (c == ',' && !quoted)
            {
                list.Add(cur.ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        list.Add(cur.ToString());
        return list;
    }

    private static string Unquote(string s, bool tspl)
    {
        var t = s.Trim();
        if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
        {
            t = t[1..^1];
        }

        return tspl ? t.Replace("\\[\"]", "\"", StringComparison.Ordinal) : t.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static IEnumerable<(int Number, string Text)> Lines(string text)
    {
        var n = 0;
        foreach (var l in text.Split('\n'))
        {
            n++;
            yield return (n, l.TrimEnd('\r'));
        }
    }

    [GeneratedRegex(@"^(SIZE|GAP|BLINE|CLS|PRINT|BARCODE|TEXT|QRCODE|DMATRIX|DIRECTION|REFERENCE|BOX|BAR|CODEPAGE|DENSITY|SPEED|OFFSET|SET)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TsplLine();

    [GeneratedRegex(@"^([ABbXLGq]\d+,|Q\d+,\d+|R\d+,\d+|S\d$|D\d+$|ZB$|ZT$|I8,)")]
    private static partial Regex EplLine();

    [GeneratedRegex(@"^(PRPOS|PRTXT|PRBAR|PRINTFEED|BARSET|BARFONT|DIR\s|ALIGN\s)", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintLine();

    [GeneratedRegex(@"^(BARCODE|QRCODE|DMATRIX|PDF417|AZTEC|MAXICODE)\s+(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex TsplCode();

    [GeneratedRegex(@"^([1-4])(W1[A-Za-z]|[A-WYZa-wyz])([0-9A-Za-z])([0-9A-Za-z])([0-9A-Za-z]{3})(\d{4})(\d{4})(.*)$")]
    private static partial Regex DplCode();

    [GeneratedRegex(@"^([Bb])(\d+,.*)$")]
    private static partial Regex EplCode();
}
