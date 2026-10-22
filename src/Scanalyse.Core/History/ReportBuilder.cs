using System.Globalization;
using System.Text;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.History;

/// <summary>
/// Rapport SAV d'un dossier, en HTML autonome (images incluses) prêt à imprimer ou à convertir en PDF.
/// Toujours en thème clair : c'est un document destiné au client.
/// </summary>
public static class ReportBuilder
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <param name="serviceCase">Dossier.</param>
    /// <param name="entries">Entrées à inclure (l'ordre est rétabli chronologiquement).</param>
    /// <param name="imageDataUrl">Image d'une entrée en « data:image/… », ou <c>null</c>.</param>
    /// <param name="generated">Date de génération.</param>
    /// <param name="version">Version de Scanalyse.</param>
    public static string Build(ServiceCase serviceCase, IEnumerable<HistoryEntry> entries, Func<HistoryEntry, string?> imageDataUrl,
        DateTimeOffset generated, string version)
    {
        ArgumentNullException.ThrowIfNull(serviceCase);
        ArgumentNullException.ThrowIfNull(imageDataUrl);
        var list = entries.OrderBy(e => e.At).ToList();
        var sb = new StringBuilder(64 * 1024);
        var title = serviceCase.IsInbox ? "Analyses non classées" : serviceCase.DisplayName;

        sb.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Rapport Scanalyse · ")
          .Append(H(title)).Append("</title><style>").Append(Css).Append("</style></head><body>");

        // En-tête
        sb.Append("<header class=\"top\"><div class=\"brand\"><span class=\"mark\"></span><b>Scanalyse</b><span>Rapport d'analyse des codes-barres</span></div>")
          .Append("<div class=\"gen\">Édité le ").Append(H(generated.ToString("dd MMMM yyyy 'à' HH:mm", Fr))).Append("</div></header>");

        // Dossier
        sb.Append("<section class=\"case\"><h1>").Append(H(title)).Append("</h1><dl>");
        Row(sb, "Client / site", serviceCase.Client);
        Row(sb, "Référence", serviceCase.Reference);
        Row(sb, "Intervenant", serviceCase.Technician);
        if (list.Count > 0)
        {
            var first = list[0].At;
            var last = list[^1].At;
            Row(sb, "Période", first.Date == last.Date
                ? $"{first.ToString("dddd d MMMM yyyy", Fr)}, de {first:HH:mm} à {last:HH:mm}"
                : $"du {first.ToString("d MMMM yyyy", Fr)} au {last.ToString("d MMMM yyyy", Fr)}");
        }

        sb.Append("</dl>");
        if (!string.IsNullOrWhiteSpace(serviceCase.Notes))
        {
            sb.Append("<div class=\"notes\"><h3>Notes</h3><p>").Append(Multiline(serviceCase.Notes)).Append("</p></div>");
        }

        sb.Append("</section>");

        // Synthèse
        var images = list.Count(e => e.Kind == HistoryKind.ImageAnalysis);
        var compares = list.Count(e => e.Kind == HistoryKind.Comparison);
        var ok = list.Count(e => e.Worst <= Severity.Info);
        var warn = list.Count(e => e.Worst == Severity.Warning);
        var bad = list.Count(e => e.Worst == Severity.Error);
        sb.Append("<section class=\"sum\"><h2>Synthèse</h2><div class=\"tiles\">")
          .Append(Tile(images.ToString(Fr), images > 1 ? "images analysées" : "image analysée"))
          .Append(Tile(compares.ToString(Fr), compares > 1 ? "comparaisons douchette" : "comparaison douchette"))
          .Append(Tile(ok.ToString(Fr), "conformes", "ok"))
          .Append(Tile(warn.ToString(Fr), "à vérifier", "warn"))
          .Append(Tile(bad.ToString(Fr), bad > 1 ? "problèmes" : "problème", "bad"))
          .Append("</div>");
        var issues = list.Where(e => e.Worst >= Severity.Warning).ToList();
        if (issues.Count > 0)
        {
            sb.Append("<h3>Points relevés</h3><ul class=\"issues\">");
            foreach (var e in issues)
            {
                sb.Append("<li class=\"").Append(Css4(e.Worst)).Append("\"><b>").Append(H(e.At.ToString("HH:mm", Fr))).Append("</b> ")
                  .Append(H(e.Title)).Append(MainIssue(e) is { } m ? " : " + H(m) : string.Empty).Append("</li>");
            }

            sb.Append("</ul>");
        }
        else if (list.Count > 0)
        {
            sb.Append("<p class=\"allok\">Aucune anomalie relevée sur les analyses de ce dossier.</p>");
        }

        sb.Append("</section>");

        // Détail
        sb.Append("<h2 class=\"detail\">Détail des analyses</h2>");
        var n = 0;
        foreach (var e in list)
        {
            n++;
            sb.Append("<article class=\"entry\"><div class=\"eh\"><span class=\"n\">").Append(n).Append("</span><div><h3>").Append(H(e.Title)).Append("</h3><p class=\"meta\">")
              .Append(H(e.At.ToString("dd/MM/yyyy HH:mm:ss", Fr))).Append(string.IsNullOrWhiteSpace(e.Source) ? string.Empty : " · " + H(e.Source)).Append("</p></div><span class=\"pill ")
              .Append(Css4(e.Worst)).Append("\">").Append(H(Verdict(e))).Append("</span></div>");

            var img = imageDataUrl(e);
            var hasImg = !string.IsNullOrEmpty(img);
            sb.Append(hasImg ? "<div class=\"body withimg\"><figure><img src=\"" + img + "\" alt=\"Image analysée\"></figure><div>" : "<div class=\"body\"><div>");

            if (e.Kind == HistoryKind.ImageAnalysis)
            {
                if (e.Codes.Count == 0)
                {
                    sb.Append("<p>Aucun code n'a pu être lu sur cette image.</p>");
                }
                else
                {
                    sb.Append("<table><thead><tr><th>#</th><th>Symbologie</th><th>Contenu</th></tr></thead><tbody>");
                    var i = 0;
                    foreach (var c in e.Codes)
                    {
                        i++;
                        sb.Append("<tr><td>").Append(i).Append("</td><td>").Append(H(c.Format))
                          .Append(string.IsNullOrEmpty(c.SymbologyIdentifier) ? string.Empty : "<br><small>" + H(c.SymbologyIdentifier) + "</small>")
                          .Append(string.IsNullOrEmpty(c.Kind) ? string.Empty : "<br><small>" + H(c.Kind) + "</small>")
                          .Append("</td><td class=\"mono\">").Append(H(c.Text)).Append("</td></tr>");
                    }

                    sb.Append("</tbody></table>");
                }

                if (e.LabelNotes.Count > 0)
                {
                    sb.Append("<h4>Cohérence de l'étiquette</h4><ul class=\"findings\">");
                    foreach (var note in e.LabelNotes)
                    {
                        sb.Append("<li class=\"").Append(Css4(e.Worst)).Append("\">").Append(H(note)).Append("</li>");
                    }

                    sb.Append("</ul>");
                }
            }
            else
            {
                sb.Append("<dl class=\"cmp\"><dt>Contenu du code</dt><dd class=\"mono\">").Append(H(e.Expected))
                  .Append("</dd><dt>Reçu de la douchette</dt><dd class=\"mono\">").Append(e.Received.Length == 0 ? "<i>rien</i>" : H(e.Received)).Append("</dd>");
                if (!string.IsNullOrEmpty(e.AnnouncedSymbology))
                {
                    sb.Append("<dt>Symbologie annoncée</dt><dd>").Append(H(e.AnnouncedSymbology)).Append("</dd>");
                }

                sb.Append("</dl>");
            }

            if (e.Findings.Count > 0)
            {
                sb.Append("<ul class=\"findings\">");
                foreach (var f in e.Findings)
                {
                    sb.Append("<li class=\"").Append(Css4(f.Severity)).Append("\"><b>").Append(H(f.Title)).Append("</b>");
                    if (!string.IsNullOrWhiteSpace(f.Explanation))
                    {
                        sb.Append("<br>").Append(H(f.Explanation));
                    }

                    if (!string.IsNullOrWhiteSpace(f.Fix))
                    {
                        sb.Append("<br><span class=\"fix\">À faire : ").Append(H(f.Fix)).Append("</span>");
                    }

                    sb.Append("</li>");
                }

                sb.Append("</ul>");
            }

            sb.Append("</div></div></article>");
        }

        if (list.Count == 0)
        {
            sb.Append("<p>Ce dossier ne contient encore aucune analyse.</p>");
        }

        sb.Append("<footer>Rapport généré par Scanalyse ").Append(H(version))
          .Append(". Analyse faite localement sur le poste de l'intervenant. Les indications de contraste et de lisibilité sont des estimations : elles ne remplacent pas une vérification ISO/IEC 15415 ou 15416 par un vérificateur étalonné.</footer>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Libellé du résultat d'une entrée.</summary>
    public static string Verdict(HistoryEntry e) => e.Kind == HistoryKind.Comparison && !string.IsNullOrEmpty(e.Verdict)
        ? e.Verdict
        : e.Worst switch
        {
            Severity.Error => "Problème",
            Severity.Warning => "À vérifier",
            _ => "Conforme",
        };

    private static string? MainIssue(HistoryEntry e) =>
        e.Findings.Where(f => f.Severity >= Severity.Warning).OrderByDescending(f => f.Severity).Select(f => f.Title).FirstOrDefault()
        ?? e.LabelNotes.FirstOrDefault();

    private static void Row(StringBuilder sb, string label, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            sb.Append("<dt>").Append(H(label)).Append("</dt><dd>").Append(H(value)).Append("</dd>");
        }
    }

    private static string Tile(string value, string label, string css = "") =>
        $"<div class=\"tile {css}\"><b>{H(value)}</b><span>{H(label)}</span></div>";

    private static string Css4(Severity s) => s switch
    {
        Severity.Error => "bad",
        Severity.Warning => "warn",
        Severity.Info => "info",
        _ => "ok",
    };

    /// <summary>Échappement HTML minimal : les accents restent lisibles dans le fichier.</summary>
    private static string H(string? s) => (s ?? string.Empty)
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private static string Multiline(string s) => H(s).Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal);

    private const string Css = """
        @page{size:A4;margin:12mm}
        *{box-sizing:border-box}
        body{font:10.5pt/1.45 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;color:#15191C;margin:0;background:#fff;-webkit-print-color-adjust:exact;print-color-adjust:exact}
        .top{display:flex;justify-content:space-between;align-items:center;background:#0F3D2E;color:#E6F0EB;padding:12px 16px;border-radius:10px}
        .brand{display:flex;align-items:center;gap:10px}
        .brand b{font-size:15pt;letter-spacing:.01em}
        .brand span{color:#A9C3B7;font-size:9.5pt}
        .mark{width:14px;height:14px;border-radius:4px;background:#F07F13;display:inline-block}
        .gen{font-size:9pt;color:#A9C3B7}
        h1{font-size:18pt;margin:18px 0 8px}
        h2{font-size:13pt;margin:22px 0 10px;color:#145C43}
        h3{font-size:11pt;margin:0}
        h4{font-size:9pt;text-transform:uppercase;letter-spacing:.06em;color:#5D656C;margin:12px 0 6px}
        dl{display:grid;grid-template-columns:max-content 1fr;gap:4px 16px;margin:0}
        dt{color:#5D656C}
        dd{margin:0}
        .notes{margin-top:10px;padding:10px 12px;background:#F4F5F6;border-radius:8px}
        .notes p{margin:4px 0 0}
        .tiles{display:grid;grid-template-columns:repeat(5,1fr);gap:8px}
        .tile{border:1px solid #E1E4E7;border-radius:8px;padding:8px 10px}
        .tile b{display:block;font-size:17pt;line-height:1.1}
        .tile span{color:#5D656C;font-size:9pt}
        .tile.ok b{color:#13633F}.tile.warn b{color:#6B5000}.tile.bad b{color:#A3261C}
        .issues{margin:0;padding-left:18px}
        .issues li{margin:3px 0}
        .issues li.bad{color:#A3261C}.issues li.warn{color:#6B5000}
        .allok{color:#13633F;font-weight:600}
        .detail{page-break-before:auto}
        .entry{border:1px solid #E1E4E7;border-radius:10px;padding:12px 14px;margin:0 0 10px;page-break-inside:avoid;break-inside:avoid}
        .eh{display:flex;align-items:flex-start;gap:10px}
        .eh>div{flex:1}
        .n{width:22px;height:22px;border-radius:50%;background:#145C43;color:#fff;display:grid;place-items:center;font-size:9pt;font-weight:700;flex:none}
        .meta{margin:2px 0 0;color:#5D656C;font-size:9pt}
        .pill{font-size:8.5pt;font-weight:600;padding:2px 9px;border-radius:11px;white-space:nowrap}
        .pill.ok{background:#E1F2E8;color:#13633F}.pill.info{background:#E4ECF7;color:#244F86}.pill.warn{background:#FBF0C8;color:#6B5000}.pill.bad{background:#FCE5E3;color:#A3261C}
        .body{margin-top:10px}
        .withimg{display:grid;grid-template-columns:38% 1fr;gap:14px;align-items:start}
        figure{margin:0;border:1px solid #E1E4E7;border-radius:8px;padding:6px;background:#fff}
        figure img{display:block;width:100%;max-height:70mm;object-fit:contain}
        table{width:100%;border-collapse:collapse;font-size:9.5pt}
        th,td{text-align:left;padding:5px 6px;border-bottom:1px solid #E1E4E7;vertical-align:top}
        th{color:#5D656C;font-weight:600}
        td small{color:#5D656C}
        .mono{font-family:"Cascadia Mono",Consolas,monospace;font-size:9pt;overflow-wrap:anywhere;word-break:break-all}
        .cmp{grid-template-columns:max-content 1fr}
        .findings{list-style:none;margin:10px 0 0;padding:0;display:grid;gap:6px}
        .findings li{padding:7px 10px;border-radius:7px;background:#F4F5F6}
        .findings li.ok{background:#E1F2E8;color:#13633F}.findings li.info{background:#E4ECF7;color:#1d3f6b}.findings li.warn{background:#FBF0C8;color:#5a4300}.findings li.bad{background:#FCE5E3;color:#8f1f17}
        .fix{color:#15191C}
        footer{margin-top:18px;padding-top:8px;border-top:1px solid #E1E4E7;color:#5D656C;font-size:8.5pt}
        """;
}
