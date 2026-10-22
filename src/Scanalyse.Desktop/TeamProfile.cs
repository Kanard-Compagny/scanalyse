using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scanalyse.Desktop;

/// <summary>
/// Profil d'équipe : les réglages communs d'un service SAV, dans un seul fichier à importer sur chaque poste. Le nom du
/// technicien, la langue, le thème et les dossiers personnels (sous le profil Windows) n'en font pas partie.
/// </summary>
internal sealed class TeamProfile
{
    public const string Extension = ".scanalyse-equipe";

    public string Format { get; set; } = "scanalyse-equipe";

    public int Version { get; set; } = 1;

    public DateTimeOffset Created { get; set; } = DateTimeOffset.Now;

    public string CreatedBy { get; set; } = string.Empty;

    public string ReportCompany { get; set; } = string.Empty;

    public string ReportContact { get; set; } = string.Empty;

    /// <summary>Logo des rapports, inclus dans le fichier (le chemin d'origine n'existe que sur le poste qui exporte).</summary>
    public string? LogoBase64 { get; set; }

    public string LogoExtension { get; set; } = ".png";

    public string HistoryFolder { get; set; } = string.Empty;

    public string ReportsFolder { get; set; } = string.Empty;

    public string LabelsFolder { get; set; } = string.Empty;

    public string UpdateFolder { get; set; } = string.Empty;

    public string PrintLanguage { get; set; } = "Zpl";

    public int PrintDpi { get; set; } = 203;

    public int RetentionDays { get; set; }

    public bool AutoRecord { get; set; } = true;

    public bool AutoCompare { get; set; } = true;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Dossier personnel (sous le profil Windows de ce poste) : inutile pour les autres, il n'est pas exporté.</summary>
    private static string Shared(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return folder.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? string.Empty : folder;
    }

    public static TeamProfile From(AppSettings s)
    {
        var p = new TeamProfile
        {
            CreatedBy = s.ReportTechnician,
            ReportCompany = s.ReportCompany,
            ReportContact = s.ReportContact,
            HistoryFolder = Shared(s.HistoryFolder),
            ReportsFolder = Shared(s.ReportsFolder),
            LabelsFolder = Shared(s.LabelsFolder),
            UpdateFolder = Shared(s.UpdateFolder),
            PrintLanguage = s.Print.Language,
            PrintDpi = s.Print.Dpi,
            RetentionDays = s.RetentionDays,
            AutoRecord = s.AutoRecord,
            AutoCompare = s.AutoCompare,
        };
        if (!string.IsNullOrWhiteSpace(s.ReportLogoPath) && File.Exists(s.ReportLogoPath) && new FileInfo(s.ReportLogoPath).Length < 4_000_000)
        {
            p.LogoBase64 = Convert.ToBase64String(File.ReadAllBytes(s.ReportLogoPath));
            p.LogoExtension = Path.GetExtension(s.ReportLogoPath).ToLowerInvariant();
        }

        return p;
    }

    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Json));

    /// <summary>Lit un profil ; <c>null</c> si le fichier n'en est pas un.</summary>
    public static TeamProfile? Load(string path)
    {
        try
        {
            var p = JsonSerializer.Deserialize<TeamProfile>(File.ReadAllText(path), Json);
            return p?.Format == "scanalyse-equipe" ? p : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Ce que l'import va régler, pour la confirmation.</summary>
    public IEnumerable<string> Summary()
    {
        if (ReportCompany.Length > 0)
        {
            yield return Localization.T("Société des rapports : ") + ReportCompany;
        }

        if (LogoBase64 is not null)
        {
            yield return Localization.T("Logo des rapports");
        }

        if (HistoryFolder.Length > 0)
        {
            yield return Localization.T("Historique partagé : ") + HistoryFolder;
        }

        if (ReportsFolder.Length > 0)
        {
            yield return Localization.T("Rapports : ") + ReportsFolder;
        }

        if (LabelsFolder.Length > 0)
        {
            yield return Localization.T("Étiquettes : ") + LabelsFolder;
        }

        if (UpdateFolder.Length > 0)
        {
            yield return Localization.T("Mises à jour : ") + UpdateFolder;
        }

        yield return string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Impression : {0}, {1} dpi"), PrintLanguage.ToUpperInvariant(), PrintDpi);
    }

    /// <summary>Applique le profil aux réglages de ce poste (le dossier d'historique est changé à part, avec sa copie).</summary>
    public void ApplyTo(AppSettings s)
    {
        s.ReportCompany = ReportCompany;
        s.ReportContact = ReportContact;
        if (LogoBase64 is not null)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                var logo = Path.Combine(AppSettings.DataFolder, "logo-equipe" + (LogoExtension.Length is > 1 and < 6 ? LogoExtension : ".png"));
                File.WriteAllBytes(logo, Convert.FromBase64String(LogoBase64));
                s.ReportLogoPath = logo;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
            {
            }
        }

        if (ReportsFolder.Length > 0)
        {
            s.ReportsFolder = ReportsFolder;
        }

        if (LabelsFolder.Length > 0)
        {
            s.LabelsFolder = LabelsFolder;
        }

        if (UpdateFolder.Length > 0)
        {
            s.UpdateFolder = UpdateFolder;
        }

        s.Print.Language = PrintLanguage;
        s.Print.Dpi = PrintDpi;
        s.RetentionDays = RetentionDays;
        s.AutoRecord = AutoRecord;
        s.AutoCompare = AutoCompare;
    }
}
