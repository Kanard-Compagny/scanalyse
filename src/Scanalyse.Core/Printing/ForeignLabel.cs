using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scanalyse.Core.Content;
using Scanalyse.Core.Gs1;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.Printing;

/// <summary>Étiquette lue dans un programme TSPL, EPL2, DPL, SBPL ou Fingerprint, prête à être dessinée comme une étiquette ZPL.</summary>
/// <param name="Label">Champs de l'étiquette (coordonnées en points, origine en haut à gauche).</param>
/// <param name="Pivot">
/// Repère des champs tournés : « tl » (TSPL, EPL2, SBPL) le champ tourne autour de son point d'origine ; « bl » (DPL, Fingerprint) le point
/// donné est le coin bas gauche du champ.
/// </param>
public sealed record ForeignLabel(ZplLabel Label, string Pivot)
{
    /// <summary>Description de l'étiquette pour le dessin par l'Atelier.</summary>
    public string RenderJson() => ZplReader.RenderJson(Label, Pivot);
}

/// <summary>
/// Lit les programmes TSPL (TSC…), EPL2 (anciennes Zebra), DPL (Honeywell / Datamax), SBPL (SATO) et Fingerprint (Intermec / Honeywell) pour en dessiner un aperçu : textes,
/// cadres, traits et codes-barres. Les polices sont approchées ; les commandes non reconnues sont ignorées.
/// </summary>
public static partial class ForeignLabelReader
{
    /// <summary>Lit un programme dans le langage indiqué.</summary>
    /// <param name="program">Texte du programme.</param>
    /// <param name="kind">Langage (TSPL, EPL2, DPL, SBPL ou Fingerprint).</param>
    /// <param name="dpi">Résolution de la tête, pour convertir les millimètres et pouces en points.</param>
    /// <returns>L'étiquette (la première si le programme en contient plusieurs), ou <c>null</c> si ce langage n'est pas lu.</returns>
    public static ForeignLabel? Read(string program, ProgramKind kind, int dpi)
    {
        ArgumentNullException.ThrowIfNull(program);
        dpi = dpi is >= 100 and <= 1200 ? dpi : 203;
        return kind switch
        {
            ProgramKind.Tspl => new ForeignLabel(ReadTspl(program, dpi), "tl"),
            ProgramKind.Epl2 => new ForeignLabel(ReadEpl(program, dpi), "tl"),
            ProgramKind.Dpl => new ForeignLabel(ReadDpl(program, dpi), "bl"),
            ProgramKind.Sbpl => ReadAll(program, kind, dpi) is [var sato, ..] ? new ForeignLabel(sato, "tl") : null,
            ProgramKind.Fingerprint => ReadAll(program, kind, dpi) is [var fp, ..] ? new ForeignLabel(fp, "bl") : null,
            _ => null,
        };
    }

    // ------------------------------------------------------------------ TSPL

    private static ZplLabel ReadTspl(string program, int dpi)
    {
        var elements = new List<ZplElement>();
        var issues = new List<Alert>();
        int? width = null, length = null;
        int refX = 0, refY = 0, copies = 1;
        var scale = dpi / 203.0;
        foreach (var (n, raw) in Lines(program))
        {
            var line = raw.Trim();
            var m = Command().Match(line);
            if (!m.Success)
            {
                continue;
            }

            var cmd = m.Groups[1].Value.ToUpperInvariant();
            var a = Split(m.Groups[2].Value);
            switch (cmd)
            {
                case "SIZE":
                    width = TsplLength(Arg(a, 0), dpi) ?? width;
                    length = TsplLength(Arg(a, 1), dpi) ?? length;
                    break;
                case "REFERENCE":
                    refX = Int(a, 0);
                    refY = Int(a, 1);
                    break;
                case "PRINT":
                    copies = Math.Max(1, Int(a, 0)) * Math.Max(1, a.Count > 1 ? Int(a, 1) : 1);
                    break;
                case "TEXT" when a.Count >= 7:
                {
                    var (h, w) = TsplFont(Unquote(a[2]), Dbl(a, 4), Dbl(a, 5), dpi, scale);
                    var text = Unquote(a[^1]);
                    elements.Add(new ZplText(refX + Int(a, 0), refY + Int(a, 1), false, Rotation(Int(a, 3)), n, Bitmap(Unquote(a[2])) ? "A" : "0", h, w, text, false));
                    break;
                }

                case "BLOCK" when a.Count >= 9:
                {
                    var (h, w) = TsplFont(Unquote(a[4]), Dbl(a, 6), Dbl(a, 7), dpi, scale);
                    var align = a.Count >= 11 ? Int(a, 9) switch { 2 => 'C', 3 => 'R', _ => 'L' } : 'L';
                    elements.Add(new ZplText(refX + Int(a, 0), refY + Int(a, 1), false, Rotation(Int(a, 5)), n, Bitmap(Unquote(a[4])) ? "A" : "0", h, w,
                        Unquote(a[^1]).Replace("\\[L]", "\n", StringComparison.OrdinalIgnoreCase), false, Math.Max(1, Int(a, 2)), align));
                    break;
                }

                case "BARCODE" when a.Count >= 9:
                {
                    var type = Unquote(a[2]).ToUpperInvariant();
                    var (name, format) = TsplBarcode(type);
                    var data = Unquote(a[^1]);
                    var gs1 = type.StartsWith("EAN128", StringComparison.Ordinal) || data.StartsWith('(');
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), Rotation(Int(a, 5)), n, "BARCODE", name, format, data, gs1,
                        Math.Max(1, Int(a, 6)), Math.Max(1, Int(a, 3)), Int(a, 4) > 0));
                    break;
                }

                case "QRCODE" when a.Count >= 6:
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), Rotation(Int(a, 5)), n, "QRCODE", "QR Code", "QRCode", Unquote(a[^1]), false,
                        Math.Max(1, Int(a, 3)), 0, false));
                    break;
                case "DMATRIX" when a.Count >= 5:
                {
                    // Taille du module : paramètre « x6 » facultatif, sinon environ 6 points.
                    var module = a.Skip(4).Take(a.Count - 5).Select(s => s.Trim()).FirstOrDefault(s => s.StartsWith('x') || s.StartsWith('X')) is { } xm
                        && int.TryParse(xm[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int)Math.Round(6 * scale);
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), ZplOrientation.N, n, "DMATRIX", "Data Matrix", "DataMatrix", Unquote(a[^1]), false,
                        Math.Max(1, module), 0, false));
                    break;
                }

                case "PDF417" when a.Count >= 6:
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), Rotation(Int(a, 4)), n, "PDF417", "PDF417", "PDF417", Unquote(a[^1]), false,
                        (int)Math.Max(1, Math.Round(2 * scale)), (int)Math.Round(8 * scale), false));
                    break;
                case "AZTEC" when a.Count >= 4:
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), Rotation(Int(a, 2)), n, "AZTEC", "Aztec", "Aztec", Unquote(a[^1]), false,
                        a.Count >= 5 && Int(a, 3) > 0 ? Int(a, 3) : (int)Math.Round(6 * scale), 0, false));
                    break;
                case "BOX" when a.Count >= 5:
                {
                    int x1 = Int(a, 0), y1 = Int(a, 1), x2 = Int(a, 2), y2 = Int(a, 3);
                    elements.Add(new ZplBox(refX + Math.Min(x1, x2), refY + Math.Min(y1, y2), false, n, Math.Abs(x2 - x1), Math.Abs(y2 - y1), Math.Max(1, Int(a, 4)), false, 0, false));
                    break;
                }

                case "BAR" when a.Count >= 4:
                {
                    int w = Int(a, 2), h = Int(a, 3);
                    elements.Add(new ZplBox(refX + Int(a, 0), refY + Int(a, 1), false, n, w, h, Math.Max(1, Math.Min(w, h)), false, 0, false));
                    break;
                }

                case "REVERSE" when a.Count >= 4:
                {
                    int w = Int(a, 2), h = Int(a, 3);
                    elements.Add(new ZplBox(refX + Int(a, 0), refY + Int(a, 1), false, n, w, h, Math.Max(1, Math.Min(w, h)), false, 0, true));
                    break;
                }
            }
        }

        return new ZplLabel(width, length, false, copies, true, elements, issues, 1);
    }

    /// <summary>« 100 mm », « 4 » (pouces) ou « 812 dot » → points.</summary>
    private static int? TsplLength(string value, int dpi)
    {
        var v = value.Trim().ToLowerInvariant();
        var unit = v.EndsWith("mm", StringComparison.Ordinal) ? "mm" : v.EndsWith("dot", StringComparison.Ordinal) ? "dot" : "in";
        var number = v.Replace("mm", string.Empty, StringComparison.Ordinal).Replace("dot", string.Empty, StringComparison.Ordinal).Trim();
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) || d <= 0)
        {
            return null;
        }

        return (int)Math.Round(unit switch { "mm" => d * dpi / 25.4, "dot" => d, _ => d * dpi });
    }

    private static bool Bitmap(string font) => font.Length == 1 && font[0] is >= '1' and <= '8';

    /// <summary>Hauteur et largeur des caractères : polices bitmap 1 à 8 (dimensions à 203 dpi), sinon police vectorielle en points typographiques.</summary>
    private static (int H, int W) TsplFont(string font, double xmul, double ymul, int dpi, double scale)
    {
        if (Bitmap(font))
        {
            var (w, h) = font[0] switch
            {
                '1' => (8, 12),
                '2' => (12, 20),
                '3' => (16, 24),
                '4' => (24, 32),
                '5' => (32, 48),
                '6' => (14, 19),
                '7' => (21, 27),
                _ => (14, 25),
            };
            return ((int)Math.Round(h * Math.Max(1, ymul) * scale), (int)Math.Round(w * Math.Max(1, xmul) * scale));
        }

        // Police TrueType (« 0 », « ROMAN.TTF »…) : multiplicateurs = taille en points.
        var size = (int)Math.Round(Math.Max(4, ymul > 0 ? ymul : 10) * dpi / 72.0);
        var width = (int)Math.Round(Math.Max(4, xmul > 0 ? xmul : ymul) * dpi / 72.0);
        return (size, width);
    }

    private static (string Name, string Format) TsplBarcode(string type) => type switch
    {
        "128" or "128M" => ("Code 128", "Code128"),
        "EAN128" or "EAN128M" => ("GS1-128", "Code128"),
        "39" or "39C" or "39S" => ("Code 39", "Code39"),
        "93" => ("Code 93", "Code93"),
        "EAN13" or "EAN13+2" or "EAN13+5" => ("EAN-13", "EAN13"),
        "EAN8" or "EAN8+2" or "EAN8+5" => ("EAN-8", "EAN8"),
        "UPCA" or "UPCA+2" or "UPCA+5" => ("UPC-A", "UPCA"),
        "UPCE" or "UPCE+2" or "UPCE+5" => ("UPC-E", "UPCE"),
        "25" or "25C" => ("ITF", "ITF"),
        "ITF14" => ("ITF-14", "ITF"),
        "CODA" => ("Codabar", "Codabar"),
        _ => ("Code " + type, string.Empty),
    };

    // ------------------------------------------------------------------ EPL2

    private static ZplLabel ReadEpl(string program, int dpi)
    {
        var elements = new List<ZplElement>();
        int? width = null, length = null;
        int refX = 0, refY = 0, copies = 1;
        var inverted = false;
        var scale = dpi / 203.0;
        foreach (var (n, raw) in Lines(program))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line is "ZB")
            {
                inverted = true;
                continue;
            }

            var m = EplCommand().Match(line);
            if (!m.Success)
            {
                continue;
            }

            var cmd = m.Groups[1].Value;
            var a = Split(m.Groups[2].Value);
            switch (cmd)
            {
                case "q":
                    width = Int(a, 0) > 0 ? Int(a, 0) : width;
                    break;
                case "Q":
                    length = Int(a, 0) > 0 ? Int(a, 0) : length;
                    break;
                case "R":
                    refX = Int(a, 0);
                    refY = Int(a, 1);
                    break;
                case "P":
                    copies = Math.Max(1, Int(a, 0));
                    break;
                case "A" when a.Count >= 8:
                {
                    var font = a[3].Trim();
                    var (cw, ch) = font switch
                    {
                        "1" => (8, 12),
                        "2" => (10, 16),
                        "3" => (12, 20),
                        "4" => (14, 24),
                        "5" => (32, 48),
                        _ => (12, 20),
                    };
                    var reverse = a[6].Trim().Equals("R", StringComparison.OrdinalIgnoreCase);
                    elements.Add(new ZplText(refX + Int(a, 0), refY + Int(a, 1), false, Rotation(Int(a, 2) * 90), n, "A",
                        (int)Math.Round(ch * Math.Max(1, Int(a, 5)) * scale), (int)Math.Round(cw * Math.Max(1, Int(a, 4)) * scale), Unquote(a[^1], tspl: false), reverse));
                    break;
                }

                case "B" when a.Count >= 9:
                {
                    var type = a[3].Trim().ToUpperInvariant();
                    var (name, format) = EplBarcode(type);
                    var data = Unquote(a[^1], tspl: false);
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), Rotation(Int(a, 2) * 90), n, "B", name, format, data, type == "1E" || data.StartsWith('('),
                        Math.Max(1, Int(a, 4)), Math.Max(1, Int(a, 6)), a[7].Trim().Equals("B", StringComparison.OrdinalIgnoreCase)));
                    break;
                }

                case "b" when a.Count >= 4:
                {
                    var type = a[2].Trim().ToUpperInvariant();
                    var (name, format) = type switch
                    {
                        "Q" => ("QR Code", "QRCode"),
                        "D" => ("Data Matrix", "DataMatrix"),
                        "P" => ("PDF417", "PDF417"),
                        "A" => ("Aztec", "Aztec"),
                        "M" => ("MaxiCode", "MaxiCode"),
                        _ => ("Code 2D " + type, string.Empty),
                    };

                    // Taille du module : « s5 » (QR), « c… r… » (Data Matrix)… à défaut, 4 points.
                    var module = a.Skip(3).Take(a.Count - 4).Select(s => s.Trim()).FirstOrDefault(s => s.Length > 1 && s[0] is 's' or 'S' && char.IsAsciiDigit(s[1])) is { } sp
                        && int.TryParse(sp[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (int)Math.Round(4 * scale);
                    elements.Add(Barcode(refX + Int(a, 0), refY + Int(a, 1), ZplOrientation.N, n, "b", name, format, Unquote(a[^1], tspl: false), false,
                        Math.Max(1, module), format == "PDF417" ? (int)Math.Round(8 * scale) : 0, false));
                    break;
                }

                case "LO" or "LE" or "LW" when a.Count >= 4:
                {
                    int w = Int(a, 2), h = Int(a, 3);
                    elements.Add(new ZplBox(refX + Int(a, 0), refY + Int(a, 1), false, n, w, h, Math.Max(1, Math.Min(w, h)), cmd == "LW", 0, cmd == "LE"));
                    break;
                }

                case "X" when a.Count >= 5:
                {
                    int x1 = Int(a, 0), y1 = Int(a, 1), x2 = Int(a, 3), y2 = Int(a, 4);
                    elements.Add(new ZplBox(refX + Math.Min(x1, x2), refY + Math.Min(y1, y2), false, n, Math.Abs(x2 - x1), Math.Abs(y2 - y1), Math.Max(1, Int(a, 2)), false, 0, false));
                    break;
                }
            }
        }

        return new ZplLabel(width, length, inverted, copies, false, elements, [], 1);
    }

    private static (string Name, string Format) EplBarcode(string type) => type switch
    {
        "0" or "1" or "1A" or "1B" or "1C" => ("Code 128", "Code128"),
        "1E" => ("GS1-128", "Code128"),
        "3" or "3C" => ("Code 39", "Code39"),
        "9" => ("Code 93", "Code93"),
        "E30" or "E32" or "E35" => ("EAN-13", "EAN13"),
        "E80" or "E82" or "E85" => ("EAN-8", "EAN8"),
        "UA0" or "UA2" or "UA5" => ("UPC-A", "UPCA"),
        "UE0" or "UE2" or "UE5" => ("UPC-E", "UPCE"),
        "2" or "2C" or "2D" or "2G" => ("ITF", "ITF"),
        "K" => ("Codabar", "Codabar"),
        _ => ("Code " + type, string.Empty),
    };

    // ------------------------------------------------------------------ DPL

    private static ZplLabel ReadDpl(string program, int dpi)
    {
        var parsed = new List<(int Line, char Rot, string Id, char C, char D, string Size, int Row, int Col, string Data)>();
        var metric = false;
        var copies = 1;
        foreach (var (n, raw) in Lines(program))
        {
            var line = raw.Trim().Trim('\u0002');
            if (line.Length == 0)
            {
                continue;
            }

            if (line == "m" || line.EndsWith("\u0002m", StringComparison.Ordinal) || raw.Contains("\u0002m", StringComparison.Ordinal))
            {
                metric = true;
                continue;
            }

            if (line.Length >= 5 && line[0] == 'Q' && int.TryParse(line[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q))
            {
                copies = Math.Max(1, q);
                continue;
            }

            var m = DplRecord().Match(line);
            if (!m.Success)
            {
                continue;
            }

            parsed.Add((n, m.Groups[1].Value[0], m.Groups[2].Value, m.Groups[3].Value[0], m.Groups[4].Value[0], m.Groups[5].Value,
                int.Parse(m.Groups[6].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[7].Value, CultureInfo.InvariantCulture), m.Groups[8].Value));
        }

        // Unités : 0,1 mm en mode métrique, sinon 0,01 pouce.
        double Dots(int units) => metric ? units * dpi / 254.0 : units * dpi / 100.0;
        var items = new List<(int Line, ZplOrientation O, int X, int Bottom, int Height, Func<int, int, ZplElement> Make)>();
        foreach (var r in parsed)
        {
            var o = r.Rot switch { '2' => ZplOrientation.B, '3' => ZplOrientation.I, '4' => ZplOrientation.R, _ => ZplOrientation.N };
            var x = (int)Math.Round(Dots(r.Col));
            var bottom = (int)Math.Round(Dots(r.Row));
            int Mul(char c) => c is >= '1' and <= '9' ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : c is >= 'a' and <= 'z' ? c - 'a' + 36 : 1;
            if (r.Id is "X" or "x")
            {
                var d = r.Data;
                if (d.Length >= 9 && (d[0] == 'l' || d[0] == 'b')
                    && int.TryParse(d.AsSpan(1, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var hl)
                    && int.TryParse(d.AsSpan(5, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var vl))
                {
                    var w = (int)Math.Round(Dots(hl));
                    var h = (int)Math.Round(Dots(vl));
                    var t = d[0] == 'b' && d.Length >= 13 && int.TryParse(d.AsSpan(9, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tb)
                        ? Math.Max(1, (int)Math.Round(Dots(tb)))
                        : Math.Max(1, Math.Min(w, h));
                    items.Add((r.Line, ZplOrientation.N, x, bottom, h, (xx, yy) => new ZplBox(xx, yy, false, r.Line, w, h, t, false, 0, false)));
                }

                continue;
            }

            if (r.Id.Length == 1 && char.IsAsciiDigit(r.Id[0]))
            {
                // Texte : polices internes 0 à 8 (hauteurs à 203 dpi) ou police vectorielle 9 (taille en points dans eee).
                var scale = dpi / 203.0;
                int h, w;
                if (r.Id[0] == '9')
                {
                    var pt = int.TryParse(new string([.. r.Size.Where(char.IsAsciiDigit)]), NumberStyles.Integer, CultureInfo.InvariantCulture, out var p) && p > 0 ? p : 10;
                    h = (int)Math.Round(pt * dpi / 72.0);
                    w = (int)Math.Round(h * 0.55);
                }
                else
                {
                    var (bw, bh) = r.Id[0] switch { '0' => (5, 7), '1' => (7, 13), '2' => (10, 18), '3' => (14, 27), '4' => (18, 36), '5' => (24, 52), '6' => (32, 64), '7' => (15, 24), _ => (15, 26) };
                    h = (int)Math.Round(bh * Mul(r.D) * scale);
                    w = (int)Math.Round(bw * Mul(r.C) * scale);
                }

                var text = r.Data;
                items.Add((r.Line, o, x, bottom, h, (xx, yy) => new ZplText(xx, yy, false, o, r.Line, r.Id[0] == '9' ? "0" : "A", h, w, text, false)));
                continue;
            }

            var (name, format) = DplBarcode(r.Id);
            if (name is null)
            {
                continue;
            }

            var twoD = format is "QRCode" or "DataMatrix" or "Aztec" or "MaxiCode";
            // 2D : taille du module dans l'unité de l'étiquette (0,1 mm ou 0,01 pouce) ; 1D : largeur de la barre étroite en points.
            var module = Math.Max(1, twoD ? (int)Math.Round(Dots(Mul(r.C))) : Mul(r.D));
            var barHeight = int.TryParse(r.Size, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hh) ? (int)Math.Round(Dots(hh)) : 0;
            var hri = r.Id.Length == 1 && char.IsUpper(r.Id[0]);
            var data = r.Data;
            var gs1 = r.Id is "Q" or "q" or "R" or "r" || data.StartsWith('(');
            var estimate = twoD ? module * 25 : Math.Max(barHeight, 20) + (hri ? 20 : 0);
            items.Add((r.Line, o, x, bottom, estimate, (xx, yy) => Barcode(xx, yy, o, r.Line, r.Id, name, format, data, gs1, module, Math.Max(1, barHeight), hri)));
        }

        // Origine en bas à gauche : la hauteur de l'étiquette est déduite des champs (repère « bl » : point = coin bas gauche).
        var height = items.Count == 0 ? 0 : items.Max(i => i.Bottom + i.Height) + (int)Math.Round(dpi / 25.4 * 3);
        var elements = items.Select(i => i.Make(i.X, height - i.Bottom)).ToList();
        var issues = new List<Alert>
        {
            new(Severity.Info, metric ? "Unités DPL : 0,1 mm (mode métrique « m »)." : "Unités DPL : 0,01 pouce (pas de commande « m » pour le mode métrique)."),
        };
        return new ZplLabel(null, height > 0 ? height : null, false, copies, true, elements, issues, 1);
    }

    private static (string? Name, string Format) DplBarcode(string id)
    {
        if (id.Length == 3)
        {
            return char.ToLowerInvariant(id[2]) switch
            {
                'c' => ("Data Matrix", "DataMatrix"),
                'd' => ("QR Code", "QRCode"),
                'f' => ("Aztec", "Aztec"),
                _ => ("Code W1" + id[2], string.Empty),
            };
        }

        return char.ToUpperInvariant(id[0]) switch
        {
            'A' => ("Code 39", "Code39"),
            'B' => ("UPC-A", "UPCA"),
            'C' => ("UPC-E", "UPCE"),
            'D' or 'J' => ("ITF", "ITF"),
            'E' => ("Code 128", "Code128"),
            'F' => ("EAN-13", "EAN13"),
            'G' => ("EAN-8", "EAN8"),
            'I' => ("Codabar", "Codabar"),
            'O' => ("Code 93", "Code93"),
            'Q' or 'R' => ("GS1-128", "Code128"),
            'U' => ("MaxiCode", "MaxiCode"),
            'Z' => ("PDF417", "PDF417"),
            _ => (null, string.Empty),
        };
    }

    // ------------------------------------------------------------------ Commun

    private static ZplBarcode Barcode(int x, int y, ZplOrientation o, int line, string cmd, string name, string format, string data, bool gs1, int module, int height, bool hri)
    {
        var raw = ControlCharacters.FromVisualized(data);
        var content = gs1 && Gs1Parser.Parse(raw) is { Elements.Count: > 0 } g ? g.ToTransmitted() : raw;
        var readable = gs1 && Gs1Parser.Parse(raw) is { Elements.Count: > 0 } g2 ? string.Concat(g2.Elements.Select(e => $"({e.Ai}){e.Value}")) : raw;
        return new ZplBarcode(x, y, false, o, line, cmd, name, format, data, content, readable, gs1, module, 3, height, hri, false, [], []);
    }

    private static ZplOrientation Rotation(int degrees) => (((degrees % 360) + 360) % 360) switch
    {
        90 => ZplOrientation.R,
        180 => ZplOrientation.I,
        270 => ZplOrientation.B,
        _ => ZplOrientation.N,
    };

    private static string Arg(List<string> a, int k) => k < a.Count ? a[k] : string.Empty;

    private static int Int(List<string> a, int k) =>
        k < a.Count && double.TryParse(a[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)Math.Round(v) : 0;

    private static double Dbl(List<string> a, int k) =>
        k < a.Count && double.TryParse(a[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static List<string> Split(string s)
    {
        var list = new List<string>();
        var cur = new StringBuilder();
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

    private static string Unquote(string s, bool tspl = true)
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

    [GeneratedRegex(@"^([A-Za-z]+)\s+(.*)$")]
    private static partial Regex Command();

    [GeneratedRegex(@"^(LO|LE|LW|[ABbXqQRP])(-?\d.*)$")]
    private static partial Regex EplCommand();

    [GeneratedRegex(@"^([1-4])(W1[A-Za-z]|[0-9A-WYZa-wyzXx])([0-9A-Za-z])([0-9A-Za-z])([0-9A-Za-z]{3})(\d{4})(\d{4})(.*)$")]
    private static partial Regex DplRecord();
}
