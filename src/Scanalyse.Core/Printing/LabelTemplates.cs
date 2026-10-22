using System.Globalization;
using System.Text;
using Scanalyse.Core.Gs1;

namespace Scanalyse.Core.Printing;

/// <summary>Nature d'un champ de modèle (contrôle et mise en forme).</summary>
public enum TemplateFieldKind
{
    Text,

    /// <summary>Plusieurs lignes (adresse).</summary>
    MultiLine,

    /// <summary>Chiffres uniquement.</summary>
    Digits,

    /// <summary>Date jj/mm/aaaa.</summary>
    Date,

    /// <summary>Nombre décimal (poids en kg…).</summary>
    Decimal,
}

/// <summary>Champ à remplir dans un modèle d'étiquette.</summary>
/// <param name="Key">Identifiant.</param>
/// <param name="Label">Libellé affiché.</param>
/// <param name="Default">Valeur d'exemple.</param>
/// <param name="Kind">Nature.</param>
/// <param name="Hint">Aide (format attendu…).</param>
/// <param name="Required">Obligatoire.</param>
public sealed record TemplateField(string Key, string Label, string Default, TemplateFieldKind Kind = TemplateFieldKind.Text, string Hint = "", bool Required = true);

/// <summary>Programme produit par un modèle.</summary>
/// <param name="Zpl">Programme ZPL complet.</param>
/// <param name="Warnings">Corrections et points d'attention (clé recalculée, champ vide…).</param>
public sealed record TemplateOutput(string Zpl, IReadOnlyList<string> Warnings);

/// <summary>Modèle d'étiquette prêt à l'emploi.</summary>
/// <param name="Id">Identifiant.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="Description">Usage.</param>
/// <param name="WidthMm">Largeur de l'étiquette.</param>
/// <param name="HeightMm">Hauteur de l'étiquette.</param>
/// <param name="Fields">Champs à remplir.</param>
public sealed record LabelTemplate(string Id, string Name, string Description, double WidthMm, double HeightMm, IReadOnlyList<TemplateField> Fields);

/// <summary>
/// Modèles d'étiquettes courantes (palette GS1, colis, produit, emplacement, inventaire), produits en ZPL II :
/// le programme est ensuite affiché, modifiable, prévisualisé et envoyé comme un programme client.
/// </summary>
public static class LabelTemplates
{
    public static IReadOnlyList<LabelTemplate> All { get; } =
    [
        new("pallet", "Palette logistique GS1 (SSCC)", "Étiquette palette normalisée GS1 : SSCC, GTIN contenu, quantité, lot, DDM, poids, en trois GS1-128.", 100, 150,
        [
            new("from", "Expéditeur", "SCANALYSE SAS\n5 rue des Frères Lumière\n44119 TREILLIÈRES", TemplateFieldKind.MultiLine),
            new("to", "Destinataire", "ENTREPÔT CLIENT\n12 avenue de la Gare\n69000 LYON", TemplateFieldKind.MultiLine),
            new("sscc", "SSCC (17 ou 18 chiffres)", "33109300169041805", TemplateFieldKind.Digits, "Extension + préfixe GS1 + n° de série ; la clé est calculée si tu n'en mets que 17."),
            new("gtin", "GTIN des articles contenus (02)", "03453120000011", TemplateFieldKind.Digits, "13 ou 14 chiffres."),
            new("qty", "Quantité d'articles (37)", "120", TemplateFieldKind.Digits),
            new("lot", "Lot (10)", "LOT42", Required: false),
            new("bestBefore", "Date de durabilité minimale (15)", "31/12/2027", TemplateFieldKind.Date, "jj/mm/aaaa", Required: false),
            new("weight", "Poids net en kg (3102)", "512,40", TemplateFieldKind.Decimal, Required: false),
        ]),
        new("parcel", "Colis transporteur", "Expéditeur, destinataire en grand, n° de suivi en Code 128, poids, référence, colis n/N.", 100, 150,
        [
            new("from", "Expéditeur", "SCANALYSE SAS\n5 rue des Frères Lumière\n44119 TREILLIÈRES", TemplateFieldKind.MultiLine),
            new("to", "Destinataire", "M. DUPONT Jean\n8 place du Commerce\n75015 PARIS", TemplateFieldKind.MultiLine),
            new("tracking", "N° de suivi", "1Z999AA10123456784"),
            new("service", "Service", "EXPRESS 24H", Required: false),
            new("weight", "Poids (kg)", "2,5", TemplateFieldKind.Decimal, Required: false),
            new("reference", "Référence commande", "CMD-2026-0042", Required: false),
            new("parcel", "Colis (n/N)", "1/1", Required: false),
        ]),
        new("product", "Produit (GTIN, lot, date)", "Désignation, EAN-13 de vente et GS1 DataMatrix avec GTIN, lot et date limite.", 100, 50,
        [
            new("name", "Désignation", "Douchette 2D filaire — kit USB"),
            new("reference", "Référence interne", "REF-DS2208", Required: false),
            new("gtin", "GTIN (EAN-13)", "3453120000011", TemplateFieldKind.Digits, "12 ou 13 chiffres."),
            new("lot", "Lot", "LOT42", Required: false),
            new("expiry", "Date limite (17)", "31/12/2027", TemplateFieldKind.Date, "jj/mm/aaaa", Required: false),
        ]),
        new("location", "Emplacement de stock", "Code emplacement en très grand et en Code 128, pour racks et allées.", 100, 50,
        [
            new("code", "Code emplacement", "A-12-03-B"),
            new("detail", "Description", "Allée A · Travée 12 · Niveau 3", Required: false),
        ]),
        new("asset", "Inventaire / matériel", "N° d'inventaire en QR Code et Code 128, désignation et propriétaire.", 60, 40,
        [
            new("number", "N° d'inventaire", "INV-004217"),
            new("name", "Désignation", "Terminal TC53 — atelier SAV", Required: false),
            new("owner", "Propriétaire", "Service SAV", Required: false),
        ]),
    ];

    public static LabelTemplate? Get(string id) => All.FirstOrDefault(t => t.Id == id);

    /// <summary>Valeurs d'exemple d'un modèle.</summary>
    public static Dictionary<string, string> Defaults(LabelTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Fields.ToDictionary(f => f.Key, f => f.Default);
    }

    public static TemplateOutput Build(LabelTemplate template, IReadOnlyDictionary<string, string> values, int dpi = 203, int copies = 1)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);
        var z = new Zpl(template.WidthMm, template.HeightMm, dpi);
        var w = new List<string>();
        string V(string key) => values.TryGetValue(key, out var v) ? v.Trim() : string.Empty;
        foreach (var f in template.Fields.Where(f => f.Required && V(f.Key).Length == 0))
        {
            w.Add($"« {f.Label} » est vide.");
        }

        switch (template.Id)
        {
            case "pallet":
                Pallet(z, V, w);
                break;
            case "parcel":
                Parcel(z, V);
                break;
            case "product":
                Product(z, V, w);
                break;
            case "location":
                Location(z, V);
                break;
            default:
                Asset(z, V);
                break;
        }

        return new TemplateOutput(z.End(copies), w);
    }

    // ------------------------------------------------------------------ Modèles

    private static void Pallet(Zpl z, Func<string, string> v, List<string> w)
    {
        var sscc = Key(Digits(v("sscc")), 18, "SSCC", w);
        var gtin = Key(Digits(v("gtin")).PadLeft(14, '0'), 14, "GTIN", w);
        var qty = Digits(v("qty"));
        var lot = v("lot");
        var ddm = Date(v("bestBefore"), "DDM", w);
        var weight = Decimal(v("weight"), 2, 6, "Poids", w);

        z.Box(0, 0, 100, 150, 0.6);
        z.Text(4, 3, 2.6, "EXPÉDITEUR");
        z.Block(4, 7, 44, 3.4, v("from"), 4);
        z.Text(52, 3, 2.6, "DESTINATAIRE");
        z.Block(52, 7, 44, 3.4, v("to"), 4);
        z.Line(0, 26, 100, 0.5);
        z.Text(4, 28, 2.6, "SSCC");
        z.Text(4, 32, 6, Spaced(sscc));
        z.Line(0, 40, 100, 0.5);
        var rows = new List<(string Label, string Value)>
        {
            ("CONTENU (GTIN)", gtin),
            ("QUANTITÉ", qty),
        };
        if (lot.Length > 0)
        {
            rows.Add(("LOT", lot));
        }

        if (ddm.Length > 0)
        {
            rows.Add(("DDM", v("bestBefore")));
        }

        if (weight.Length > 0)
        {
            rows.Add(("POIDS NET", v("weight") + " kg"));
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var x = i % 2 == 0 ? 4 : 52;
            var y = 42 + i / 2 * 11;
            z.Text(x, y, 2.4, rows[i].Label);
            z.Text(x, y + 3.5, 4.5, rows[i].Value);
        }

        z.Line(0, 76, 100, 0.5);
        var second = (lot.Length > 0 ? "(10)" + lot : string.Empty) + (ddm.Length > 0 ? "(15)" + ddm : string.Empty) + (weight.Length > 0 ? "(3102)" + weight : string.Empty);
        z.Gs1_128(4, 79, 13, $"(02){gtin}(37){qty}", 92, w);
        if (second.Length > 0)
        {
            z.Gs1_128(4, 102, 13, second, 92, w);
        }

        z.Gs1_128(4, 125, 15, $"(00){sscc}", 92, w);
        Check($"(02){gtin}(37){qty}{second}(00){sscc}", w);
    }

    private static void Parcel(Zpl z, Func<string, string> v)
    {
        z.Box(0, 0, 100, 150, 0.6);
        z.Text(4, 3, 2.6, "DE :");
        z.Block(4, 7, 92, 3.2, v("from"), 3);
        z.Line(0, 20, 100, 0.5);
        z.Text(4, 22, 2.6, "À :");
        z.Block(4, 27, 92, 6, v("to"), 4);
        z.Line(0, 58, 100, 0.5);
        if (v("service").Length > 0)
        {
            z.Fill(0, 58, 100, 12);
            z.Text(4, 60.5, 7, v("service"), reverse: true);
        }

        var y = v("service").Length > 0 ? 73 : 62;
        z.Text(4, y, 2.6, "POIDS");
        z.Text(4, y + 3.5, 5, v("weight").Length > 0 ? v("weight") + " kg" : "—");
        z.Text(36, y, 2.6, "COLIS");
        z.Text(36, y + 3.5, 5, v("parcel").Length > 0 ? v("parcel") : "1/1");
        z.Text(62, y, 2.6, "RÉFÉRENCE");
        z.Block(62, y + 3.5, 34, 3.4, v("reference"), 2);
        z.Line(0, y + 14, 100, 0.5);
        z.Text(4, y + 17, 2.6, "N° DE SUIVI");
        z.Code128(6, y + 22, 28, v("tracking"), 88);
    }

    private static void Product(Zpl z, Func<string, string> v, List<string> w)
    {
        var gtin = Digits(v("gtin"));
        if (gtin.Length == 13 && !CheckDigit.IsValid(gtin))
        {
            w.Add($"Clé du GTIN fausse : l'imprimante imprime la clé calculée {CheckDigit.Compute(gtin.AsSpan(0, 12))}.");
        }

        var body = gtin.Length >= 12 ? gtin[..12] : gtin.PadLeft(12, '0');
        var full = body + CheckDigit.Compute(body).ToString(CultureInfo.InvariantCulture);
        var expiry = Date(v("expiry"), "Date limite", w);
        var lot = v("lot");
        z.Block(4, 3, 62, 4.2, v("name"), 2);
        if (v("reference").Length > 0)
        {
            z.Text(4, 13.5, 2.8, v("reference"));
        }

        var info = new StringBuilder();
        if (lot.Length > 0)
        {
            info.Append("LOT ").Append(lot);
        }

        if (expiry.Length > 0)
        {
            info.Append(info.Length > 0 ? "   " : string.Empty).Append("DLC ").Append(v("expiry"));
        }

        if (info.Length > 0)
        {
            z.Text(4, 18, 3, info.ToString());
        }

        z.Ean13(8, 25, 16, body);
        var dm = $"(01){full.PadLeft(14, '0')}" + (expiry.Length > 0 ? "(17)" + expiry : string.Empty) + (lot.Length > 0 ? "(10)" + lot : string.Empty);
        z.Gs1DataMatrix(72, 18, dm, w);
        Check(dm, w);
    }

    private static void Location(Zpl z, Func<string, string> v)
    {
        z.Box(0, 0, 100, 50, 0.8);
        z.Block(4, 3, 92, 13, v("code"), 1, center: true);
        if (v("detail").Length > 0)
        {
            z.Block(4, 17.5, 92, 3.2, v("detail"), 1, center: true);
        }

        z.Code128(8, 23, 18, v("code"), 84, readable: false);
    }

    private static void Asset(Zpl z, Func<string, string> v)
    {
        z.Qr(3, 3, v("number"), 0.5);
        z.Block(25, 4, 33, 3.2, v("name"), 3);
        if (v("owner").Length > 0)
        {
            z.Text(25, 16, 2.6, v("owner"));
        }

        z.Text(25, 20, 4, v("number"));
        z.Code128(4, 27, 8, v("number"), 52, readable: false);
    }

    // ------------------------------------------------------------------ Contrôles et mise en forme des données

    private static string Digits(string s) => new([.. s.Where(char.IsAsciiDigit)]);

    /// <summary>Complète ou vérifie la clé de contrôle GS1 d'un numéro de <paramref name="length"/> chiffres.</summary>
    private static string Key(string digits, int length, string name, List<string> w)
    {
        if (digits.Length == length - 1)
        {
            return digits + CheckDigit.Compute(digits).ToString(CultureInfo.InvariantCulture);
        }

        if (digits.Length != length)
        {
            w.Add($"{name} : {length} chiffres attendus ({length - 1} sans la clé), {digits.Length} saisis.");
            return digits.PadLeft(length, '0')[..length];
        }

        if (!CheckDigit.IsValid(digits))
        {
            var good = digits[..^1] + CheckDigit.Compute(digits.AsSpan(0, length - 1)).ToString(CultureInfo.InvariantCulture);
            w.Add($"{name} : clé de contrôle fausse ({digits[^1]}), corrigée en {good[^1]}.");
            return good;
        }

        return digits;
    }

    /// <summary>jj/mm/aaaa → AAMMJJ (vide si le champ est vide).</summary>
    private static string Date(string s, string name, List<string> w)
    {
        if (s.Trim().Length == 0)
        {
            return string.Empty;
        }

        if (DateTime.TryParseExact(s.Trim(), ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            return d.ToString("yyMMdd", CultureInfo.InvariantCulture);
        }

        w.Add($"{name} : date « {s} » non comprise (attendu jj/mm/aaaa).");
        return string.Empty;
    }

    /// <summary>« 512,40 » kg avec 2 décimales → « 051240 » (6 chiffres).</summary>
    private static string Decimal(string s, int decimals, int width, string name, List<string> w)
    {
        if (s.Trim().Length == 0)
        {
            return string.Empty;
        }

        if (!double.TryParse(s.Replace(',', '.').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            w.Add($"{name} : « {s} » n'est pas un nombre.");
            return string.Empty;
        }

        var n = (long)Math.Round(value * Math.Pow(10, decimals));
        var text = n.ToString(CultureInfo.InvariantCulture);
        if (text.Length > width)
        {
            w.Add($"{name} : valeur trop grande pour {width} chiffres.");
            return string.Empty;
        }

        return text.PadLeft(width, '0');
    }

    private static string Spaced(string sscc) => sscc.Length == 18 ? $"{sscc[0]} {sscc[1..8]} {sscc[8..17]} {sscc[17]}" : sscc;

    private static void Check(string gs1, List<string> w)
    {
        if (Gs1Parser.Parse(gs1) is { } m)
        {
            foreach (var e in m.Elements.Where(e => !e.IsValid))
            {
                w.Add($"({e.Ai}) {e.Name} : {string.Join(" ; ", e.Issues)}");
            }
        }
    }

    // ------------------------------------------------------------------ Écriture ZPL en millimètres

    private sealed class Zpl
    {
        private readonly StringBuilder _sb = new();
        private readonly int _dpi;

        public Zpl(double widthMm, double heightMm, int dpi)
        {
            _dpi = dpi;
            _sb.Append("^XA\r\n^CI28\r\n");
            _sb.Append(CultureInfo.InvariantCulture, $"^PW{D(widthMm)}\r\n^LL{D(heightMm)}\r\n^LH0,0\r\n");
        }

        private int D(double mm) => Math.Max(0, (int)Math.Round(mm * _dpi / 25.4));

        private static string Esc(string s) => Esc(s, '\\');

        /// <summary>^FH : caractères de contrôle, réservés et l'indicateur lui-même en hexadécimal.</summary>
        private static string Esc(string s, char indicator)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c < ' ' || c is '^' or '~' || c == indicator)
                {
                    sb.Append(indicator).Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        public void Text(double x, double y, double heightMm, string text, bool reverse = false) =>
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^A0N,{D(heightMm)},{D(heightMm)}{(reverse ? "^FR" : string.Empty)}^FH\\^FD{Esc(text)}^FS\r\n");

        /// <summary>Bloc de texte sur plusieurs lignes (^FB), une ligne du champ par ligne d'étiquette.</summary>
        public void Block(double x, double y, double widthMm, double heightMm, string text, int maxLines, bool center = false)
        {
            if (text.Length == 0)
            {
                return;
            }

            var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
            // Retour à la ligne de ^FB : « \& » ; l'indicateur hexadécimal est donc « _ » ici.
            var data = string.Join("\\&", lines.Select(l => Esc(l, '_')));
            _sb.Append(CultureInfo.InvariantCulture,
                $"^FO{D(x)},{D(y)}^A0N,{D(heightMm)},{D(heightMm)}^FB{D(widthMm)},{Math.Max(maxLines, lines.Length)},{D(heightMm * 0.15)},{(center ? 'C' : 'L')},0^FH_^FD{data}^FS\r\n");
        }

        public void Line(double x, double y, double widthMm, double thicknessMm) =>
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^GB{D(widthMm)},{Math.Max(1, D(thicknessMm))},{Math.Max(1, D(thicknessMm))}^FS\r\n");

        public void Box(double x, double y, double widthMm, double heightMm, double thicknessMm) =>
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^GB{D(widthMm)},{D(heightMm)},{Math.Max(1, D(thicknessMm))}^FS\r\n");

        public void Fill(double x, double y, double widthMm, double heightMm) =>
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^GB{D(widthMm)},{D(heightMm)},{D(Math.Min(widthMm, heightMm))}^FS\r\n");

        /// <summary>Module le plus large (au plus 0,5 mm, recommandation GS1 logistique) qui tient dans la largeur.</summary>
        private int Module(int modules, double maxWidthMm)
        {
            var best = Math.Max(1, D(0.5));
            while (best > 1 && modules * best > D(maxWidthMm))
            {
                best--;
            }

            return best;
        }

        /// <summary>Nombre de modules d'un Code 128 (estimation : sous-ensemble C pour les paires de chiffres, B sinon).</summary>
        private static int Modules128(string data, bool gs1)
        {
            var symbols = 3 + (gs1 ? 1 : 0); // départ, clé, arrêt (+ FNC1)
            var i = 0;
            while (i < data.Length)
            {
                var run = 0;
                while (i + run < data.Length && char.IsAsciiDigit(data[i + run]))
                {
                    run++;
                }

                if (run >= 4 || (run >= 2 && i == 0 && run == data.Length))
                {
                    // Sous-ensemble C : deux chiffres par symbole (+ changement de sous-ensemble, sauf en tête).
                    symbols += run / 2 + run % 2 + (i == 0 ? 0 : 1);
                    i += run;
                }
                else
                {
                    symbols++;
                    i++;
                }
            }

            return symbols * 11 + 2; // la marge à gauche et à droite sert de zone de silence
        }

        public void Gs1_128(double x, double y, double heightMm, string hri, double maxWidthMm, List<string> w)
        {
            var compact = new string([.. hri.Where(c => c is not '(' and not ')')]);
            var m = Module(Modules128(compact, true), maxWidthMm);
            if (m * 25.4 / _dpi < 0.495)
            {
                w.Add($"Code « {hri} » : module de {(m * 25.4 / _dpi).ToString("0.###", CultureInfo.InvariantCulture)} mm pour tenir dans la largeur (GS1 recommande 0,495 mm en logistique).");
            }

            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^BY{m},3,{D(heightMm)}^BCN,{D(heightMm)},Y,N,N,D^FD{hri}^FS\r\n");
        }

        public void Code128(double x, double y, double heightMm, string data, double maxWidthMm, bool readable = true)
        {
            if (data.Length == 0)
            {
                return;
            }

            var m = Module(Modules128(data, false), maxWidthMm);
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^BY{m},3,{D(heightMm)}^BCN,{D(heightMm)},{(readable ? 'Y' : 'N')},N,N,A^FH\\^FD{Esc(data)}^FS\r\n");
        }

        public void Ean13(double x, double y, double heightMm, string body12)
        {
            var m = Math.Max(2, D(0.33));
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^BY{m},3,{D(heightMm)}^BEN,{D(heightMm)},Y,N^FD{body12}^FS\r\n");
        }

        public void Gs1DataMatrix(double x, double y, string hri, List<string> w)
        {
            var m = Gs1Parser.Parse(hri);
            if (m is null)
            {
                w.Add("Données GS1 du Data Matrix illisibles.");
                return;
            }

            var data = "_1" + m.ToTransmitted().Replace("\u001D", "_1", StringComparison.Ordinal);
            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^BXN,{Math.Max(3, D(0.5))},200,,,,_^FD{data}^FS\r\n");
        }

        public void Qr(double x, double y, string data, double moduleMm)
        {
            if (data.Length == 0)
            {
                return;
            }

            _sb.Append(CultureInfo.InvariantCulture, $"^FO{D(x)},{D(y)}^BQN,2,{Math.Max(2, D(moduleMm))}^FH\\^FDMA,{Esc(data)}^FS\r\n");
        }

        public string End(int copies)
        {
            _sb.Append(CultureInfo.InvariantCulture, $"^PQ{Math.Max(1, copies)}\r\n^XZ\r\n");
            return _sb.ToString();
        }
    }
}
