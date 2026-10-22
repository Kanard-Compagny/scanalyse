using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Scanalyse.Desktop;

/// <summary>Demande de comparaison envoyée par l'Atelier (bouton « Comparer avec une lecture douchette »).</summary>
/// <param name="Text">Données brutes du code, telles qu'un scanner les transmet.</param>
/// <param name="Format">Symbologie lue (« Data Matrix »…).</param>
/// <param name="SymbologyIdentifier">Identifiant AIM (« ]d2 »…), si connu.</param>
/// <param name="ImagePng">Vignette du code découpée dans l'image analysée (PNG), si disponible.</param>
public sealed record CompareRequest(string Text, string Format, string SymbologyIdentifier, byte[]? ImagePng = null);

/// <summary>Demande « Code imprimante » envoyée par l'Atelier (analyse ou création).</summary>
/// <param name="Text">Contenu brut du code (caractères GS réels) ou forme « (01)… ».</param>
/// <param name="Format">Format lu ou type de l'Atelier.</param>
/// <param name="Gs1">Le code porte des données GS1 (FNC1).</param>
/// <param name="Source">Origine, pour l'affichage.</param>
public sealed record PrintRequest(string Text, string Format, bool Gs1, string Source);

/// <summary>Bilan de cohérence d'une étiquette à plusieurs codes.</summary>
/// <param name="Verdict">« good », « warn » ou « bad ».</param>
/// <param name="Problems">Incohérences et points à vérifier.</param>
public sealed record LabelCheck(string Verdict, IReadOnlyList<string> Problems);

/// <summary>Code trouvé par l'Atelier dans une image.</summary>
public sealed record DecodedCode(string Format, string Text, string SymbologyIdentifier = "", string Kind = "",
    string Health = "", string HealthClass = "", List<Scanalyse.Core.History.HistoryDetail>? Content = null,
    List<Scanalyse.Core.History.HistoryDetail>? Technical = null, List<Scanalyse.Core.History.HistoryNote>? Alerts = null, string Hex = "");

/// <summary>Résultat complet de l'analyse d'une image par l'Atelier.</summary>
/// <param name="Source">Origine (« Capture d'écran », nom de fichier, « Caméra : … »).</param>
/// <param name="Sequence">Numéro de l'image : une même image réanalysée (zone, recherche approfondie) garde le même.</param>
/// <param name="Record">À enregistrer dans l'historique (faux pour une image rouverte ou un code créé).</param>
/// <param name="Image">Image réduite (JPEG), pour l'historique.</param>
/// <param name="Codes">Codes lus.</param>
/// <param name="Label">Bilan de cohérence d'étiquette, s'il y a lieu.</param>
/// <param name="Annotated">Même image avec les codes encadrés et numérotés.</param>
public sealed record ImageAnalysis(string Source, int Sequence, bool Record, byte[]? Image, IReadOnlyList<DecodedCode> Codes, LabelCheck? Label, byte[]? Annotated = null);

/// <summary>
/// Héberge l'Atelier (page web embarquée dans l'exécutable) dans le composant WebView2,
/// entièrement hors ligne, et fait le lien dans les deux sens avec le programme.
/// </summary>
internal sealed class AtelierHost
{
    /// <summary>Nom d'hôte virtuel : la page est servie depuis le disque local, jamais depuis Internet.</summary>
    private const string Host = "scanalyse.local";

    private readonly WebView2 _view;
    private readonly Dictionary<string, TaskCompletionSource<(byte[]? Png, string? Error)>> _renders = new();
    private bool _ready;

    public AtelierHost(WebView2 view) => _view = view;

    /// <summary>La page est chargée et prête à recevoir des commandes.</summary>
    public event Action? Ready;

    /// <summary>L'utilisateur demande une comparaison depuis l'Atelier.</summary>
    public event Action<CompareRequest>? CompareRequested;

    /// <summary>L'utilisateur veut le programme d'impression d'un code (ZPL…).</summary>
    public event Action<PrintRequest>? PrintRequested;

    /// <summary>L'Atelier a analysé une image (liste vide : aucun code trouvé).</summary>
    public event Action<ImageAnalysis>? Analyzed;

    /// <summary>Environnement du moteur web (pour générer les rapports PDF), disponible après l'initialisation.</summary>
    public CoreWebView2Environment? Environment { get; private set; }

    /// <summary>L'Atelier est passé de lui-même en mode « scan » ou « create » (par exemple « Recréer ce code »).</summary>
    public event Action<string>? ModeChanged;

    /// <summary>Prépare le moteur web et charge l'Atelier.</summary>
    public async Task InitializeAsync()
    {
        var webFolder = ExtractPage();
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(AppSettings.DataFolder, "WebView2"));
        Environment = env;
        await _view.EnsureCoreWebView2Async(env);

        var core = _view.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(Host, webFolder, CoreWebView2HostResourceAccessKind.DenyCors);
        core.WebMessageReceived += OnWebMessage;

        // Caméra : autorisée pour la page de l'Atelier uniquement (lecture en direct). Windows garde le dernier mot
        // via ses réglages de confidentialité.
        core.PermissionRequested += (_, e) =>
        {
            if (e.PermissionKind == CoreWebView2PermissionKind.Camera
                && e.Uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase))
            {
                e.State = CoreWebView2PermissionState.Allow;
            }
        };
        core.NavigationCompleted += (_, e) =>
        {
            _ready = e.IsSuccess;
            if (_ready)
            {
                Ready?.Invoke();
            }
        };

        // Les liens externes (pages web lues dans un QR) s'ouvrent dans le navigateur par défaut, pas dans le programme.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenExternal(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            var uri = e.Uri;
            var internalPage = uri.StartsWith($"https://{Host}/", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase);
            if (!internalPage)
            {
                e.Cancel = true;
                OpenExternal(uri);
            }
        };

        _view.Source = new Uri($"https://{Host}/index.html");
    }

    /// <summary>Aligne l'Atelier sur le thème du programme.</summary>
    public Task SetThemeAsync(bool dark) =>
        ExecuteAsync($"document.documentElement.dataset.theme='{(dark ? "dark" : "light")}'");

    /// <summary>Affiche la partie « créer » ou « analyser » de l'Atelier.</summary>
    public Task SetModeAsync(string mode) =>
        ExecuteAsync($"window.atelierSetMode&&window.atelierSetMode({JsonSerializer.Serialize(mode)},false)");

    /// <summary>Ouvre « Créer un code » avec un contenu brut (GS réels) et un type donné.</summary>
    public Task CreateFromAsync(string text, string typeId) =>
        ExecuteAsync($"window.atelierCreateFrom&&window.atelierCreateFrom({JsonSerializer.Serialize(text)},{JsonSerializer.Serialize(typeId)})");

    /// <summary>Ouvre « Créer un code » sur un type donné (identifiant de l'Atelier : « ean13 », « gs1datamatrix »…).</summary>
    public Task SetCreateTypeAsync(string typeId) =>
        ExecuteAsync($"window.atelierSetCreateType&&window.atelierSetCreateType({JsonSerializer.Serialize(typeId)})");

    /// <summary>
    /// Fait dessiner un code par l'Atelier (aperçu du banc d'essai). Renvoie l'image PNG, ou un message d'erreur.
    /// </summary>
    public async Task<(byte[]? Png, string? Error)> RenderAsync(string text, string typeId)
    {
        if (!_ready || _view.CoreWebView2 is null)
        {
            return (null, "L'Atelier n'est pas encore prêt.");
        }

        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<(byte[]? Png, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);
        _renders[id] = tcs;
        await ExecuteAsync($"window.atelierRender({JsonSerializer.Serialize(id)},{JsonSerializer.Serialize(text)},{JsonSerializer.Serialize(typeId)})");
        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        _renders.Remove(id);
        return done == tcs.Task ? tcs.Task.Result : (null, "Délai dépassé.");
    }

    /// <summary>Convertit une image « data:image/png;base64,… » en octets.</summary>
    public static byte[]? FromDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0 || !dataUrl.StartsWith("data:image/", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(dataUrl[(comma + 1)..]);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Ouvre la lecture en direct ; <paramref name="cameraName"/> choisit la caméra par son nom Windows.</summary>
    public Task StartCameraAsync(string? cameraName = null) =>
        ExecuteAsync($"window.atelierStartCamera&&window.atelierStartCamera({JsonSerializer.Serialize(cameraName ?? string.Empty)})");

    /// <summary>Coupe la caméra (on quitte la page d'analyse).</summary>
    public Task StopCameraAsync() => ExecuteAsync("window.atelierStopCamera&&window.atelierStopCamera()");

    /// <summary>Envoie une image à analyser (PNG par défaut, ou tout format lu par le navigateur).</summary>
    public Task LoadImageAsync(byte[] image, string label, string mime = "image/png", bool record = true) =>
        ExecuteAsync($"window.atelierLoadImageData('data:{mime};base64,{Convert.ToBase64String(image)}',{JsonSerializer.Serialize(label)},{{record:{(record ? "true" : "false")}}})");

    private async Task ExecuteAsync(string script)
    {
        if (!_ready || _view.CoreWebView2 is null)
        {
            return;
        }

        await _view.CoreWebView2.ExecuteScriptAsync(script);
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try
        {
            json = e.TryGetWebMessageAsString();
        }
        catch (ArgumentException)
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string Get(JsonElement el, string name) =>
                el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

            switch (Get(root, "type"))
            {
                case "compare":
                    CompareRequested?.Invoke(new CompareRequest(Get(root, "text"), Get(root, "format"), Get(root, "symbologyIdentifier"),
                        FromDataUrl(Get(root, "image"))));
                    break;

                case "print":
                    PrintRequested?.Invoke(new PrintRequest(Get(root, "text"), Get(root, "format"),
                        root.TryGetProperty("gs1", out var g) && g.ValueKind == JsonValueKind.True, Get(root, "source")));
                    break;

                case "mode":
                    ModeChanged?.Invoke(Get(root, "mode"));
                    break;

                case "rendered":
                    if (_renders.TryGetValue(Get(root, "id"), out var tcs))
                    {
                        var error = Get(root, "error");
                        tcs.TrySetResult(error.Length > 0 ? (null, error) : (FromDataUrl(Get(root, "image")), null));
                    }

                    break;

                case "decoded":
                    var items = new List<DecodedCode>();
                    if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            items.Add(new DecodedCode(Get(it, "format"), Get(it, "text"), Get(it, "symbologyIdentifier"), Get(it, "kind"),
                                Get(it, "health"), Get(it, "healthClass"), Pairs(it, "rows"), Pairs(it, "spec"),
                                [.. Pairs(it, "alerts").Select(p => new Scanalyse.Core.History.HistoryNote(p.Label, p.Value))], Get(it, "hex")));
                        }
                    }

                    LabelCheck? check = null;
                    if (root.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.Object)
                    {
                        var problems = new List<string>();
                        if (label.TryGetProperty("notes", out var notes) && notes.ValueKind == JsonValueKind.Array)
                        {
                            problems.AddRange(notes.EnumerateArray().Where(n => n.ValueKind == JsonValueKind.String).Select(n => n.GetString() ?? string.Empty));
                        }

                        check = new LabelCheck(Get(label, "verdict"), problems);
                    }

                    var seq = root.TryGetProperty("seq", out var s) && s.TryGetInt32(out var n) ? n : 0;
                    var record = !root.TryGetProperty("record", out var rec) || rec.ValueKind != JsonValueKind.False;
                    Analyzed?.Invoke(new ImageAnalysis(Get(root, "source"), seq, record, FromDataUrl(Get(root, "image")), items, check, FromDataUrl(Get(root, "annotated"))));
                    break;
            }
        }
        catch (JsonException)
        {
            // Message mal formé : ignoré.
        }
    }

    /// <summary>Lit un tableau JSON de paires [libellé, valeur].</summary>
    private static List<Scanalyse.Core.History.HistoryDetail> Pairs(JsonElement el, string name)
    {
        var list = new List<Scanalyse.Core.History.HistoryDetail>();
        if (el.TryGetProperty(name, out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in arr.EnumerateArray())
            {
                if (p.ValueKind == JsonValueKind.Array && p.GetArrayLength() >= 2)
                {
                    list.Add(new(p[0].GetString() ?? string.Empty, p[1].GetString() ?? string.Empty));
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Copie la page embarquée sur le disque (une fois par version) pour la servir via l'hôte virtuel.
    /// </summary>
    private static string ExtractPage()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Scanalyse.Web.index.html")
            ?? throw new InvalidOperationException("La page de l'Atelier est absente de l'exécutable.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];

        var folder = Path.Combine(AppSettings.DataFolder, "Web", hash);
        var file = Path.Combine(folder, "index.html");
        if (!File.Exists(file))
        {
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(file, bytes);
        }

        return folder;
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
