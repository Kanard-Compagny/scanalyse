using System.Globalization;
using System.Text;
using Scanalyse.Core.Content;
using Scanalyse.Core.Text;

namespace Scanalyse.Core.History;

/// <summary>Niveau de détail des contenus de codes dans le rapport.</summary>
public enum ReportContent
{
    /// <summary>Contenu interprété : vue structurée, champs, caractéristiques.</summary>
    Detailed,

    /// <summary>Contenu brut tel que lu, et octets en hexadécimal.</summary>
    Raw,

    /// <summary>Les deux.</summary>
    Both,
}

/// <summary>
/// Rapport SAV d'un dossier, en HTML autonome (images incluses) prêt à imprimer ou à convertir en PDF.
/// Toujours en thème clair : c'est un document destiné au client.
/// </summary>
/// <summary>Émetteur du rapport (Paramètres → Rapport) : apparaît en en-tête.</summary>
/// <param name="Company">Société.</param>
/// <param name="Technician">Technicien par défaut (si le dossier n'en indique pas).</param>
/// <param name="Contact">Coordonnées (téléphone, e-mail…).</param>
/// <param name="LogoDataUrl">Logo en « data:image/… », ou vide.</param>
public sealed record ReportIssuer(string Company, string Technician, string Contact, string LogoDataUrl)
{
    public bool IsEmpty => Company.Length == 0 && Contact.Length == 0 && LogoDataUrl.Length == 0;
}

public static class ReportBuilder
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    /// <param name="serviceCase">Dossier.</param>
    /// <param name="entries">Entrées à inclure (l'ordre est rétabli chronologiquement).</param>
    /// <param name="imageDataUrl">Image d'une entrée en « data:image/… », ou <c>null</c>.</param>
    /// <param name="generated">Date de génération.</param>
    /// <param name="version">Version de Scanalyse.</param>
    /// <param name="content">Niveau de détail des contenus de codes (détaillé, brut ou les deux).</param>
    /// <param name="cases">Dossiers couverts, pour un rapport client sur plusieurs dossiers (détail regroupé par dossier).</param>
    /// <param name="issuer">Société et technicien qui éditent le rapport (Paramètres), ou rien.</param>
    public static string Build(ServiceCase serviceCase, IEnumerable<HistoryEntry> entries, Func<HistoryEntry, string?> imageDataUrl,
        DateTimeOffset generated, string version, IReadOnlyList<ServiceCase>? cases = null, ReportContent content = ReportContent.Both,
        ReportIssuer? issuer = null)
    {
        ArgumentNullException.ThrowIfNull(serviceCase);
        ArgumentNullException.ThrowIfNull(imageDataUrl);
        var list = entries.OrderBy(e => e.At).ToList();
        var sb = new StringBuilder(64 * 1024);
        var title = serviceCase.IsInbox ? "Analyses non classées" : serviceCase.DisplayName;

        sb.Append("<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Rapport Scanalyse · ")
          .Append(H(title)).Append("</title><style>").Append(Css).Append("</style></head><body>");

        // En-tête
        if (issuer is { IsEmpty: false })
        {
            // En-tête de la société qui édite le rapport (logo, nom, coordonnées).
            sb.Append("<header class=\"top\"><div class=\"brand\">");
            if (issuer.LogoDataUrl.StartsWith("data:image/", StringComparison.Ordinal))
            {
                sb.Append("<img class=\"logo\" alt=\"\" src=\"").Append(H(issuer.LogoDataUrl)).Append("\">");
            }

            sb.Append("<b>").Append(H(issuer.Company.Length > 0 ? issuer.Company : "Scanalyse")).Append("</b><span>Rapport d'analyse des codes-barres")
              .Append(issuer.Contact.Length > 0 ? " · " + H(issuer.Contact) : string.Empty).Append("</span></div>");
        }
        else
        {
            sb.Append("<header class=\"top\"><div class=\"brand\"><svg class=\"mark\" aria-hidden=\"true\" viewBox=\"0 0 1024 1024\" version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\" style=\"fill-rule:evenodd;clip-rule:evenodd;stroke-linejoin:round;stroke-miterlimit:2;\"><g transform=\"matrix(1.01286,0,0,1.01286,-5.24256,1.51611)\"><path d=\"M998,120.974C998,63.589 951.411,17 894.026,17L127.974,17C70.589,17 24,63.589 24,120.974L24,887.026C24,944.411 70.589,991 127.974,991L894.026,991C951.411,991 998,944.411 998,887.026L998,120.974Z\" style=\"fill:rgb(11,42,32);\"/></g><g transform=\"matrix(7.23509,0,0,0.919651,-740.543,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(6.05289,0,0,0.919651,-737.539,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(0.735594,0,0,1.00408,716.396,-15.6422)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(3.7337,0,0,1.00408,177.154,-15.6422)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-0.945764,0,0,1.00408,983.206,-15.6422)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-3.05272,0,0,0.919651,1262.66,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-2.50036,0,0,0.919651,1000.77,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-2.15002,0,0,0.919651,1102.65,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-4.83129,0,0,0.919651,1520.87,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-2.43905,0,0,1.00408,608.629,-15.6422)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(-7.49576,0,0,0.919651,1634.36,-0.276829)\"><rect x=\"164\" y=\"182\" width=\"20\" height=\"687\" style=\"fill:white;\"/></g><g transform=\"matrix(0.00774808,0.914273,-0.914273,0.00774808,463.666,759.667)\"><path d=\"M-318.57,248.614L-320.57,484.614L-229.43,485.386L-227.43,249.386L-318.57,248.614Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(0.914273,-0.00774808,0.00774808,0.914273,759.999,-211.034)\"><path d=\"M-318.57,248.614L-320.57,484.614L-229.43,485.386L-227.43,249.386L-318.57,248.614Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(-0.914273,0.00774808,-0.00774808,-0.914273,264.664,1235.03)\"><path d=\"M-318.57,248.614L-320.57,484.614L-229.43,485.386L-227.43,249.386L-318.57,248.614Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(0.724599,0.712421,-0.64101,0.651967,485.731,10.7422)\"><path d=\"M-204.696,365.302C-202.618,362.172 -199.645,357.893 -196.93,354.615C-181.825,336.383 -158.239,312.858 -126.212,277.859L-183.709,212.869C-225.941,259.02 -254.154,286.854 -268.897,308.057C-283.217,328.651 -288.611,346.815 -288.809,364.181C-289.008,381.665 -283.981,400.026 -269.971,421.013C-255.616,442.518 -227.843,470.92 -186.239,517.95L-127.761,454.05C-159.253,418.451 -182.447,394.496 -197.219,375.989C-199.84,372.705 -202.693,368.438 -204.696,365.302Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(-0.718536,0.718536,-0.646512,-0.646512,1011.12,486.669)\"><path d=\"M-204.696,365.302C-202.618,362.172 -199.645,357.893 -196.93,354.615C-181.825,336.383 -158.239,312.858 -126.212,277.859L-183.709,212.869C-225.941,259.02 -254.154,286.854 -268.897,308.057C-283.217,328.651 -288.611,346.815 -288.809,364.181C-289.008,381.665 -283.981,400.026 -269.971,421.013C-255.616,442.518 -227.843,470.92 -186.239,517.95L-127.761,454.05C-159.253,418.451 -182.447,394.496 -197.219,375.989C-199.84,372.705 -202.693,368.438 -204.696,365.302Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(-0.718536,-0.718536,0.646512,-0.646512,537.183,1010.26)\"><path d=\"M-204.696,365.302C-202.618,362.172 -199.645,357.893 -196.93,354.615C-181.825,336.383 -158.239,312.858 -126.212,277.859L-183.709,212.869C-225.941,259.02 -254.154,286.854 -268.897,308.057C-283.217,328.651 -288.611,346.815 -288.809,364.181C-289.008,381.665 -283.981,400.026 -269.971,421.013C-255.616,442.518 -227.843,470.92 -186.239,517.95L-127.761,454.05C-159.253,418.451 -182.447,394.496 -197.219,375.989C-199.84,372.705 -202.693,368.438 -204.696,365.302Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(0.718536,-0.718536,0.646512,0.646512,12.8842,536.331)\"><path d=\"M-204.696,365.302C-202.618,362.172 -199.645,357.893 -196.93,354.615C-181.825,336.383 -158.239,312.858 -126.212,277.859L-183.709,212.869C-225.941,259.02 -254.154,286.854 -268.897,308.057C-283.217,328.651 -288.611,346.815 -288.809,364.181C-289.008,381.665 -283.981,400.026 -269.971,421.013C-255.616,442.518 -227.843,470.92 -186.239,517.95L-127.761,454.05C-159.253,418.451 -182.447,394.496 -197.219,375.989C-199.84,372.705 -202.693,368.438 -204.696,365.302Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(-0.00774808,-0.914273,0.914273,-0.00774808,560.043,264.333)\"><path d=\"M-318.57,248.614L-320.57,484.614L-229.43,485.386L-227.43,249.386L-318.57,248.614Z\" style=\"fill:rgb(255,154,46);\"/></g><g transform=\"matrix(1.14868,0,0,1.14868,-79.2385,-25.582)\"><path d=\"M515,286C615.449,286 697,367.551 697,468C697,568.449 615.449,650 515,650C414.551,650 333,568.449 333,468C333,367.551 414.551,286 515,286ZM515,377C565.224,377 606,417.776 606,468C606,518.224 565.224,559 515,559C464.776,559 424,518.224 424,468C424,417.776 464.776,377 515,377Z\" style=\"fill:rgb(255,154,46);\"/></g></svg><b>Scanalyse</b><span>Rapport d'analyse des codes-barres</span></div>");
        }

        sb.Append("<div class=\"gen\">Édité le ").Append(H(generated.ToString("dd MMMM yyyy 'à' HH:mm", Fr))).Append("</div></header>");

        // Dossier
        sb.Append("<section class=\"case\"><h1>").Append(H(title)).Append("</h1><dl>");
        Row(sb, "Client / site", serviceCase.Client);
        Row(sb, "Référence", serviceCase.Reference);
        Row(sb, "Intervenant", string.IsNullOrWhiteSpace(serviceCase.Technician) ? issuer?.Technician ?? string.Empty : serviceCase.Technician);
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

        // Détail, regroupé par dossier quand le rapport en couvre plusieurs (rapport client)
        sb.Append("<h2 class=\"detail\">Détail des analyses</h2>");
        var groups = list.GroupBy(e => e.CaseId).ToList();
        var multi = cases is { Count: > 1 } && groups.Count > 1;
        var n = 0;
        List<(string Key, IEnumerable<HistoryEntry> Items)> parts = multi
            ? [.. groups.OrderBy(g => g.Min(e => e.At)).Select(g => (g.Key, (IEnumerable<HistoryEntry>)g))]
            : [(serviceCase.Id, list)];
        foreach (var (key, items) in parts)
        {
            if (multi)
            {
                var c = cases!.FirstOrDefault(x => x.Id == key);
                sb.Append("<section class=\"case-head\"><h2>Dossier ").Append(H(c?.Reference is { Length: > 0 } r ? r : c?.DisplayName ?? "non classé")).Append("</h2>");
                if (c is not null)
                {
                    sb.Append("<p class=\"meta\">Créé le ").Append(H(c.Created.ToString("d MMMM yyyy", Fr)))
                      .Append(string.IsNullOrWhiteSpace(c.Technician) ? string.Empty : " · " + H(c.Technician)).Append("</p>");
                    if (!string.IsNullOrWhiteSpace(c.Notes))
                    {
                        sb.Append("<p>").Append(Multiline(c.Notes)).Append("</p>");
                    }
                }

                sb.Append("</section>");
            }

            foreach (var e in items)
            {
                n++;
                AppendEntry(sb, e, n, imageDataUrl(e), content);
            }
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

    private static void AppendEntry(StringBuilder sb, HistoryEntry e, int n, string? img, ReportContent content)
    {
        var detailed = content != ReportContent.Raw;
        var raw = content != ReportContent.Detailed;
        var hasImg = !string.IsNullOrEmpty(img);
        sb.Append("<article class=\"entry\"><div class=\"eh\"><span class=\"n\">").Append(n).Append("</span><div><h3>").Append(H(e.Title)).Append("</h3><p class=\"meta\">")
          .Append(H(e.At.ToString("dd/MM/yyyy HH:mm:ss", Fr))).Append(string.IsNullOrWhiteSpace(e.Source) ? string.Empty : " · " + H(e.Source))
          .Append(string.IsNullOrWhiteSpace(e.CustomTitle) ? string.Empty : " · " + H(e.AutoTitle)).Append("</p></div><span class=\"pill ")
          .Append(Css4(e.Worst)).Append("\">").Append(H(Verdict(e))).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(e.Comment))
        {
            sb.Append("<div class=\"comment\"><b>Commentaire de l'intervenant</b><br>").Append(Multiline(e.Comment)).Append("</div>");
        }

        if (e.Kind == HistoryKind.ImageAnalysis)
        {
            // Photo pleine largeur, codes encadrés et numérotés, puis la fiche complète de chaque code.
            if (hasImg)
            {
                sb.Append("<figure class=\"photo\"><img src=\"").Append(img).Append("\" alt=\"Image analysée, codes numérotés\"><figcaption>Les numéros sur l'image renvoient aux fiches ci-dessous.</figcaption></figure>");
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
            else if (e.LabelVerdict == "good" && e.Codes.Count > 1)
            {
                sb.Append("<p class=\"allok\">Étiquette cohérente : les données sont identiques d'un code à l'autre et respectent les règles GS1.</p>");
            }

            if (e.Codes.Count == 0)
            {
                sb.Append("<p>Aucun code n'a pu être lu sur cette image.</p>");
            }

            var i = 0;
            foreach (var c in e.Codes)
            {
                i++;
                sb.Append("<section class=\"code\"><div class=\"ch\"><span class=\"n small\">").Append(i).Append("</span><b>").Append(H(c.Format)).Append("</b>")
                  .Append(string.IsNullOrEmpty(c.Kind) ? string.Empty : "<span class=\"kind\">" + H(c.Kind) + "</span>");
                if (!string.IsNullOrEmpty(c.Health))
                {
                    sb.Append("<span class=\"pill ").Append(c.HealthClass switch { "bad" => "bad", "warn" => "warn", _ => "ok" }).Append("\">").Append(H(c.Health)).Append("</span>");
                }

                sb.Append("</div>");
                var structured = detailed && AppendStructure(sb, c.Text);
                if (raw || !structured)
                {
                    sb.Append("<div class=\"content mono\">").Append(H(c.Text)).Append("</div>");
                }

                // Les remarques purement informatives restent dans l'analyseur ; le rapport garde ce qui demande une action.
                if (c.Alerts?.Where(x => x.Level is "warn" or "bad").ToList() is { Count: > 0 } alerts)
                {
                    sb.Append("<ul class=\"findings\">");
                    foreach (var a in alerts)
                    {
                        sb.Append("<li class=\"").Append(a.Level switch { "bad" => "bad", "warn" => "warn", _ => "info" }).Append("\">").Append(H(a.Message)).Append("</li>");
                    }

                    sb.Append("</ul>");
                }

                if (!c.HasDetails && detailed)
                {
                    sb.Append("<p class=\"meta\">Fiche détaillée non enregistrée (analyse faite avec une version précédente) : rouvre l'analyse depuis l'historique pour la compléter.</p>");
                }

                var hasContent = c.Content is { Count: > 0 };
                var hasTech = c.Technical is { Count: > 0 };
                if (detailed && (hasContent || hasTech))
                {
                    sb.Append("<div class=\"cols\">");
                    if (hasContent)
                    {
                        Details(sb, "Ce que contient le code", c.Content!);
                    }

                    if (hasTech)
                    {
                        Details(sb, "Caractéristiques techniques", c.Technical!);
                    }

                    sb.Append("</div>");
                }
                else if (!string.IsNullOrEmpty(c.SymbologyIdentifier))
                {
                    sb.Append("<p class=\"meta\">Identifiant de symbologie ").Append(H(c.SymbologyIdentifier)).Append("</p>");
                }

                if (raw && !string.IsNullOrEmpty(c.Hex))
                {
                    sb.Append("<h4>Données brutes (hexadécimal)</h4><pre class=\"hex\">").Append(H(c.Hex)).Append("</pre>");
                }

                sb.Append("</section>");
            }
        }
        else
        {
            sb.Append(hasImg ? "<div class=\"body withimg\"><figure><img src=\"" + img + "\" alt=\"Code testé\"></figure><div>" : "<div class=\"body\"><div>");
            // Lecture identique au contenu (au suffixe près) : inutile de recopier deux fois un long code.
            var same = e.Received.Length > 0 && TrimSuffix(e.Received) == TrimSuffix(e.Expected);
            var expectedLength = ControlCharacters.FromVisualized(e.Expected).Length;
            sb.Append("<dl class=\"cmp\"><dt>Contenu du code</dt><dd class=\"mono\">")
              .Append(raw ? H(e.Expected) : $"<span class=\"ui\">{expectedLength} caractères (détail ci-dessous)</span>")
              .Append("</dd><dt>Reçu de la douchette</dt><dd class=\"mono\">")
              .Append(e.Received.Length == 0 ? "<i>rien</i>"
                  : same ? $"<span class=\"ui ok\">identique au contenu du code{(e.Received.Length != e.Expected.Length ? $" (suffixe « {H(e.Received[TrimSuffix(e.Received).Length..])} » en plus)" : string.Empty)}</span>"
                  : H(e.Received))
              .Append("</dd>");
            if (!string.IsNullOrEmpty(e.AnnouncedSymbology))
            {
                sb.Append("<dt>Symbologie annoncée</dt><dd>").Append(H(e.AnnouncedSymbology)).Append("</dd>");
            }

            sb.Append("</dl>");
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

            sb.Append("</div></div>");
            if (detailed)
            {
                AppendContentAnalysis(sb, e.Expected);
            }
        }

        sb.Append("</article>");
    }

    private static string TrimSuffix(string visual)
    {
        var s = visual;
        foreach (var suffix in new[] { "<CR><LF>", "<CR>", "<LF>", "<HT>" })
        {
            if (s.EndsWith(suffix, StringComparison.Ordinal))
            {
                return s[..^suffix.Length];
            }
        }

        return s;
    }

    /// <summary>Vue structurée (enveloppe ISO/IEC 15434, sections, champs) ; <c>false</c> si le contenu n'a pas de séparateurs.</summary>
    private static bool AppendStructure(StringBuilder sb, string text)
    {
        var view = StructuredData.Parse(text);
        if (view is null)
        {
            return false;
        }

        sb.Append("<div class=\"struct\">");
        if (view.Envelope is not null)
        {
            sb.Append("<p class=\"meta\">").Append(H(view.Envelope)).Append("</p>");
        }

        foreach (var section in view.Sections)
        {
            sb.Append("<h4>").Append(H(section.Title)).Append(" · ").Append(section.Fields.Count).Append(" champ").Append(section.Fields.Count > 1 ? "s" : string.Empty)
              .Append("</h4><table class=\"kv fields\"><tbody>");
            foreach (var f in section.Fields)
            {
                var value = f.Identifier is null ? f.Value : f.Value[f.Identifier.Length..];
                var shown = f.Parts.Count > 1 ? string.Join(" │ ", f.Parts.Select(ControlCharacters.Visualize)) : ControlCharacters.Visualize(value);
                sb.Append("<tr><th>#").Append(f.Index).Append(f.Identifier is null ? string.Empty : " " + H(f.Identifier))
                  .Append(f.IdentifierName is null ? string.Empty : "<br><small>" + H(f.IdentifierName) + "</small>")
                  .Append("</th><td class=\"mono\">").Append(shown.Length == 0 ? "<i>(vide)</i>" : H(shown)).Append("</td></tr>");
            }

            sb.Append("</tbody></table>");
        }

        foreach (var r in view.Remarks)
        {
            sb.Append("<p class=\"meta\">• ").Append(H(r)).Append("</p>");
        }

        sb.Append("</div>");
        return true;
    }

    /// <summary>Interprétation du contenu attendu d'une comparaison (comme « Analyser la référence » au banc).</summary>
    private static void AppendContentAnalysis(StringBuilder sb, string expectedVisual)
    {
        var text = ControlCharacters.FromVisualized(expectedVisual);
        var analysis = new ContentAnalyzer().Analyze(new ScanInput(text));
        var structured = StructuredData.Parse(text) is not null;
        if (analysis.Fields.Count == 0 && !structured)
        {
            return;
        }

        sb.Append("<section class=\"code\"><div class=\"ch\"><b>Contenu du code détaillé</b><span class=\"kind\">").Append(H(analysis.Title)).Append("</span></div>");
        AppendStructure(sb, text);
        if (analysis.Fields.Count > 0)
        {
            Details(sb, "Ce que contient le code", [.. analysis.Fields.Select(f => new HistoryDetail(f.Label, f.Sensitive ? "•••••• (masqué)" : f.Value + (f.Hint is { Length: > 0 } h ? " · " + h : string.Empty)))]);
        }

        sb.Append("</section>");
    }

    private static void Details(StringBuilder sb, string title, List<HistoryDetail> rows)
    {
        sb.Append("<div><h4>").Append(H(title)).Append("</h4><table class=\"kv\"><tbody>");
        foreach (var r in rows)
        {
            sb.Append("<tr><th>").Append(H(r.Label)).Append("</th><td>").Append(H(r.Value)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></div>");
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
        .brand .logo{height:34px;max-width:140px;object-fit:contain;background:#fff;border-radius:6px;padding:3px}
        .brand span{color:#A9C3B7;font-size:9.5pt}
        .mark{width:18px;height:18px;display:inline-block;vertical-align:middle}
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
        .detail{page-break-before:auto;page-break-after:avoid;break-after:avoid}
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
        .photo{margin:10px 0 0;padding:6px;text-align:center;page-break-inside:avoid;break-inside:avoid}
        .photo img{max-width:100%;max-height:115mm;width:auto;display:inline-block}
        .photo figcaption{font-size:8.5pt;color:#5D656C;margin-top:4px}
        .code{border-top:1px solid #E1E4E7;margin-top:12px;padding-top:10px;page-break-inside:avoid;break-inside:avoid}
        .ch{display:flex;align-items:center;gap:8px;flex-wrap:wrap}
        .n.small{width:20px;height:20px;font-size:8.5pt}
        .kind{color:#5D656C}
        .content{background:#F4F5F6;border-radius:7px;padding:7px 10px;margin-top:7px}
        .cols{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin-top:4px}
        .kv th{width:40%;color:#5D656C;font-weight:500}
        .kv td,.kv th{font-size:9pt;padding:4px 6px}
        .hex{font-family:"Cascadia Mono",Consolas,monospace;font-size:7.5pt;line-height:1.35;background:#F4F5F6;border-radius:7px;padding:7px 10px;margin:0;white-space:pre-wrap;overflow-wrap:anywhere}
        .comment{margin-top:10px;padding:8px 11px;border-left:3px solid #F07F13;background:#FFF5EA;border-radius:0 7px 7px 0;font-size:9.5pt}
        .ui{font-family:"Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;color:#5D656C}
        .ui.ok{color:#13633F;font-weight:600}
        .struct h4{margin-top:10px}
        .code:has(.struct){page-break-inside:auto;break-inside:auto}
        .kv.fields th{width:22%}
        .case-head{margin-top:16px;border-left:4px solid #F07F13;padding-left:10px}
        .case-head h2{margin:0}
        .case-head p{margin:4px 0}
        .entry{page-break-inside:auto;break-inside:auto}
        .eh{page-break-after:avoid;break-after:avoid}
        footer{margin-top:18px;padding-top:8px;border-top:1px solid #E1E4E7;color:#5D656C;font-size:8.5pt}
        """;
}
