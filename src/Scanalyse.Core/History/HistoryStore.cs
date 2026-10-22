using System.Text.Json;

namespace Scanalyse.Core.History;

/// <summary>
/// Historique enregistré sur disque : un fichier JSON par entrée (et son image), rangés par mois,
/// plus la liste des dossiers SAV. Lisible et sauvegardable à la main, sans base de données.
/// </summary>
public sealed class HistoryStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly object _gate = new();
    private readonly List<HistoryEntry> _entries = [];
    private readonly List<ServiceCase> _cases = [];

    public HistoryStore(string folder)
    {
        Folder = folder;
    }

    /// <summary>Dossier racine (…\Scanalyse\Historique).</summary>
    public string Folder { get; }

    /// <summary>Un fichier par dossier : plusieurs postes peuvent partager le même historique (dossier réseau).</summary>
    private string CasesFolder => Path.Combine(Folder, "Dossiers");

    private string LegacyCasesFile => Path.Combine(Folder, "dossiers.json");

    /// <summary>Entrées, de la plus récente à la plus ancienne.</summary>
    public IReadOnlyList<HistoryEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>Dossiers SAV, « Non classé » en premier puis du plus récent au plus ancien.</summary>
    public IReadOnlyList<ServiceCase> Cases
    {
        get
        {
            lock (_gate)
            {
                return [.. _cases.OrderByDescending(c => c.IsInbox).ThenByDescending(c => c.Created)];
            }
        }
    }

    /// <summary>Lit tout l'historique ; les fichiers illisibles sont ignorés.</summary>
    public void Load()
    {
        lock (_gate)
        {
            _entries.Clear();
            _cases.Clear();
            // Ancienne version : tous les dossiers dans un seul fichier, découpé au passage.
            if (File.Exists(LegacyCasesFile))
            {
                try
                {
                    foreach (var c in JsonSerializer.Deserialize<List<ServiceCase>>(File.ReadAllText(LegacyCasesFile), Json) ?? [])
                    {
                        if (!c.IsInbox)
                        {
                            WriteCase(c);
                        }
                    }

                    File.Delete(LegacyCasesFile);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                }
            }

            if (Directory.Exists(CasesFolder))
            {
                foreach (var file in Directory.EnumerateFiles(CasesFolder, "*.json"))
                {
                    try
                    {
                        if (JsonSerializer.Deserialize<ServiceCase>(File.ReadAllText(file), Json) is { } c && !c.IsInbox)
                        {
                            _cases.Add(c);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            if (!_cases.Any(c => c.IsInbox))
            {
                _cases.Add(new ServiceCase { Id = ServiceCase.InboxId, Created = DateTimeOffset.MinValue });
            }

            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.EnumerateFiles(Folder, "*.json", SearchOption.AllDirectories))
                {
                    if (Path.GetFileName(file) == "dossiers.json" || Path.GetDirectoryName(file) == CasesFolder)
                    {
                        continue;
                    }

                    try
                    {
                        if (JsonSerializer.Deserialize<HistoryEntry>(File.ReadAllText(file), Json) is { } e)
                        {
                            _entries.Add(e);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                    {
                    }
                }
            }

            _entries.Sort((a, b) => b.At.CompareTo(a.At));
        }
    }

    /// <summary>Enregistre une entrée et son image (PNG ou JPEG), puis l'ajoute en tête.</summary>
    /// <param name="entry">Entrée à enregistrer.</param>
    /// <param name="image">Image d'origine, si disponible.</param>
    /// <param name="imageExtension">Extension du fichier image (« .jpg », « .png »).</param>
    /// <param name="annotated">Image avec les codes encadrés et numérotés, si disponible.</param>
    public HistoryEntry Add(HistoryEntry entry, byte[]? image = null, string imageExtension = ".jpg", byte[]? annotated = null)
    {
        var dir = MonthFolder(entry.At);
        Directory.CreateDirectory(dir);
        WriteImages(entry, image, imageExtension, annotated);
        File.WriteAllText(Path.Combine(dir, entry.Id + ".json"), JsonSerializer.Serialize(entry, Json));
        lock (_gate)
        {
            _entries.Insert(0, entry);
        }

        return entry;
    }

    /// <summary>Chemin complet de l'image d'une entrée, ou <c>null</c>.</summary>
    public string? ImagePath(HistoryEntry entry)
    {
        if (entry.ImageFile is null)
        {
            return null;
        }

        var path = Path.Combine(MonthFolder(entry.At), entry.ImageFile);
        return File.Exists(path) ? path : null;
    }

    /// <summary>Supprime une entrée et son image.</summary>
    public void Delete(HistoryEntry entry)
    {
        var dir = MonthFolder(entry.At);
        TryDelete(Path.Combine(dir, entry.Id + ".json"));
        if (entry.ImageFile is not null)
        {
            TryDelete(Path.Combine(dir, entry.ImageFile));
        }

        if (entry.AnnotatedFile is not null)
        {
            TryDelete(Path.Combine(dir, entry.AnnotatedFile));
        }

        lock (_gate)
        {
            _entries.RemoveAll(e => e.Id == entry.Id);
        }
    }

    /// <summary>Réécrit une entrée modifiée (son image reste inchangée).</summary>
    public void Update(HistoryEntry entry, byte[]? image = null, byte[]? annotated = null)
    {
        Directory.CreateDirectory(MonthFolder(entry.At));
        WriteImages(entry, image, Path.GetExtension(entry.ImageFile) is { Length: > 0 } ext ? ext : ".jpg", annotated);
        File.WriteAllText(Path.Combine(MonthFolder(entry.At), entry.Id + ".json"), JsonSerializer.Serialize(entry, Json));
    }

    private void WriteImages(HistoryEntry entry, byte[]? image, string extension, byte[]? annotated)
    {
        var dir = MonthFolder(entry.At);
        if (image is { Length: > 0 })
        {
            entry.ImageFile = entry.Id + extension;
            File.WriteAllBytes(Path.Combine(dir, entry.ImageFile), image);
        }

        if (annotated is { Length: > 0 })
        {
            entry.AnnotatedFile = entry.Id + "-codes.jpg";
            File.WriteAllBytes(Path.Combine(dir, entry.AnnotatedFile), annotated);
        }
    }

    /// <summary>Image annotée (codes encadrés) si elle existe, sinon l'image d'origine.</summary>
    public string? DisplayImagePath(HistoryEntry entry)
    {
        if (entry.AnnotatedFile is not null)
        {
            var path = Path.Combine(MonthFolder(entry.At), entry.AnnotatedFile);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return ImagePath(entry);
    }

    /// <summary>Supprime toutes les analyses d'un dossier (le dossier, lui, reste). Renvoie leur nombre.</summary>
    public int DeleteEntriesOf(string caseId)
    {
        var entries = EntriesOf(caseId);
        foreach (var e in entries)
        {
            Delete(e);
        }

        return entries.Count;
    }

    /// <summary>Range une entrée dans un autre dossier.</summary>
    public void Move(HistoryEntry entry, string caseId)
    {
        entry.CaseId = caseId;
        Update(entry);
    }

    /// <summary>Crée ou met à jour un dossier SAV.</summary>
    public void SaveCase(ServiceCase serviceCase)
    {
        lock (_gate)
        {
            if (!_cases.Contains(serviceCase))
            {
                _cases.RemoveAll(c => c.Id == serviceCase.Id);
                _cases.Add(serviceCase);
            }
        }

        if (!serviceCase.IsInbox)
        {
            WriteCase(serviceCase);
        }
    }

    /// <summary>Supprime un dossier ; ses entrées repassent dans « Non classé ».</summary>
    public void DeleteCase(ServiceCase serviceCase)
    {
        if (serviceCase.IsInbox)
        {
            return;
        }

        foreach (var e in Entries.Where(e => e.CaseId == serviceCase.Id))
        {
            Move(e, ServiceCase.InboxId);
        }

        lock (_gate)
        {
            _cases.RemoveAll(c => c.Id == serviceCase.Id);
        }

        TryDelete(Path.Combine(CasesFolder, serviceCase.Id + ".json"));
    }

    /// <summary>Dossiers d'un même client (nom comparé sans casse ni accents).</summary>
    public IReadOnlyList<ServiceCase> CasesOfClient(string client)
    {
        var key = HistorySearch.Normalize(client.Trim());
        return key.Length == 0 ? [] : [.. Cases.Where(c => !c.IsInbox && HistorySearch.Normalize(c.Client.Trim()) == key)];
    }

    public ServiceCase? FindCase(string id) => Cases.FirstOrDefault(c => c.Id == id);

    public IReadOnlyList<HistoryEntry> EntriesOf(string caseId) => [.. Entries.Where(e => e.CaseId == caseId)];

    private void WriteCase(ServiceCase c)
    {
        Directory.CreateDirectory(CasesFolder);
        File.WriteAllText(Path.Combine(CasesFolder, c.Id + ".json"), JsonSerializer.Serialize(c, Json));
    }

    private string MonthFolder(DateTimeOffset at) => Path.Combine(Folder, at.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
