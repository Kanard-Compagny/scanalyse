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

/// <summary>
/// Héberge l'Atelier (page web embarquée dans l'exécutable) dans le composant WebView2,
/// entièrement hors ligne, et relaie ses messages vers le programme.
/// </summary>
internal sealed class AtelierHost
{
    /// <summary>Nom d'hôte virtuel : la page est servie depuis le disque local, jamais depuis Internet.</summary>
    private const string Host = "scanalyse.local";

    private readonly WebView2 _view;

    public AtelierHost(WebView2 view) => _view = view;

    /// <summary>Déclenché quand l'utilisateur demande une comparaison depuis l'Atelier.</summary>
    public event Action<CompareRequest>? CompareRequested;

    private static string AppDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Scanalyse");

    /// <summary>Prépare le moteur web et charge l'Atelier.</summary>
    public async Task InitializeAsync()
    {
        var webFolder = ExtractPage();
        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: Path.Combine(AppDataFolder, "WebView2"));
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
                OpenExternal(e.Uri);
            }
        };

        _view.Source = new Uri($"https://{Host}/index.html");
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
            if (root.TryGetProperty("type", out var type) && type.GetString() == "compare")
            {
                string Get(string name) => root.TryGetProperty(name, out var v) ? v.GetString() ?? string.Empty : string.Empty;
                CompareRequested?.Invoke(new CompareRequest(Get("text"), Get("format"), Get("symbologyIdentifier")));
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

        var folder = Path.Combine(AppDataFolder, "Web", hash);
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
