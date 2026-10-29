using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using Scanalyse.Core.Updates;

namespace Scanalyse.Desktop;

/// <summary>
/// Mises à jour : une version plus récente est cherchée dans le dossier de diffusion de l'équipe (partage réseau contenant
/// Scanalyse.exe) et sur GitHub (si le dépôt est public). L'installation remplace l'exécutable par renommage, sans script :
/// l'ancien devient « .old.exe », effacé au lancement suivant.
/// </summary>
internal static class Updater
{
    private const string GitHubLatest = "https://api.github.com/repos/Kanard-Compagny/scanalyse/releases/latest";

    /// <summary>Version en cours d'exécution.</summary>
    public static AppVersion Current { get; } =
        AppVersion.Parse(Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
        ?? new AppVersion(0, 0, 0, 0);

    /// <summary>Cherche une version plus récente que celle en cours. <c>Update</c> est <c>null</c> si tout est à jour.</summary>
    public static async Task<(AvailableUpdate? Update, string Message)> CheckAsync(AppSettings settings)
    {
        var found = new List<AvailableUpdate>();
        var notes = new List<string>();
        if (!string.IsNullOrWhiteSpace(settings.UpdateFolder))
        {
            var exe = Path.Combine(settings.UpdateFolder, "Scanalyse.exe");
            try
            {
                if (File.Exists(exe) && AppVersion.Parse(FileVersionInfo.GetVersionInfo(exe).ProductVersion) is { } v)
                {
                    var changelog = Path.Combine(settings.UpdateFolder, "CHANGELOG.md");
                    var checksum = Path.Combine(settings.UpdateFolder, ReleaseFeed.ChecksumFileName);
                    found.Add(new AvailableUpdate(v, exe, File.Exists(changelog) ? await File.ReadAllTextAsync(changelog) : string.Empty, settings.UpdateFolder,
                        File.Exists(checksum) ? checksum : null));
                }
                else
                {
                    notes.Add(Localization.T("Dossier de l'équipe : Scanalyse.exe introuvable."));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                notes.Add(Localization.T("Dossier de l'équipe inaccessible : ") + ex.Message);
            }
        }

        try
        {
            using var http = Client(TimeSpan.FromSeconds(8));
            using var response = await http.GetAsync(GitHubLatest);
            if (response.IsSuccessStatusCode && ReleaseFeed.ParseGitHubLatest(await response.Content.ReadAsStringAsync()) is { } gh)
            {
                found.Add(gh);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Hors ligne ou dépôt privé : seul le dossier de l'équipe compte.
        }

        var best = found.Where(u => u.Version.IsNewerThan(Current)).OrderByDescending(u => u.Version).FirstOrDefault();
        if (best is not null)
        {
            return (best, string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Version {0} disponible ({1})."), best.Version, best.Source));
        }

        var message = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Scanalyse {0} est à jour."), Current);
        if (found.Count == 0 && string.IsNullOrWhiteSpace(settings.UpdateFolder))
        {
            message += " " + Localization.T("Indique le dossier de diffusion de l'équipe pour recevoir les nouvelles versions.");
        }

        return (null, string.Join(" ", notes.Prepend(message)));
    }

    /// <summary>Récupère la nouvelle version, remplace Scanalyse.exe et relance le programme.</summary>
    /// <returns>Message d'erreur, ou <c>null</c> si le redémarrage est lancé.</returns>
    public static async Task<string?> InstallAsync(AvailableUpdate update)
    {
        var exe = Environment.ProcessPath;
        if (exe is null)
        {
            return Localization.T("Emplacement du programme inconnu.");
        }

        var dir = Path.GetDirectoryName(exe)!;
        var name = Path.GetFileNameWithoutExtension(exe);
        var fresh = Path.Combine(dir, name + ".new.exe");
        var old = Path.Combine(dir, name + ".old.exe");
        try
        {
            if (update.DownloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                using var http = Client(TimeSpan.FromMinutes(5));
                await using var input = await http.GetStreamAsync(update.DownloadUrl);
                await using var output = File.Create(fresh);
                await input.CopyToAsync(output);
            }
            else
            {
                File.Copy(update.DownloadUrl, fresh, overwrite: true);
            }

            // Contrôle minimal : un exécutable Windows complet.
            var info = new FileInfo(fresh);
            using (var check = File.OpenRead(fresh))
            {
                if (info.Length < 1_000_000 || check.ReadByte() != 'M' || check.ReadByte() != 'Z')
                {
                    File.Delete(fresh);
                    return Localization.T("Le fichier reçu n'est pas un Scanalyse.exe valide.");
                }
            }

            // Empreinte SHA-256 publiée avec la version : un fichier abîmé ou modifié en route n'est jamais installé.
            if (update.ChecksumUrl is { } checksumUrl)
            {
                string publishedText;
                if (checksumUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    using var http = Client(TimeSpan.FromSeconds(30));
                    publishedText = await http.GetStringAsync(checksumUrl);
                }
                else
                {
                    publishedText = await File.ReadAllTextAsync(checksumUrl);
                }

                var published = ReleaseFeed.ParseSha256(publishedText);
                string actual;
                await using (var read = File.OpenRead(fresh))
                {
                    actual = Convert.ToHexStringLower(await System.Security.Cryptography.SHA256.HashDataAsync(read));
                }

                if (published is null || published != actual)
                {
                    File.Delete(fresh);
                    return Localization.T("Mise à jour refusée : l'empreinte SHA-256 du fichier téléchargé ne correspond pas à celle publiée. Réessaie plus tard, ou télécharge Scanalyse.exe depuis GitHub.");
                }
            }

            if (File.Exists(old))
            {
                File.Delete(old);
            }

            // Windows autorise à renommer un programme en cours d'exécution : la nouvelle version prend sa place.
            File.Move(exe, old);
            File.Move(fresh, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException or TaskCanceledException)
        {
            TryDelete(fresh);
            if (!File.Exists(exe) && File.Exists(old))
            {
                File.Move(old, exe);
            }

            return Localization.T("Mise à jour impossible : ") + ex.Message
                + (ex is UnauthorizedAccessException ? " " + Localization.T("Le dossier du programme est protégé : copie la nouvelle version à la main.") : string.Empty);
        }

        App.Restart();
        return null;
    }

    /// <summary>Efface l'ancienne version laissée par une mise à jour (au lancement suivant).</summary>
    public static void CleanupOld()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        TryDelete(Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".old.exe"));
        TryDelete(Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".new.exe"));
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scanalyse/" + Current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
