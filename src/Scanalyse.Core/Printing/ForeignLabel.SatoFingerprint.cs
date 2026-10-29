using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Scanalyse.Core.Content;
using Scanalyse.Core.Gs1;

namespace Scanalyse.Core.Printing;

/// <summary>
/// SBPL (SATO) et Fingerprint / Direct Protocol (Intermec, Honeywell) : mêmes éléments qu'une étiquette ZPL, pour l'aperçu
/// et la liste des codes-barres. Sources : guides de programmation SBPL de SATO (commandes de base) et manuels Direct
/// Protocol / Fingerprint d'Intermec. Polices approchées ; paramètres des codes 2D SBPL lus au mieux (contenu exact).
/// </summary>
public static partial class ForeignLabelReader
{
    /// <summary>Toutes les étiquettes d'un programme SBPL ou Fingerprint (une par bloc ESC A … ESC Z, ou par PRINTFEED).</summary>
    /// <param name="program">Texte du programme.</param>
    /// <param name="kind">SBPL ou Fingerprint.</param>
    /// <param name="dpi">Résolution de la tête.</param>
    /// <param name="codes">Reçoit les codes-barres de toutes les étiquettes, avec leur position écrite dans le programme.</param>
    internal static IReadOnlyList<ZplLabel> ReadAll(string program, ProgramKind kind, int dpi, List<ProgramBarcode>? codes = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        dpi = dpi is >= 100 and <= 1200 ? dpi : 203;
        return kind switch
        {
            ProgramKind.Sbpl => ReadSbpl(program, dpi, codes),
            ProgramKind.Fingerprint => ReadFingerprint(program, dpi, codes),
            _ => [],
        };
    }

    // ------------------------------------------------------------------ SBPL (SATO)

    private sealed class SbplPending
    {
        public required string Name { get; init; }
        public required string Format { get; init; }
        public required string Command { get; init; }
        public bool Gs1 { get; init; }
        public int Module { get; init; }
        public int X { get; init; }
        public int Y { get; init; }
        public ZplOrientation O { get; init; }
        public int Line { get; init; }
    }

    private static List<ZplLabel> ReadSbpl(string program, int dpi, List<ProgramBarcode>? codes)
    {
        // Notation texte « <ESC> » (documentations, fichiers d'exemple) ou vrai caractère ESC ; STX / ETX d'encadrement ignorés.
        var text = program.Replace("<ESC>", "\u001B", StringComparison.OrdinalIgnoreCase)
            .Replace("<STX>", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("<ETX>", string.Empty, StringComparison.OrdinalIgnoreCase);
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lineStarts.Add(i + 1);
            }
        }

        int LineAt(int pos)
        {
            var k = lineStarts.BinarySearch(pos);
            return (k >= 0 ? k : ~k - 1) + 1;
        }

        var labels = new List<ZplLabel>();
        var elements = new List<ZplElement>();
        int? width = null, length = null;
        int h = 0, v = 0, copies = 1, enlargeH = 1, enlargeV = 1;
        var o = ZplOrientation.N;
        var started = false;
        var reverse = false;
        (int W, int H) outline = (0, 0);
        SbplPending? pending = null;
        var scale = dpi / 203.0;

        void AddCode(ZplBarcode b)
        {
            elements.Add(b);
            codes?.Add(new ProgramBarcode(b.Line, b.Command, b.Symbology, b.X, b.Y, b.FieldData, b.Gs1));
        }

        void Close()
        {
            if (started && elements.Count > 0)
            {
                labels.Add(new ZplLabel(width, length, false, copies, false, [.. elements],
                    [new Alert(Severity.Info, "Aperçu SBPL : polices approchées ; taille des modules des codes 2D estimée (le contenu des codes est exact).")], 1));
            }

            elements.Clear();
            copies = 1;
            enlargeH = enlargeV = 1;
            o = ZplOrientation.N;
            pending = null;
        }

        var pos = text.IndexOf('\u001B', StringComparison.Ordinal);
        while (pos >= 0)
        {
            var next = text.IndexOf('\u001B', pos + 1);
            var seg = (next < 0 ? text[(pos + 1)..] : text[(pos + 1)..next]).TrimEnd('\r', '\n', '\u0002', '\u0003');
            var line = LineAt(pos);
            pos = next;
            if (seg.Length == 0)
            {
                continue;
            }

            var (cmd, arg) = SbplSplit(seg);
            switch (cmd)
            {
                case "A":
                    Close();
                    started = true;
                    break;
                case "Z":
                    Close();
                    started = false;
                    break;
                case "A1":
                {
                    // « aaaabbbb » (hauteur puis largeur) ou « V…H… ».
                    var vh = SbplVh().Match(arg);
                    if (vh.Success)
                    {
                        length = int.Parse(vh.Groups[1].Value, CultureInfo.InvariantCulture);
                        width = int.Parse(vh.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                    else if (arg.Length >= 8 && int.TryParse(arg.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var a)
                        && int.TryParse(arg.AsSpan(4, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                    {
                        length = a;
                        width = b;
                    }

                    break;
                }

                case "H":
                    h = Num(arg);
                    break;
                case "V":
                    v = Num(arg);
                    break;
                case "%":
                    // Rotation dans le sens inverse des aiguilles d'une montre (guide SBPL).
                    o = Num(arg) switch { 1 => ZplOrientation.B, 2 => ZplOrientation.I, 3 => ZplOrientation.R, _ => ZplOrientation.N };
                    break;
                case "L" when arg.Length >= 4:
                    enlargeH = Math.Clamp(Num(arg[..2]), 1, 12);
                    enlargeV = Math.Clamp(Num(arg[2..4]), 1, 12);
                    break;
                case "Q":
                    copies = Math.Max(1, Num(arg));
                    break;
                case "U" or "S" or "M" or "XU" or "XS" or "XM" or "XL" or "XB" or "WB" or "WL" or "OA" or "OB":
                {
                    var (cw, ch) = cmd switch
                    {
                        "U" or "XU" => (5, 9),
                        "S" => (8, 15),
                        "M" => (13, 20),
                        "XS" => (17, 17),
                        "XM" => (24, 24),
                        "WB" => (18, 30),
                        "WL" => (28, 52),
                        "OA" => (15, 22),
                        "OB" => (20, 24),
                        _ => (48, 48),
                    };

                    // XL, XB, WB, WL : premier caractère = lissage (0 ou 1).
                    var data = cmd is "XL" or "XB" or "WB" or "WL" && arg.Length > 0 && arg[0] is '0' or '1' ? arg[1..] : arg;
                    elements.Add(new ZplText(h, v, false, o, line, "A", (int)Math.Round(ch * enlargeV * scale), (int)Math.Round(cw * enlargeH * scale), data, reverse));
                    break;
                }

                case "$":
                {
                    // « a,bbb,ccc,d » : police vectorielle, largeur et hauteur en points.
                    var a = Split(arg);
                    outline = (Math.Max(8, Int(a, 1)), Math.Max(8, Int(a, 2)));
                    break;
                }

                case "$=":
                    elements.Add(new ZplText(h, v, false, o, line, "0", outline.H > 0 ? outline.H : 50, outline.W > 0 ? outline.W : 50, arg, reverse));
                    break;
                case "B" or "D" or "BD" when arg.Length >= 7:
                {
                    // « abbccc » : type, largeur de la barre étroite, hauteur, puis les données.
                    var type = arg[0];
                    var narrow = Num(arg[1..3]);
                    var height = Num(arg[3..6]);
                    var data = arg[6..];
                    var (name, format) = SbplBarcode(type, data);
                    if (format.Length == 0 && name.Length == 0)
                    {
                        break;
                    }

                    var hri = cmd != "B" && type is '3' or '4' or 'H';
                    AddCode(SbplCode(h, v, o, line, cmd + type, name, format, data, narrow, height, hri));
                    break;
                }

                case "BG" when arg.Length >= 5:
                    AddCode(SbplCode(h, v, o, line, "BG", "Code 128", "Code128", arg[5..], Num(arg[..2]), Num(arg[2..5]), false));
                    break;
                case "BI" when arg.Length >= 6:
                {
                    // UCC/EAN-128 : 17 chiffres, l'imprimante ajoute l'identifiant (00) et la clé (SSCC).
                    var data = arg[6..].Trim();
                    var digits = new string([.. data.Where(char.IsAsciiDigit)]);
                    var ok = digits.Length is 17 or 18 && digits.Length == data.Length;
                    var sscc = !ok ? data
                        : digits.Length == 17 ? "(00)" + digits + CheckDigit.Compute(digits).ToString(CultureInfo.InvariantCulture)
                        : "(00)" + digits;
                    var bi = Barcode(h, v, o, line, "BI", "GS1-128 (SSCC)", "Code128", sscc, ok, Math.Max(1, Num(arg[..2])), Math.Max(1, Num(arg[2..5])), arg[5] is '1' or '2');
                    AddCode(ok ? bi : bi with
                    {
                        Notes = [new Alert(Severity.Warning, $"SSCC SBPL (BI) : 17 chiffres attendus (sans la clé), {data.Length} caractère(s) reçu(s). L'imprimante refusera ou imprimera un code faux.")],
                    });
                    break;
                }

                case "BC" when arg.Length >= 7:
                    AddCode(SbplCode(h, v, o, line, "BC", "Code 93", "Code93", arg[7..], Num(arg[..2]), Num(arg[2..5]), false));
                    break;
                case "2D10" or "2D11" or "2D20" or "2D30" or "2D31" or "2D50" or "2D51":
                {
                    // Paramètres écrits juste après la commande ou après une virgule (« 2D30,M,06,… »).
                    var a = Split(arg.TrimStart(','));
                    var (name, format) = cmd switch
                    {
                        "2D10" => ("PDF417", "PDF417"),
                        "2D11" => ("MicroPDF417", "MicroPDF417"),
                        "2D20" => ("MaxiCode", "MaxiCode"),
                        "2D30" => ("QR Code", "QRCode"),
                        "2D31" => ("Micro QR Code", "MicroQRCode"),
                        "2D51" => ("GS1 DataMatrix", "DataMatrix"),
                        _ => ("Data Matrix", "DataMatrix"),
                    };

                    // Taille de cellule : 2e paramètre du QR (« niveau, cellule, … »), 1er des autres.
                    var module = cmd is "2D30" or "2D31" ? Int(a, 1) : Int(a, 0);
                    pending = new SbplPending
                    {
                        Name = name,
                        Format = format,
                        Command = cmd,
                        Gs1 = cmd == "2D51",
                        Module = module is >= 1 and <= 32 ? (int)Math.Round(module * scale) : (int)Math.Round(5 * scale),
                        X = h,
                        Y = v,
                        O = o,
                        Line = line,
                    };
                    break;
                }

                case "DN" or "DS" when pending is not null:
                {
                    var data = SbplData(cmd, arg);
                    var gs1 = pending.Gs1 || data.StartsWith('(');
                    if (pending.Gs1 && !data.StartsWith('('))
                    {
                        data = data.TrimStart('\u001D');
                    }

                    AddCode(Barcode(pending.X, pending.Y, pending.O, pending.Line, pending.Command, pending.Name, pending.Format, data, gs1, Math.Max(1, pending.Module), 0, false));
                    pending = null;
                    break;
                }

                case "FW":
                {
                    var frame = SbplFrame().Match(arg);
                    var rule = SbplRule().Match(arg);
                    if (frame.Success)
                    {
                        var tv = int.Parse(frame.Groups[1].Value, CultureInfo.InvariantCulture);
                        var th = int.Parse(frame.Groups[2].Value, CultureInfo.InvariantCulture);
                        elements.Add(new ZplBox(h, v, false, line, int.Parse(frame.Groups[4].Value, CultureInfo.InvariantCulture),
                            int.Parse(frame.Groups[3].Value, CultureInfo.InvariantCulture), Math.Max(1, Math.Max(tv, th)), false, 0, false));
                    }
                    else if (rule.Success)
                    {
                        var t = int.Parse(rule.Groups[1].Value, CultureInfo.InvariantCulture);
                        var len = int.Parse(rule.Groups[3].Value, CultureInfo.InvariantCulture);
                        var horizontal = rule.Groups[2].Value is "H" or "h";
                        elements.Add(new ZplBox(h, v, false, line, horizontal ? len : t, horizontal ? t : len, Math.Max(1, t), false, 0, false));
                    }

                    break;
                }

                case "(":
                {
                    var a = Split(arg);
                    int w = Int(a, 0), hh = Int(a, 1);
                    if (w > 0 && hh > 0)
                    {
                        elements.Add(new ZplBox(h, v, false, line, w, hh, Math.Min(w, hh), false, 0, true));
                    }

                    break;
                }
            }
        }

        // Programme sans ESC Z final : l'étiquette commencée est gardée.
        Close();
        return labels;
    }

    /// <summary>Sépare la commande SBPL de ses paramètres (commandes à deux lettres ou plus reconnues en premier).</summary>
    private static (string Cmd, string Arg) SbplSplit(string seg)
    {
        foreach (var c in SbplCommands)
        {
            if (seg.StartsWith(c, StringComparison.Ordinal))
            {
                return (c, seg[c.Length..]);
            }
        }

        return (seg[..1], seg[1..]);
    }

    private static readonly string[] SbplCommands =
    [
        "2D10", "2D11", "2D20", "2D30", "2D31", "2D50", "2D51",
        "A1", "A3", "BD", "BG", "BI", "BC", "BQ", "DN", "DS", "FW", "XU", "XS", "XM", "XL", "XB", "WB", "WL", "OA", "OB", "PS", "PR", "$=",
    ];

    /// <summary>Données d'un code 2D : « DN » longueur sur 4 chiffres, virgule, données ; « DS » mode, virgule, données.</summary>
    private static string SbplData(string cmd, string arg)
    {
        var comma = arg.IndexOf(',', StringComparison.Ordinal);
        if (cmd == "DN" && comma == 4 && int.TryParse(arg.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var n))
        {
            var rest = arg[5..];
            return n > 0 && n <= rest.Length ? rest[..n] : rest;
        }

        return comma is > 0 and <= 2 ? arg[(comma + 1)..] : arg;
    }

    private static (string Name, string Format) SbplBarcode(char type, string data) => type switch
    {
        '0' => ("Codabar (NW-7)", "Codabar"),
        '1' => ("Code 39", "Code39"),
        '2' => ("ITF", "ITF"),
        '3' => data.Trim().Length == 11 ? ("UPC-A", "UPCA") : ("EAN-13", "EAN13"),
        '4' => ("EAN-8", "EAN8"),
        '5' => ("Industrial 2 of 5", string.Empty),
        '6' => ("Matrix 2 of 5", string.Empty),
        'A' => ("MSI", string.Empty),
        'C' => ("Code 93", "Code93"),
        'E' => ("UPC-E", "UPCE"),
        'F' => ("Bookland", "EAN13"),
        'G' => ("Code 128", "Code128"),
        'H' => ("UPC-A", "UPCA"),
        'I' => ("GS1-128", "Code128"),
        'P' => ("POSTNET", string.Empty),
        _ => (string.Empty, string.Empty),
    };

    /// <summary>
    /// Code-barres SBPL 1D : astérisques du Code 39 retirés (la douchette ne les transmet pas) ; Code 128 : codes de départ
    /// « &gt;G », « &gt;H », « &gt;I » retirés et « &gt;F » (FNC1) traduit, ce qui reconnaît les données GS1.
    /// </summary>
    private static ZplBarcode SbplCode(int x, int y, ZplOrientation o, int line, string cmd, string name, string format, string data, int narrow, int height, bool hri)
    {
        var gs1 = false;
        if (format == "Code39" && data.Length >= 2 && data[0] == '*' && data[^1] == '*')
        {
            data = data[1..^1];
        }
        else if (format == "Code128")
        {
            data = data.Replace(">G", string.Empty, StringComparison.Ordinal).Replace(">H", string.Empty, StringComparison.Ordinal).Replace(">I", string.Empty, StringComparison.Ordinal);
            if (data.StartsWith(">F", StringComparison.Ordinal))
            {
                gs1 = true;
                data = data[2..];
            }

            data = data.Replace(">F", "\u001D", StringComparison.Ordinal);
            gs1 |= name == "GS1-128" || data.StartsWith('(');
            if (gs1 && name == "Code 128")
            {
                name = "GS1-128";
            }
        }

        return Barcode(x, y, o, line, cmd, name, format, data, gs1, Math.Max(1, narrow), Math.Max(1, height), hri);
    }

    private static int Num(string s)
    {
        var digits = new string([.. s.Trim().TakeWhile(c => char.IsAsciiDigit(c) || c is '+' or '-')]);
        return int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    [GeneratedRegex(@"^V0*(\d+)H0*(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SbplVh();

    [GeneratedRegex(@"^(\d{2})(\d{2})V(\d+)H(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SbplFrame();

    [GeneratedRegex(@"^(\d{2,3})([HVhv])(\d+)")]
    private static partial Regex SbplRule();

    // ------------------------------------------------------------------ Fingerprint / Direct Protocol

    private static List<ZplLabel> ReadFingerprint(string program, int dpi, List<ProgramBarcode>? codes)
    {
        var labels = new List<ZplLabel>();
        var items = new List<(int Line, int X, int Bottom, int Height, Func<int, int, ZplElement> Make)>();
        int x = 0, y = 0, dir = 1, anchor = 1;
        int barHeight = 100, barMag = 2;
        double fontPt = 12;
        var barType = "CODE39";
        var hri = false;
        var inverse = false;
        var fontName = string.Empty;

        void Feed(int copies)
        {
            if (items.Count > 0)
            {
                // Origine au premier point de la tête, Y le long du papier : vu de face, Y monte depuis le bas de l'étiquette.
                var height = items.Max(i => i.Bottom + i.Height) + (int)Math.Round(dpi / 25.4 * 3);
                var elements = items.Select(i => i.Make(i.X, height - i.Bottom)).ToList();
                labels.Add(new ZplLabel(null, height, false, Math.Max(1, copies), true, elements,
                    [new Alert(Severity.Info, "Fingerprint : la position « y » se compte depuis le bas de l'étiquette (vue de face) ; polices et ancrages approchés.")], 1));
            }

            items.Clear();
            dir = 1;
            anchor = 1;
            barHeight = 100;
            barMag = 2;
            hri = false;
            inverse = false;
        }

        // Point d'ancrage (ALIGN) : disposition du pavé numérique, 1 = bas gauche, 5 = centre, 9 = haut droit.
        int Bottom(int fieldHeight, bool text) => dir != 1 ? y : anchor switch
        {
            >= 7 and <= 9 => y - fieldHeight,
            >= 4 and <= 6 => text ? y - (int)Math.Round(fieldHeight * 0.2) : y - (fieldHeight / 2),
            _ => y,
        };

        int Left(int fieldWidth) => dir != 1 ? x : anchor switch
        {
            2 or 5 or 8 => x - (fieldWidth / 2),
            3 or 6 or 9 => x - fieldWidth,
            _ => x,
        };

        ZplOrientation Orientation() => dir switch { 2 => ZplOrientation.R, 3 => ZplOrientation.I, 4 => ZplOrientation.B, _ => ZplOrientation.N };

        foreach (var (n, raw) in Lines(program))
        {
            foreach (var st in FingerprintStatements(raw))
            {
                var m = FingerprintStatement().Match(st);
                if (!m.Success)
                {
                    continue;
                }

                var key = m.Groups[1].Value.ToUpperInvariant();
                var arg = m.Groups[2].Value.Trim();
                var a = Split(arg);
                switch (key)
                {
                    case "PP" or "PRPOS":
                        x = Int(a, 0);
                        y = Int(a, 1);
                        break;
                    case "DIR" or "DIRECTION":
                        dir = Math.Clamp(Int(a, 0), 1, 4);
                        break;
                    case "AN" or "ALIGN":
                        anchor = Math.Clamp(Int(a, 0), 1, 9);
                        break;
                    case "FT" or "FONT":
                        fontName = FingerprintString(Arg(a, 0));
                        fontPt = a.Count > 1 && Dbl(a, 1) > 0 ? Dbl(a, 1) : 12;
                        break;
                    case "II" or "INVIMAGE":
                        inverse = true;
                        break;
                    case "NI" or "NORIMAGE":
                        inverse = false;
                        break;
                    case "BT" or "BARTYPE":
                        barType = FingerprintString(Arg(a, 0)).ToUpperInvariant();
                        break;
                    case "BH" or "BARHEIGHT":
                        barHeight = Math.Max(1, Int(a, 0));
                        break;
                    case "BM" or "BARMAG":
                        barMag = Math.Max(1, Int(a, 0));
                        break;
                    case "BF" or "BARFONT":
                        hri = !arg.Contains("OFF", StringComparison.OrdinalIgnoreCase);
                        break;
                    case "BARSET":
                        barType = FingerprintString(Arg(a, 0)).ToUpperInvariant();
                        barMag = a.Count > 2 && Int(a, 2) > 0 ? Int(a, 2) : barMag;
                        break;
                    case "PT" or "PRTXT":
                    {
                        var text = FingerprintString(arg);
                        var hDots = (int)Math.Round(fontPt * dpi / 72.0);
                        var wDots = (int)Math.Round(hDots * 0.55);
                        int bx = Left(wDots * Math.Max(1, text.Length)), bottom = Bottom(hDots, true);
                        var o = Orientation();
                        var rev = inverse;
                        var font = fontName.Length > 0 && char.IsAsciiDigit(fontName[^1]) && fontName.Contains("SW", StringComparison.OrdinalIgnoreCase) ? "A" : "0";
                        items.Add((n, bx, bottom, hDots, (xx, yy) => new ZplText(xx, yy, false, o, n, font, hDots, wDots, text, rev)));
                        break;
                    }

                    case "PB" or "PRBAR":
                    {
                        var data = FingerprintString(arg);
                        var (name, format, gs1) = FingerprintBarcode(barType);
                        gs1 |= data.StartsWith('(');
                        var twoD = format is "QRCode" or "DataMatrix" or "Aztec" or "MaxiCode" or "PDF417";
                        var fieldH = twoD ? barMag * 25 : barHeight + (hri ? 25 : 0);
                        int bx = Left(twoD ? fieldH : 0), bottom = Bottom(fieldH, false);
                        var o = Orientation();
                        int mag = barMag, bh = barHeight;
                        var withHri = hri;
                        var bt = barType;
                        codes?.Add(new ProgramBarcode(n, bt, name, x, y, data, gs1));
                        items.Add((n, bx, bottom, fieldH, (xx, yy) => Barcode(xx, yy, o, n, bt, name, format, data, gs1, mag, twoD ? 0 : bh, withHri)));
                        break;
                    }

                    case "PL" or "PRLINE":
                    {
                        int len = Int(a, 0), t = Math.Max(1, Int(a, 1));
                        var vertical = dir is 2 or 4;
                        int w = vertical ? t : len, hh = vertical ? len : t;
                        items.Add((n, x, y, hh, (xx, yy) => new ZplBox(xx, yy, false, n, w, hh, Math.Max(1, Math.Min(w, hh)), false, 0, false)));
                        break;
                    }

                    case "PX" or "PRBOX":
                    {
                        // Hauteur perpendiculaire au sens d'impression, largeur dans le sens d'impression.
                        int bh = Int(a, 0), bw = Int(a, 1), t = Math.Max(1, Int(a, 2));
                        var vertical = dir is 2 or 4;
                        int w = vertical ? bh : bw, hh = vertical ? bw : bh;
                        items.Add((n, x, y, hh, (xx, yy) => new ZplBox(xx, yy, false, n, w, hh, t, false, 0, false)));
                        break;
                    }

                    case "PF" or "PRINTFEED":
                        Feed(a.Count > 0 && Int(a, 0) > 0 ? Int(a, 0) : 1);
                        break;
                }
            }
        }

        Feed(1);
        return labels;
    }

    /// <summary>Instructions d'une ligne Fingerprint : numéro de ligne BASIC retiré, séparées par « : » hors guillemets.</summary>
    private static IEnumerable<string> FingerprintStatements(string line)
    {
        var s = LineNumber().Replace(line, string.Empty);
        var cur = new StringBuilder();
        var quoted = false;
        foreach (var c in s)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }

            if (c == ':' && !quoted)
            {
                yield return cur.ToString().Trim();
                cur.Clear();
            }
            else
            {
                cur.Append(c);
            }
        }

        if (cur.Length > 0)
        {
            yield return cur.ToString().Trim();
        }
    }

    /// <summary>
    /// Expression chaîne Fingerprint : morceaux entre guillemets et CHR$(n) reliés par « + » ; CHR$(128) = FNC1 (séparateur GS1).
    /// Les variables (NOM$) sont montrées entre accolades.
    /// </summary>
    private static string FingerprintString(string expr)
    {
        var sb = new StringBuilder();
        foreach (Match m in FingerprintPart().Matches(expr))
        {
            if (m.Groups[1].Success)
            {
                sb.Append(m.Groups[1].Value);
            }
            else if (m.Groups[2].Success && int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                sb.Append(code == 128 ? '\u001D' : (char)code);
            }
            else if (m.Groups[3].Success)
            {
                sb.Append('{').Append(m.Groups[3].Value).Append('}');
            }
        }

        return sb.ToString();
    }

    private static (string Name, string Format, bool Gs1) FingerprintBarcode(string type) => type switch
    {
        "CODE128" or "CODE128A" or "CODE128B" or "CODE128C" => ("Code 128", "Code128", false),
        "EAN128" or "UCC128" or "GS1-128" => ("GS1-128", "Code128", true),
        "CODE39" or "CODE39A" or "CODE39C" => ("Code 39", "Code39", false),
        "CODE93" => ("Code 93", "Code93", false),
        "EAN13" => ("EAN-13", "EAN13", false),
        "EAN8" => ("EAN-8", "EAN8", false),
        "UPCA" => ("UPC-A", "UPCA", false),
        "UPCE" => ("UPC-E", "UPCE", false),
        "INT2OF5" or "INT2OF5C" or "I2OF5A" => ("ITF", "ITF", false),
        "DUN" => ("ITF-14", "ITF", false),
        "CODABAR" => ("Codabar", "Codabar", false),
        "QRCODE" => ("QR Code", "QRCode", false),
        "DATAMATRIX" => ("Data Matrix", "DataMatrix", false),
        "PDF417" => ("PDF417", "PDF417", false),
        "AZTEC" => ("Aztec", "Aztec", false),
        "MAXICODE" => ("MaxiCode", "MaxiCode", false),
        _ => (type.Length > 0 ? type : "Code-barres", string.Empty, false),
    };

    [GeneratedRegex(@"^\s*\d+\s+")]
    private static partial Regex LineNumber();

    [GeneratedRegex(@"^([A-Za-z]+)\b\s*(.*)$")]
    private static partial Regex FingerprintStatement();

    [GeneratedRegex(@"""([^""]*)""|CHR\$\s*\(\s*(\d+)\s*\)|([A-Za-z][A-Za-z0-9_]*\$)", RegexOptions.IgnoreCase)]
    private static partial Regex FingerprintPart();
}
