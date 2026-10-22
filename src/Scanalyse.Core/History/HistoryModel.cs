using System.Text.Json.Serialization;
using Scanalyse.Core.Content;

namespace Scanalyse.Core.History;

/// <summary>Nature d'une entrée d'historique.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<HistoryKind>))]
public enum HistoryKind
{
    /// <summary>Image analysée (photo, capture, caméra, import).</summary>
    ImageAnalysis,

    /// <summary>Comparaison au banc d'essai entre le contenu du code et la lecture douchette.</summary>
    Comparison,
}

/// <summary>Dossier SAV : regroupe les analyses d'une intervention pour un client.</summary>
public sealed class ServiceCase
{
    /// <summary>Identifiant du dossier « non classé », toujours présent.</summary>
    public const string InboxId = "inbox";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Client ou site.</summary>
    public string Client { get; set; } = string.Empty;

    /// <summary>Référence : n° de ticket, d'intervention, de commande…</summary>
    public string Reference { get; set; } = string.Empty;

    /// <summary>Intervenant (repris dans le rapport).</summary>
    public string Technician { get; set; } = string.Empty;

    /// <summary>Notes libres (contexte, matériel, conclusion).</summary>
    public string Notes { get; set; } = string.Empty;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public bool IsInbox => Id == InboxId;

    /// <summary>Nom affiché : « Client · Référence », ou « Non classé ».</summary>
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (IsInbox)
            {
                return "Non classé";
            }

            var parts = new[] { Client, Reference }.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
            return parts.Length == 0 ? $"Dossier du {Created:dd/MM/yyyy}" : string.Join(" · ", parts);
        }
    }
}

/// <summary>Code lu dans une image.</summary>
/// <param name="Format">Symbologie (« Data Matrix »…).</param>
/// <param name="Text">Contenu, caractères invisibles en notation lisible (&lt;GS&gt;…).</param>
/// <param name="SymbologyIdentifier">Identifiant AIM, s'il est connu.</param>
/// <param name="Kind">Nature du contenu (« Données GS1 », « Lien web »…).</param>
/// <param name="Health">État de lecture (« Lecture fiable », « Code abîmé »…).</param>
/// <param name="HealthClass">« good », « warn » ou « bad ».</param>
/// <param name="Content">Ce que contient le code (libellé, valeur), comme dans l'analyseur.</param>
/// <param name="Technical">Caractéristiques techniques (libellé, valeur).</param>
/// <param name="Alerts">Remarques (gravité « info », « warn », « bad » ; message).</param>
/// <param name="Hex">Données brutes en hexadécimal.</param>
public sealed record HistoryCode(string Format, string Text, string SymbologyIdentifier = "", string Kind = "",
    string Health = "", string HealthClass = "", List<HistoryDetail>? Content = null, List<HistoryDetail>? Technical = null, List<HistoryNote>? Alerts = null,
    string Hex = "")
{
    /// <summary>La fiche complète a été enregistrée (versions récentes) ; sinon seul le contenu est connu.</summary>
    [JsonIgnore]
    public bool HasDetails => Technical is { Count: > 0 } || Content is { Count: > 0 };
}

/// <summary>Ligne « libellé : valeur » de la fiche d'un code.</summary>
public sealed record HistoryDetail(string Label, string Value);

/// <summary>Remarque sur un code (« info », « warn », « bad »).</summary>
public sealed record HistoryNote(string Level, string Message);

/// <summary>Constat enregistré.</summary>
public sealed record HistoryFinding(Severity Severity, string Title, string Explanation = "", string? Fix = null);

/// <summary>Une analyse ou une comparaison enregistrée.</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset At { get; set; } = DateTimeOffset.Now;

    public HistoryKind Kind { get; set; }

    /// <summary>Inclure dans le rapport du dossier.</summary>
    public bool InReport { get; set; } = true;

    /// <summary>Dossier SAV de rattachement.</summary>
    public string CaseId { get; set; } = ServiceCase.InboxId;

    /// <summary>Origine : nom du fichier, « Capture d'écran », « Caméra : … », « COM5 · 9600 8N1 »…</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Nom du fichier image enregistré à côté de l'entrée (sans dossier), s'il y en a un.</summary>
    public string? ImageFile { get; set; }

    /// <summary>Même image avec les codes encadrés et numérotés (rapport, vignette).</summary>
    public string? AnnotatedFile { get; set; }

    /// <summary>Codes lus (analyse d'image).</summary>
    public List<HistoryCode> Codes { get; set; } = [];

    /// <summary>Bilan de cohérence d'étiquette : « good », « warn », « bad », ou vide.</summary>
    public string LabelVerdict { get; set; } = string.Empty;

    public List<string> LabelNotes { get; set; } = [];

    /// <summary>Contenu réel du code (comparaison), en notation lisible.</summary>
    public string Expected { get; set; } = string.Empty;

    /// <summary>Données reçues de la douchette (comparaison), en notation lisible.</summary>
    public string Received { get; set; } = string.Empty;

    /// <summary>Symbologie annoncée par la douchette (HID POS).</summary>
    public string? AnnouncedSymbology { get; set; }

    /// <summary>Résumé du résultat (« Lecture conforme », « Problème de lecture »…).</summary>
    public string Verdict { get; set; } = string.Empty;

    /// <summary>Gravité la plus élevée.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Severity>))]
    public Severity Worst { get; set; }

    public List<HistoryFinding> Findings { get; set; } = [];

    /// <summary>Titre court pour les listes.</summary>
    [JsonIgnore]
    public string Title => Kind switch
    {
        HistoryKind.Comparison => string.IsNullOrEmpty(Verdict) ? "Comparaison douchette" : $"Comparaison douchette · {Verdict}",
        _ when Codes.Count == 0 => "Image sans code lisible",
        _ when Codes.Count == 1 => $"{Codes[0].Format} · {Codes[0].Kind}".TrimEnd(' ', '·'),
        _ => $"Étiquette · {Codes.Count} codes",
    };

    /// <summary>Texte de recherche (minuscules, sans accents).</summary>
    [JsonIgnore]
    public string SearchText => HistorySearch.Normalize(string.Join(" ",
        new[] { Source, Title, Expected, Received, AnnouncedSymbology ?? string.Empty, Verdict }
            .Concat(Codes.SelectMany(c => new[] { c.Format, c.Text, c.Kind }))
            .Concat(Findings.Select(f => f.Title))
            .Concat(LabelNotes)));
}

/// <summary>Recherche tolérante (casse et accents ignorés).</summary>
public static class HistorySearch
{
    public static string Normalize(string text)
    {
        var d = text.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (var c in d)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    /// <summary>Tous les mots de <paramref name="query"/> sont présents dans l'entrée.</summary>
    public static bool Matches(HistoryEntry entry, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var text = entry.SearchText;
        return Normalize(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(text.Contains);
    }
}
