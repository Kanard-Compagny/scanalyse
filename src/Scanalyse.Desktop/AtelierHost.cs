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
public sealed record CompareRequest(string Text, string Format, string SymbologyIdentifier);

/// <summary>Code trouvé par l'Atelier dans une image.</summary>
public sealed record DecodedCode(string Format, string Text);

/// <summary>
/// Héberge l'Atelier (page web embarquée dans l'exécutable) dans le composant WebView2,
/// entièrement hors ligne, et fait le lien dans les deux sens avec le programme.
/// </summary>
internal sealed class AtelierHost
{
    /// <summary>Nom d'hôte virtuel : la page est servie depuis le disque local, jamais depuis Internet.</summary>
    private const string Host = "scanalyse.local";

    private readonly WebView2 _view;
    private bool _ready;

    public AtelierHost(WebView2 view) => _view = view;

    /// <summary>La page est chargée et prête à recevoir des commandes.</summary>
    public event Action? Ready;

    /// <summary>L'utilisateur demande une comparaison depuis l'Atelier.</summary>
    public event Action<CompareRequest>? CompareRequested;

    /// <summary>L'Atelier a analysé une image (liste vide : aucun code trouvé).</summary>
    public event Action<string, IReadOnlyList<DecodedCode>>? Decoded;

    /// <summary>Prépare le moteur web et charge l'Atelier.</summary>
    public async Task InitializeAsync()
    {
        var webFolder = ExtractPage();
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(AppSettings.DataFolder, "WebView2"));
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

    /// <summary>Envoie une image PNG à analyser.</summary>
    public Task LoadImageAsync(byte[] png, string label) =>
        ExecuteAsync($"window.atelierLoadImageData('data:image/png;base64,{Convert.ToBase64String(png)}',{JsonSerializer.Serialize(label)})");

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
                    CompareRequested?.Invoke(new CompareRequest(Get(root, "text"), Get(root, "format"), Get(root, "symbologyIdentifier")));
                    break;

                case "decoded":
                    var items = new List<DecodedCode>();
                    if (root.TryGetProperty("items", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var it in arr.EnumerateArray())
                        {
                            items.Add(new DecodedCode(Get(it, "format"), Get(it, "text")));
                        }
                    }

                    Decoded?.Invoke(Get(root, "source"), items);
                    break;
            }
        }
        catch (JsonException)
        {
            // Message mal formé : ignoré.
        }
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
