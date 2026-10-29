using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scanalyse.Desktop.Scanners;

namespace Scanalyse.Desktop;

/// <summary>Préférences de l'utilisateur, enregistrées dans %LOCALAPPDATA%\Scanalyse\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Langue de l'interface : « fr » ou « en » (appliquée au démarrage).</summary>
    public string Language { get; set; } = "fr";

    /// <summary>Langue choisie au premier lancement (ou dans Paramètres) : la fenêtre de choix ne revient plus.</summary>
    public bool LanguageChosen { get; set; }

    /// <summary>Thème choisi.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Dossier de l'historique choisi par l'utilisateur ; vide = à côté du programme.</summary>
    public string HistoryFolder { get; set; } = string.Empty;

    /// <summary>Chemin d'adb.exe choisi à la main ; vide = recherche automatique.</summary>
    public string AdbPath { get; set; } = string.Empty;

    /// <summary>Relais terminal accessible en Wi-Fi (sinon seulement en USB via ADB).</summary>
    public bool RelayWifi { get; set; }

    /// <summary>Adresse du PC choisie pour le relais Wi-Fi (plusieurs cartes réseau possibles).</summary>
    public string RelayAddress { get; set; } = string.Empty;

    /// <summary>Contenu des codes dans les rapports : « Detailed », « Raw » ou « Both ».</summary>
    public string ReportContent { get; set; } = "Both";

    /// <summary>Dossier SAV en cours (les analyses y sont enregistrées).</summary>
    public string ActiveCaseId { get; set; } = "inbox";

    // ---- Rapport
    /// <summary>Société qui édite les rapports (en-tête).</summary>
    public string ReportCompany { get; set; } = string.Empty;

    /// <summary>Technicien par défaut (si le dossier n'en indique pas).</summary>
    public string ReportTechnician { get; set; } = string.Empty;

    /// <summary>Coordonnées imprimées en en-tête (téléphone, e-mail…).</summary>
    public string ReportContact { get; set; } = string.Empty;

    /// <summary>Logo de la société (fichier image), ou vide.</summary>
    public string ReportLogoPath { get; set; } = string.Empty;

    // ---- Démarrage et affichage
    /// <summary>Page ouverte au démarrage : analyse, create, print, bench, devices, history.</summary>
    public string StartPage { get; set; } = "analyse";

    /// <summary>Journal des lectures déplié au démarrage.</summary>
    public bool LogOpen { get; set; } = true;

    /// <summary>Taille de l'interface : 1, 1.15 ou 1.3.</summary>
    public double UiScale { get; set; } = 1;

    // ---- Historique
    /// <summary>Les analyses et comparaisons sont enregistrées automatiquement dans le dossier SAV en cours.</summary>
    public bool AutoRecord { get; set; } = true;

    /// <summary>Suppression automatique des analyses plus anciennes que ce nombre de jours (0 : jamais).</summary>
    public int RetentionDays { get; set; }

    // ---- Banc d'essai
    /// <summary>Chaque lecture reçue est comparée tout de suite à la référence.</summary>
    public bool AutoCompare { get; set; } = true;

    /// <summary>Chercher une nouvelle version au démarrage.</summary>
    public bool UpdateCheck { get; set; } = true;

    /// <summary>Scanalyse proposé dans « Ouvrir avec » de l'Explorateur (inscription pour l'utilisateur, retirée à la désinstallation).</summary>
    public bool OpenWith { get; set; }

    /// <summary>Dossier de diffusion de l'équipe (partage réseau contenant la dernière version de Scanalyse.exe).</summary>
    public string UpdateFolder { get; set; } = string.Empty;

    /// <summary>L'accueil du premier lancement a été vu (terminé ou passé).</summary>
    public bool WelcomeDone { get; set; }

    /// <summary>Dossier proposé pour enregistrer les rapports PDF ; vide = Documents\Scanalyse\Rapports.</summary>
    public string ReportsFolder { get; set; } = string.Empty;

    /// <summary>Dossier proposé pour ouvrir et enregistrer les programmes d'étiquettes ; vide = Documents\Scanalyse\Étiquettes.</summary>
    public string LabelsFolder { get; set; } = string.Empty;

    /// <summary>Dossier des rapports en vigueur (simple chemin : rien n'est créé tant qu'aucun rapport n'est enregistré).</summary>
    public string ReportsFolderOrDefault => Resolve(ReportsFolder, "Rapports");

    /// <summary>Dossier des programmes d'étiquettes en vigueur (simple chemin, rien n'est créé).</summary>
    public string LabelsFolderOrDefault => Resolve(LabelsFolder, "Étiquettes");

    /// <summary>Dossier des rapports, créé au moment d'en enregistrer un.</summary>
    public string ReportsFolderForSave() => Ensure(ReportsFolderOrDefault);

    /// <summary>Dossier des programmes d'étiquettes, créé au moment d'en enregistrer un.</summary>
    public string LabelsFolderForSave() => Ensure(LabelsFolderOrDefault);

    /// <summary>Dossier où ouvrir un fichier : le dossier choisi s'il existe, sinon Documents.</summary>
    public static string ExistingOrDocuments(string folder) =>
        Directory.Exists(folder) ? folder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private static string Resolve(string chosen, string name) =>
        string.IsNullOrWhiteSpace(chosen)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scanalyse", name)
            : chosen;

    private static string Ensure(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        return folder;
    }

    /// <summary>Réglages de l'onglet Impression (étiquette, langage, imprimante).</summary>
    public PrintPreferences Print { get; set; } = new();

    /// <summary>Dernière douchette connectée (reconnectée au démarrage si elle est présente).</summary>
    public ScannerPreferences Scanner { get; set; } = new();

    /// <summary>Dossier des données de Scanalyse (réglages, cache de l'Atelier).</summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scanalyse");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>Désinstallation en cours : plus aucun réglage n'est écrit (le dossier des données vient d'être effacé).</summary>
    public static bool Frozen { get; set; }

    /// <summary>Lit les préférences ; valeurs par défaut si le fichier est absent ou illisible.</summary>
    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    /// <summary>Enregistre les préférences (silencieux en cas d'échec : ce n'est qu'un confort).</summary>
    public void Save()
    {
        if (Frozen)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(DataFolder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>Douchette mémorisée.</summary>
public sealed class ScannerPreferences
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ScanSourceKind Kind { get; set; } = ScanSourceKind.Keyboard;

    /// <summary>Port (« COM5 ») ou identifiant Windows du scanner HID POS.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public SerialSettings Serial { get; set; } = new();
}

/// <summary>Derniers réglages de l'onglet Impression.</summary>
public sealed class PrintPreferences
{
    public string Language { get; set; } = "Zpl";

    public int Dpi { get; set; } = 203;

    public double WidthMm { get; set; } = 100;

    public double HeightMm { get; set; } = 50;

    /// <summary>« tcp » (adresse IP) ou « queue » (file Windows).</summary>
    public string Mode { get; set; } = "tcp";

    public string Address { get; set; } = string.Empty;

    public string Queue { get; set; } = string.Empty;
}
