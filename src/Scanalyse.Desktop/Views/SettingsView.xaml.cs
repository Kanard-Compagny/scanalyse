using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop.Views;

/// <summary>Paramètres : thème et informations.</summary>
public partial class SettingsView : UserControl
{
    private bool _syncing;

    public SettingsView()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? "Scanalyse" : $"Version {v.Major}.{v.Minor}.{v.Build} · analyse et diagnostic de lecture des codes-barres";
        Loaded += (_, _) => DiagBox.Text = Diagnostic();
        SyncTheme();
        ThemeManager.Changed += SyncTheme;
        Unloaded += (_, _) => ThemeManager.Changed -= SyncTheme;
        Loaded += (_, _) =>
        {
            ThemeManager.Changed -= SyncTheme;
            ThemeManager.Changed += SyncTheme;
            SyncTheme();
        };
    }

    private void SyncTheme()
    {
        _syncing = true;
        ThemeSystem.IsChecked = ThemeManager.Mode == AppTheme.System;
        ThemeLight.IsChecked = ThemeManager.Mode == AppTheme.Light;
        ThemeDark.IsChecked = ThemeManager.Mode == AppTheme.Dark;
        _syncing = false;
    }

    private void Theme_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        var mode = sender == ThemeLight ? AppTheme.Light : sender == ThemeDark ? AppTheme.Dark : AppTheme.System;
        if (mode != ThemeManager.Mode)
        {
            ThemeManager.Apply(mode, save: true);
        }
    }

    /// <summary>L'utilisateur choisit un autre dossier d'historique (chaîne vide : emplacement par défaut).</summary>
    public event Action<string>? HistoryFolderChangeRequested;

    /// <summary>Affiche le dossier d'historique en vigueur.</summary>
    public void ShowHistoryFolder(string path, bool isDefault)
    {
        HistoryPath.Text = path;
        ResetHistoryButton.Visibility = isDefault ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ChangeHistory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Dossier de l'historique Scanalyse (local ou réseau)",
            InitialDirectory = HistoryPath.Text,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            HistoryFolderChangeRequested?.Invoke(dialog.FolderName);
        }
    }

    private void ResetHistory_Click(object sender, RoutedEventArgs e) => HistoryFolderChangeRequested?.Invoke(string.Empty);

    private void OpenHistory_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(HistoryPath.Text);
        Process.Start(new ProcessStartInfo(HistoryPath.Text) { UseShellExecute = true });
    }

    /// <summary>Informations utiles au support : versions, emplacements, composants.</summary>
    private static string Diagnostic()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
        string webView;
        try
        {
            webView = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString() ?? "absent";
        }
        catch (Exception ex) when (ex is Microsoft.Web.WebView2.Core.WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or DllNotFoundException)
        {
            webView = "absent (installer « WebView2 Runtime » de Microsoft)";
        }

        var settings = AppSettings.Load();
        var adb = Relay.Adb.Find(settings.AdbPath) ?? "introuvable";
        return string.Join(Environment.NewLine,
            $"Scanalyse    {info}",
            $"Windows      {Environment.OSVersion.Version} ({(Environment.Is64BitOperatingSystem ? "64 bits" : "32 bits")})",
            $".NET         {Environment.Version}",
            $"WebView2     {webView}",
            $"ADB          {adb}",
            $"Programme    {Environment.ProcessPath}",
            $"Historique   {HistoryLocation.Current(settings)}",
            $"Réglages     {AppSettings.DataFolder}");
    }

    private void CopyDiag_Click(object sender, RoutedEventArgs e)
    {
        DiagBox.Text = Diagnostic();
        try
        {
            Clipboard.SetText(DiagBox.Text);
            ((Button)sender).Content = "Diagnostic copié";
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            DiagBox.SelectAll();
            DiagBox.Focus();
        }
    }

    private void Changelog_Click(object sender, RoutedEventArgs e) => OpenEmbedded("Scanalyse.Changelog.md", "Scanalyse - Quoi de neuf.txt");

    private void Licenses_Click(object sender, RoutedEventArgs e) => OpenEmbedded("Scanalyse.ThirdParty.md", "Scanalyse - Composants tiers.txt");

    /// <summary>Ouvre un texte embarqué dans le programme (Bloc-notes), sans dépendre d'un fichier à côté de l'exe.</summary>
    private static void OpenEmbedded(string resource, string fileName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        var path = Path.Combine(Path.GetTempPath(), fileName);
        File.WriteAllText(path, reader.ReadToEnd());
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        Process.Start(new ProcessStartInfo(AppSettings.DataFolder) { UseShellExecute = true });
    }
}
