using System.IO;

namespace Scanalyse.Desktop;

/// <summary>
/// Emplacement de l'historique : à côté du programme par défaut (il le suit sur une clé USB ou un partage),
/// ou un dossier choisi, par exemple sur le réseau pour toute l'équipe.
/// </summary>
internal static class HistoryLocation
{
    /// <summary>Emplacement des premières versions, repris automatiquement.</summary>
    public static string LegacyFolder => Path.Combine(AppSettings.DataFolder, "Historique");

    /// <summary>Dossier « Historique » à côté de Scanalyse.exe, s'il est accessible en écriture ; sinon le profil Windows.</summary>
    public static string DefaultFolder
    {
        get
        {
            var beside = Path.Combine(AppContext.BaseDirectory, "Historique");
            return IsWritable(beside) ? beside : LegacyFolder;
        }
    }

    /// <summary>Dossier en vigueur.</summary>
    public static string Current(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.HistoryFolder) ? DefaultFolder : settings.HistoryFolder;

    /// <summary>Le dossier contient déjà un historique.</summary>
    public static bool HasData(string folder) =>
        Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Any();

    /// <summary>Copie un historique vers un autre dossier, sans écraser ce qui s'y trouve déjà. Renvoie le nombre de fichiers copiés.</summary>
    public static int CopyAll(string from, string to)
    {
        if (!Directory.Exists(from) || Path.GetFullPath(from).TrimEnd('\\') == Path.GetFullPath(to).TrimEnd('\\'))
        {
            return 0;
        }

        var n = 0;
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            if (File.Exists(target))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
            n++;
        }

        return n;
    }

    public static bool IsWritable(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".scanalyse-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
