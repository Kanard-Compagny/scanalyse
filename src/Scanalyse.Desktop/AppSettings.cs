using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scanalyse.Desktop.Scanners;

namespace Scanalyse.Desktop;

/// <summary>Préférences de l'utilisateur, enregistrées dans %LOCALAPPDATA%\Scanalyse\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Thème choisi.</summary>
    public AppTheme Theme { get; set; } = AppTheme.System;

    /// <summary>Dossier de l'historique choisi par l'utilisateur ; vide = à côté du programme.</summary>
    public string HistoryFolder { get; set; } = string.Empty;

    /// <summary>Dossier SAV en cours (les analyses y sont enregistrées).</summary>
    public string ActiveCaseId { get; set; } = "inbox";

    /// <summary>Dernière douchette connectée (reconnectée au démarrage si elle est présente).</summary>
    public ScannerPreferences Scanner { get; set; } = new();

    /// <summary>Dossier des données de Scanalyse (réglages, cache de l'Atelier).</summary>
    public static string DataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scanalyse");

    private static string FilePath => Path.Combine(DataFolder, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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
