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
        VersionText.Text = v is null ? "Scanalyse" : $"Scanalyse version {v.Major}.{v.Minor}.{v.Build}";
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

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        Process.Start(new ProcessStartInfo(AppSettings.DataFolder) { UseShellExecute = true });
    }
}
