using System.IO;
using System.Text.Json;

namespace Scanalyse.Desktop;

/// <summary>Préférences de l'utilisateur, enregistrées dans %LOCALAPPDATA%\Scanalyse\settings.json.</summary>
public sealed class AppSettings
{
    /// <summary>Thème choisi.</summary>
    public ThemeMode Theme { get; set; } = ThemeMode.System;

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
