using System.IO;
using Microsoft.Web.WebView2.Core;

namespace Scanalyse.Desktop;

/// <summary>
/// Convertit un rapport HTML en PDF avec le moteur Edge déjà utilisé par l'Atelier, dans une vue invisible :
/// aucun composant PDF supplémentaire à installer.
/// </summary>
internal static class ReportPrinter
{
    public static async Task SavePdfAsync(CoreWebView2Environment env, IntPtr window, string html, string pdfPath)
    {
        var folder = Path.Combine(AppSettings.DataFolder, "Rapports");
        Directory.CreateDirectory(folder);
        var page = Path.Combine(folder, "rapport-en-cours.html");
        await File.WriteAllTextAsync(page, html);

        var controller = await env.CreateCoreWebView2ControllerAsync(window);
        try
        {
            controller.IsVisible = false;
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            controller.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            controller.CoreWebView2.Navigate(new Uri(page).AbsoluteUri);
            if (await Task.WhenAny(loaded.Task, Task.Delay(TimeSpan.FromSeconds(20))) != loaded.Task || !loaded.Task.Result)
            {
                throw new InvalidOperationException("la page du rapport ne s'est pas chargée.");
            }

            var settings = env.CreatePrintSettings();
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = 8.27;   // A4, en pouces
            settings.PageHeight = 11.69;
            settings.MarginTop = settings.MarginBottom = 0.5;
            settings.MarginLeft = settings.MarginRight = 0.47;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            if (!await controller.CoreWebView2.PrintToPdfAsync(pdfPath, settings))
            {
                throw new InvalidOperationException("le moteur Edge n'a pas pu créer le PDF (fichier ouvert dans un autre programme ?).");
            }
        }
        finally
        {
            controller.Close();
            try
            {
                File.Delete(page);
            }
            catch (IOException)
            {
            }
        }
    }
}
