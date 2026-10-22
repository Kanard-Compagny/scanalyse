using System.Globalization;
using System.Text;
using Scanalyse.Core.Gs1;

namespace Scanalyse.Core.Printing;

/// <summary>Programme d'étiquette converti dans un autre langage, avec ce qui doit être vérifié à l'impression.</summary>
/// <param name="Program">Programme produit.</param>
/// <param name="Warnings">Points à valider sur l'imprimante.</param>
public sealed record ConvertedProgram(string Program, IReadOnlyList<string> Warnings);

/// <summary>
/// Convertit une étiquette ZPL (textes, cadres, traits, codes-barres en orientation normale) en TSPL, EPL2 ou DPL : les modèles
/// d'étiquettes sont ainsi proposés pour toutes les imprimantes. Les polices sont approchées.
/// </summary>
public static class LabelConverter
{
    /// <summary>Convertit un programme ZPL dans le langage demandé.</summary>
    /// <param name="zpl">Programme ZPL (une étiquette ^XA … ^XZ).</param>
    /// <param name="target">Langage voulu.</param>
    /// <param name="dpi">Résolution de la tête.</param>
    /// <param name="copies">Nombre d'exemplaires.</param>
    public static ConvertedProgram Convert(string zpl, PrinterLanguage target, int dpi, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(zpl);
        if (target == PrinterLanguage.Zpl)
        {
            return new ConvertedProgram(zpl, []);
        }

        var label = ZplReader.Read(zpl);
        var warnings = new List<string>();
        var width = label.WidthDots ?? label.Elements.Select(Right).DefaultIfEmpty(400).Max() + 16;
        var height = label.LengthDots ?? label.Elements.Select(e => e.Y + 60).DefaultIfEmpty(300).Max();
        if (label.Elements.Any(e => e.Orientation != ZplOrientation.N))
        {
            warnings.Add("Champs tournés : position à vérifier, chaque langage tourne les champs autour d'un point différent.");
        }

        var program = target switch
        {
            PrinterLanguage.Tspl => Tspl(label, width, height, dpi, copies, warnings),
            PrinterLanguage.Epl2 => Epl(label, width, height, dpi, copies, warnings),
            _ => Dpl(label, height, dpi, copies, warnings),
        };
        return new ConvertedProgram(program, warnings);
    }

    private static int Right(ZplElement e) => e switch
    {
        ZplBox b => b.X + b.Width,
        ZplText t => t.X + Math.Max(t.BlockWidth, (int)(t.Text.Length * t.Height * 0.5)),
        _ => e.X + 200,
    };

    /// <summary>Lignes d'un texte (^FB : une par retour à la ligne) avec leur décalage horizontal selon l'alignement.</summary>
    private static IEnumerable<(string Text, int Dx, int Dy)> TextLines(ZplText t)
    {
        var lines = t.Text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var estimate = (int)(lines[i].Length * t.Height * 0.5);
            var dx = t.BlockWidth > 0 ? t.BlockAlign switch
            {
                'C' => Math.Max(0, (t.BlockWidth - estimate) / 2),
                'R' => Math.Max(0, t.BlockWidth - estimate),
                _ => 0,
            } : 0;
            yield return (lines[i], dx, (int)Math.Round(i * t.Height * 1.15));
        }
    }

    private static int Top(ZplElement e, int height) => e.Typeset ? Math.Max(0, e.Y - height) : e.Y;

    private static bool Filled(ZplBox b) => b.Thickness * 2 >= Math.Min(b.Width, b.Height);

    private static string Gs1Readable(ZplBarcode c) =>
        c.Gs1 && Gs1Parser.Parse(c.Content) is { Elements.Count: > 0 } g ? string.Concat(g.Elements.Select(x => $"({x.Ai}){x.Value}")) : c.Content;

    private static void Gs1Warning(ZplBarcode c, string language, List<string> warnings)
    {
        var message = $"{c.Symbology} en {language} : l'écriture des données GS1 (FNC1, séparateurs) varie selon les firmwares. Scanne l'étiquette imprimée au banc d'essai pour vérifier.";
        if (!warnings.Contains(message))
        {
            warnings.Add(message);
        }
    }

    // ------------------------------------------------------------------ TSPL

    private static string Tspl(ZplLabel label, int width, int height, int dpi, int copies, List<string> warnings)
    {
        string Mm(int dots) => (dots * 25.4 / dpi).ToString("0.#", CultureInfo.InvariantCulture);
        string Pt(int dots) => Math.Max(4, dots * 72.0 / dpi).ToString("0.#", CultureInfo.InvariantCulture);
        static string Q(string s) => "\"" + s.Replace("\"", "\\[\"]", StringComparison.Ordinal) + "\"";
        static int Rot(ZplOrientation o) => o switch { ZplOrientation.R => 90, ZplOrientation.I => 180, ZplOrientation.B => 270, _ => 0 };

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"SIZE {Mm(width)} mm,{Mm(height)} mm\r\nGAP 2 mm,0 mm\r\nDIRECTION 1\r\nREFERENCE 0,0\r\nCODEPAGE UTF-8\r\nCLS\r\n");
        foreach (var e in label.Elements)
        {
            switch (e)
            {
                case ZplBox b when Filled(b):
                    sb.Append(CultureInfo.InvariantCulture, $"{(b.Reverse ? "REVERSE" : "BAR")} {b.X},{b.Y},{b.Width},{b.Height}\r\n");
                    break;
                case ZplBox b:
                    sb.Append(CultureInfo.InvariantCulture, $"BOX {b.X},{b.Y},{b.X + b.Width},{b.Y + b.Height},{b.Thickness}\r\n");
                    break;
                case ZplText t:
                {
                    var top = Top(t, t.Height);
                    foreach (var (text, dx, dy) in TextLines(t))
                    {
                        if (t.BlockWidth > 0)
                        {
                            var align = t.BlockAlign switch { 'C' => 2, 'R' => 3, _ => 1 };
                            sb.Append(CultureInfo.InvariantCulture,
                                $"BLOCK {t.X},{top + dy},{t.BlockWidth},{(int)(t.Height * 1.2)},\"0\",{Rot(t.Orientation)},{Pt(t.Width)},{Pt(t.Height)},0,{align},{Q(text)}\r\n");
                        }
                        else
                        {
                            sb.Append(CultureInfo.InvariantCulture, $"TEXT {t.X + dx},{top + dy},\"0\",{Rot(t.Orientation)},{Pt(t.Width)},{Pt(t.Height)},{Q(text)}\r\n");
                        }

                        if (t.Reverse)
                        {
                            sb.Append(CultureInfo.InvariantCulture, $"REVERSE {t.X + dx},{top + dy},{Math.Max(t.BlockWidth, (int)(text.Length * t.Height * 0.55))},{t.Height}\r\n");
                        }
                    }

                    break;
                }

                case ZplBarcode c:
                {
                    var m = Math.Max(1, c.ModuleWidth);
                    var rot = Rot(c.Orientation);
                    var data = c.Gs1 ? Gs1Readable(c) : c.Content;
                    var type = c.Format switch
                    {
                        "Code128" => c.Gs1 ? "EAN128" : "128",
                        "Code39" => "39",
                        "Code93" => "93",
                        "EAN13" => "EAN13",
                        "EAN8" => "EAN8",
                        "UPCA" => "UPCA",
                        "UPCE" => "UPCE",
                        "ITF" => "25",
                        "Codabar" => "CODA",
                        _ => null,
                    };
                    if (type is not null)
                    {
                        var wide = c.Format is "Code39" or "ITF" or "Codabar" ? (int)Math.Round(m * Math.Max(2, c.Ratio)) : m;
                        sb.Append(CultureInfo.InvariantCulture, $"BARCODE {c.X},{Top(c, c.Height)},\"{type}\",{c.Height},{(c.Interpretation ? 2 : 0)},{rot},{m},{wide},{Q(data)}\r\n");
                        if (c.Gs1)
                        {
                            Gs1Warning(c, "TSPL", warnings);
                        }
                    }
                    else if (c.Format == "QRCode")
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"QRCODE {c.X},{c.Y},M,{m},A,{rot},{Q(c.Content)}\r\n");
                    }
                    else if (c.Format == "DataMatrix")
                    {
                        var size = LabelCode.DataMatrixModules(c.Content.Length) * m + 2 * m;
                        if (c.Gs1)
                        {
                            // c126 : « ~ » sert de caractère d'échappement ; ~1 = FNC1.
                            var gs1 = "~1" + c.Content.Replace("\u001D", "~1", StringComparison.Ordinal);
                            sb.Append(CultureInfo.InvariantCulture, $"DMATRIX {c.X},{c.Y},{size},{size},c126,x{m},{Q(gs1)}\r\n");
                            Gs1Warning(c, "TSPL", warnings);
                        }
                        else
                        {
                            sb.Append(CultureInfo.InvariantCulture, $"DMATRIX {c.X},{c.Y},{size},{size},x{m},{Q(c.Content)}\r\n");
                        }
                    }
                    else if (c.Format == "PDF417")
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"PDF417 {c.X},{c.Y},{width - c.X},{Math.Max(c.Height * 10, 80)},{rot},{Q(c.Content)}\r\n");
                    }
                    else if (c.Format == "Aztec")
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"AZTEC {c.X},{c.Y},{rot},{m},{Q(c.Content)}\r\n");
                    }
                    else
                    {
                        warnings.Add($"{c.Symbology} (ligne {c.Line}) n'a pas d'équivalent TSPL ici : champ omis.");
                    }

                    break;
                }
            }
        }

        sb.Append(CultureInfo.InvariantCulture, $"PRINT 1,{Math.Max(1, copies)}\r\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ EPL2

    private static string Epl(ZplLabel label, int width, int height, int dpi, int copies, List<string> warnings)
    {
        static string Q(string s) => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        static int Rot(ZplOrientation o) => o switch { ZplOrientation.R => 1, ZplOrientation.I => 2, ZplOrientation.B => 3, _ => 0 };
        var scale = dpi / 203.0;

        // Police interne (1 à 5) et multiplicateur les plus proches de la hauteur voulue.
        (int Font, int Mul) Font(int h)
        {
            (int Font, int H)[] fonts = [(1, 12), (2, 16), (3, 20), (4, 24), (5, 48)];
            var best = (Font: 3, Mul: 1, Gap: double.MaxValue);
            foreach (var (f, fh) in fonts)
            {
                for (var mul = 1; mul <= 6; mul++)
                {
                    var gap = Math.Abs(fh * scale * mul - h);
                    if (gap < best.Gap)
                    {
                        best = (f, mul, gap);
                    }
                }
            }

            return (best.Font, best.Mul);
        }

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"\r\nN\r\nq{width}\r\nQ{height},24\r\n");
        foreach (var e in label.Elements)
        {
            switch (e)
            {
                case ZplBox b when Filled(b):
                    sb.Append(CultureInfo.InvariantCulture, $"{(b.Reverse ? "LE" : b.White ? "LW" : "LO")}{b.X},{b.Y},{b.Width},{b.Height}\r\n");
                    break;
                case ZplBox b:
                    sb.Append(CultureInfo.InvariantCulture, $"X{b.X},{b.Y},{b.Thickness},{b.X + b.Width},{b.Y + b.Height}\r\n");
                    break;
                case ZplText t:
                {
                    var (font, mul) = Font(t.Height);
                    var top = Top(t, t.Height);
                    foreach (var (text, dx, dy) in TextLines(t))
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"A{t.X + dx},{top + dy},{Rot(t.Orientation)},{font},{mul},{mul},{(t.Reverse ? 'R' : 'N')},{Q(text)}\r\n");
                    }

                    break;
                }

                case ZplBarcode c:
                {
                    var m = Math.Max(1, c.ModuleWidth);
                    var type = c.Format switch
                    {
                        "Code128" => c.Gs1 ? "1E" : "1",
                        "Code39" => "3",
                        "Code93" => "9",
                        "EAN13" => "E30",
                        "EAN8" => "E80",
                        "UPCA" => "UA0",
                        "UPCE" => "UE0",
                        "ITF" => "2",
                        "Codabar" => "K",
                        _ => null,
                    };
                    if (type is not null)
                    {
                        // EAN / UPC : la clé de contrôle est calculée par l'imprimante.
                        // GS1 : données transmises (séparateur GS après les champs de longueur variable).
                        var data = c.Content;
                        data = c.Format switch
                        {
                            "EAN13" when data.Length == 13 => data[..12],
                            "EAN8" when data.Length == 8 => data[..7],
                            "UPCA" when data.Length == 12 => data[..11],
                            _ => data,
                        };
                        var wide = c.Format is "Code39" or "ITF" or "Codabar" ? (int)Math.Round(m * Math.Max(2, c.Ratio)) : m * 2;
                        sb.Append(CultureInfo.InvariantCulture, $"B{c.X},{Top(c, c.Height)},{Rot(c.Orientation)},{type},{m},{wide},{c.Height},{(c.Interpretation ? 'B' : 'N')},{Q(data)}\r\n");
                        if (c.Gs1)
                        {
                            Gs1Warning(c, "EPL2", warnings);
                        }
                    }
                    else if (c.Format is "QRCode" or "DataMatrix" or "PDF417" or "MaxiCode")
                    {
                        var code = c.Format switch { "QRCode" => "Q", "DataMatrix" => "D", "PDF417" => "P", _ => "M" };
                        var options = c.Format == "QRCode" ? $"m2,s{m},eM," : string.Empty;
                        sb.Append(CultureInfo.InvariantCulture, $"b{c.X},{c.Y},{code},{options}{Q(c.Content)}\r\n");
                        warnings.Add($"{c.Symbology} en EPL2 : pris en charge seulement par certaines imprimantes (LP/TLP 2844-Z, GK…). Vérifie à l'impression.");
                        if (c.Gs1)
                        {
                            Gs1Warning(c, "EPL2", warnings);
                        }
                    }
                    else
                    {
                        warnings.Add($"{c.Symbology} (ligne {c.Line}) n'a pas d'équivalent EPL2 ici : champ omis.");
                    }

                    break;
                }
            }
        }

        sb.Append(CultureInfo.InvariantCulture, $"P{Math.Max(1, copies)}\r\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ DPL

    private static string Dpl(ZplLabel label, int height, int dpi, int copies, List<string> warnings)
    {
        // Unités métriques (0,1 mm), origine en bas à gauche : la ligne (row) se compte depuis le bas de l'étiquette.
        int Tenth(int dots) => (int)Math.Round(dots * 254.0 / dpi);
        string F(int dots, int digits) => Math.Clamp(Tenth(dots), 0, (int)Math.Pow(10, digits) - 1).ToString("D" + digits, CultureInfo.InvariantCulture);
        string Row(int topDots, int heightDots) => F(Math.Max(0, height - topDots - heightDots), 4);
        static char Mult(int v) => v < 10 ? (char)('0' + Math.Max(1, v)) : (char)('A' + Math.Min(25, v - 10));
        static char Rot(ZplOrientation o) => o switch { ZplOrientation.B => '2', ZplOrientation.I => '3', ZplOrientation.R => '4', _ => '1' };

        var sb = new StringBuilder();
        sb.Append("\u0002L\r\nm\r\nD11\r\n");
        foreach (var e in label.Elements)
        {
            switch (e)
            {
                case ZplBox b when Filled(b):
                    sb.Append(CultureInfo.InvariantCulture, $"1X11000{Row(b.Y, b.Height)}{F(b.X, 4)}l{F(b.Width, 4)}{F(b.Height, 4)}\r\n");
                    break;
                case ZplBox b:
                    sb.Append(CultureInfo.InvariantCulture, $"1X11000{Row(b.Y, b.Height)}{F(b.X, 4)}b{F(b.Width, 4)}{F(b.Height, 4)}{F(b.Thickness, 4)}{F(b.Thickness, 4)}\r\n");
                    break;
                case ZplText t:
                {
                    // Police vectorielle 9 (CG Triumvirate) : taille en points dans le champ « eee » (A + 2 chiffres).
                    var pt = Math.Clamp((int)Math.Round(t.Height * 72.0 / dpi), 4, 99);
                    var top = Top(t, t.Height);
                    foreach (var (text, dx, dy) in TextLines(t))
                    {
                        sb.Append(CultureInfo.InvariantCulture, $"{Rot(t.Orientation)}911A{pt:D2}{Row(top + dy, t.Height)}{F(t.X + dx, 4)}{text}\r\n");
                    }

                    if (t.Reverse && !warnings.Contains("DPL : textes inversés (blanc sur noir) imprimés en noir ; à reprendre si besoin."))
                    {
                        warnings.Add("DPL : textes inversés (blanc sur noir) imprimés en noir ; à reprendre si besoin.");
                    }

                    break;
                }

                case ZplBarcode c:
                {
                    var m = Math.Clamp(c.ModuleWidth, 1, 9);
                    var id = c.Format switch
                    {
                        "Code39" => "A",
                        "UPCA" => "B",
                        "UPCE" => "C",
                        "ITF" => "D",
                        "Code128" => c.Gs1 ? "Q" : "E",
                        "EAN13" => "F",
                        "EAN8" => "G",
                        "Codabar" => "I",
                        "Code93" => "O",
                        "PDF417" => "Z",
                        "MaxiCode" => "U",
                        "QRCode" => "W1d",
                        "DataMatrix" => "W1c",
                        "Aztec" => "W1f",
                        _ => null,
                    };
                    if (id is null)
                    {
                        warnings.Add($"{c.Symbology} (ligne {c.Line}) n'a pas d'équivalent DPL ici : champ omis.");
                        break;
                    }

                    if (id.Length == 3)
                    {
                        // Code 2D : taille d'un module en 0,1 mm ; hauteur estimée pour placer le code depuis le bas.
                        var cell = Math.Clamp((int)Math.Round(Tenth(m) * 1.0), 1, 35);
                        var modules = c.Format == "QRCode" ? LabelCode.QrModules(c.Content.Length) : LabelCode.DataMatrixModules(c.Content.Length);
                        var sizeDots = modules * m;
                        var data = c.Gs1 && c.Format == "DataMatrix" ? "\u001D" + c.Content : c.Content;
                        sb.Append(CultureInfo.InvariantCulture, $"{Rot(c.Orientation)}{id}{Mult(cell)}{Mult(cell)}000{Row(c.Y, sizeDots)}{F(c.X, 4)}{data}\r\n");
                        if (c.Gs1)
                        {
                            Gs1Warning(c, "DPL", warnings);
                        }
                    }
                    else
                    {
                        var hri = c.Interpretation;
                        var code = hri ? id : id.ToLowerInvariant();
                        var wide = c.Format is "Code39" or "ITF" or "Codabar" ? Math.Min(9, (int)Math.Round(m * Math.Max(2, c.Ratio))) : m;
                        var data = c.Content;
                        var hriDots = hri ? (int)Math.Round(3 * dpi / 25.4) : 0;
                        sb.Append(CultureInfo.InvariantCulture, $"{Rot(c.Orientation)}{code}{Mult(wide)}{Mult(m)}{F(c.Height, 3)}{Row(Top(c, c.Height), c.Height + hriDots)}{F(c.X, 4)}{data}\r\n");
                        if (c.Gs1)
                        {
                            Gs1Warning(c, "DPL", warnings);
                        }
                    }

                    break;
                }
            }
        }

        sb.Append(CultureInfo.InvariantCulture, $"Q{Math.Clamp(copies, 1, 9999):D4}\r\nE\r\n");
        warnings.Add("DPL produit d'après le manuel Honeywell / Datamax (unités 0,1 mm, origine en bas à gauche) : valide l'étiquette sur l'imprimante.");
        return sb.ToString();
    }
}
