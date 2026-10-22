using System.Globalization;
using System.Text.Json;

namespace Scanalyse.Core.Updates;

/// <summary>
/// Numéro de version de Scanalyse : « 0.10.1 », ou version de développement « 0.10.1-dev.35 » (toujours antérieure à la
/// version publiée du même numéro).
/// </summary>
/// <param name="Major">Majeur.</param>
/// <param name="Minor">Mineur.</param>
/// <param name="Patch">Correctif.</param>
/// <param name="Dev">Numéro de compilation de développement, ou <c>null</c> pour une version publiée.</param>
public sealed record AppVersion(int Major, int Minor, int Patch, int? Dev) : IComparable<AppVersion>
{
    /// <summary>Lit « v0.10.1 », « 0.10.1 », « 0.10.1-dev.35 » ou « 0.10.1+abc ». <c>null</c> si illisible.</summary>
    public static AppVersion? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var t = text.Trim().TrimStart('v', 'V');
        var plus = t.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            t = t[..plus];
        }

        int? dev = null;
        var dash = t.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            var suffix = t[(dash + 1)..];
            var digits = new string([.. suffix.Where(char.IsAsciiDigit)]);
            dev = int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d : 0;
            t = t[..dash];
        }

        var parts = t.Split('.');
        if (parts.Length < 2 || !parts.Take(3).All(p => int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        int N(int k) => k < parts.Length ? int.Parse(parts[k], CultureInfo.InvariantCulture) : 0;
        return new AppVersion(N(0), N(1), N(2), dev);
    }

    /// <inheritdoc/>
    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var c = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (c != 0)
        {
            return c;
        }

        // Une version publiée passe après toutes ses versions de développement.
        return (Dev ?? int.MaxValue).CompareTo(other.Dev ?? int.MaxValue);
    }

    /// <summary>Cette version est plus récente que <paramref name="other"/>.</summary>
    public bool IsNewerThan(AppVersion? other) => CompareTo(other) > 0;

    /// <inheritdoc/>
    public override string ToString() => $"{Major}.{Minor}.{Patch}" + (Dev is { } d ? $"-dev.{d}" : string.Empty);
}

/// <summary>Version publiée disponible.</summary>
/// <param name="Version">Numéro.</param>
/// <param name="DownloadUrl">Adresse de Scanalyse.exe (Internet) ou chemin du fichier (dossier d'équipe).</param>
/// <param name="Notes">Nouveautés (texte Markdown), si connues.</param>
/// <param name="Source">« GitHub » ou chemin du dossier d'équipe.</param>
public sealed record AvailableUpdate(AppVersion Version, string DownloadUrl, string Notes, string Source);

/// <summary>Lecture des réponses de GitHub (dernière version publiée).</summary>
public static class ReleaseFeed
{
    /// <summary>Lit la réponse de « releases/latest » : numéro, adresse de Scanalyse.exe et texte de la version.</summary>
    /// <param name="json">Réponse JSON de l'API GitHub.</param>
    /// <returns>La version, ou <c>null</c> si la réponse ne contient pas d'exécutable.</returns>
    public static AvailableUpdate? ParseGitHubLatest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("tag_name", out var tag) || AppVersion.Parse(tag.GetString()) is not { } version)
            {
                return null;
            }

            if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                return null;
            }

            string? url = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (a.TryGetProperty("name", out var name) && string.Equals(name.GetString(), "Scanalyse.exe", StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var u))
                    {
                        url = u.GetString();
                    }
                }
            }

            if (url is null)
            {
                return null;
            }

            var notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? string.Empty : string.Empty;
            return new AvailableUpdate(version, url, notes, "GitHub");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
