using System.Windows;

namespace Scanalyse.Desktop;

/// <summary>Point d'entrée : applique le thème mémorisé avant d'ouvrir la fenêtre.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.Initialize(AppSettings.Load().Theme);
        base.OnStartup(e);
    }
}
