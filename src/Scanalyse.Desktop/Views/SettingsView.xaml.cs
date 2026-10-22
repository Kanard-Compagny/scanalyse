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
        Loaded += (_, _) => ShowFolders();
        Loaded += (_, _) => LoadOptions();
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

    // ------------------------------------------------------------------ Options

    private bool _loadingOptions;

    /// <summary>Taille de l'interface ou journal modifiés : la fenêtre principale applique tout de suite.</summary>
    public event Action? DisplaySettingsChanged;

    private static void Check(Panel chips, string tag)
    {
        foreach (var r in chips.Children.OfType<RadioButton>())
        {
            r.IsChecked = string.Equals(r.Tag as string, tag, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string? Selected(Panel chips) => chips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void LoadOptions()
    {
        _loadingOptions = true;
        var s = AppSettings.Load();
        Check(LanguageChips, s.Language);
        Check(StartChips, s.StartPage);
        Check(ScaleChips, s.UiScale.ToString(System.Globalization.CultureInfo.InvariantCulture));
        LogOpenCheck.IsChecked = s.LogOpen;
        AutoRecordCheck.IsChecked = s.AutoRecord;
        Check(RetentionChips, s.RetentionDays.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AutoCompareCheck.IsChecked = s.AutoCompare;
        Check(LangChips, s.Print.Language);
        Check(DpiChips, s.Print.Dpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
        CompanyBox.Text = s.ReportCompany;
        TechnicianBox.Text = s.ReportTechnician;
        ContactBox.Text = s.ReportContact;
        ShowLogo(s.ReportLogoPath);
        _loadingOptions = false;
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions || !IsLoaded)
        {
            return;
        }

        var s = AppSettings.Load();
        var scaleBefore = s.UiScale;
        s.StartPage = Selected(StartChips) ?? s.StartPage;
        if (double.TryParse(Selected(ScaleChips), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var scale))
        {
            s.UiScale = scale;
        }

        s.LogOpen = LogOpenCheck.IsChecked == true;
        s.AutoRecord = AutoRecordCheck.IsChecked == true;
        if (int.TryParse(Selected(RetentionChips), out var days))
        {
            s.RetentionDays = days;
        }

        s.AutoCompare = AutoCompareCheck.IsChecked == true;
        s.Print.Language = Selected(LangChips) ?? s.Print.Language;
        if (int.TryParse(Selected(DpiChips), out var dpi))
        {
            s.Print.Dpi = dpi;
        }

        s.Save();
        if (Math.Abs(scaleBefore - s.UiScale) > 0.001)
        {
            DisplaySettingsChanged?.Invoke();
        }
    }

    private void Language_Checked(object sender, RoutedEventArgs e)
    {
        if (_loadingOptions || !IsLoaded || Selected(LanguageChips) is not { } lang)
        {
            return;
        }

        var s = AppSettings.Load();
        s.Language = lang;
        s.Save();
        RestartButton.Visibility = lang == Localization.Language ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => App.Restart();

    private void Report_LostFocus(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.ReportCompany = CompanyBox.Text.Trim();
        s.ReportTechnician = TechnicianBox.Text.Trim();
        s.ReportContact = ContactBox.Text.Trim();
        s.Save();
    }

    private void LogoPick_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("Logo de la société (en-tête des rapports)"),
            Filter = Localization.T("Images (*.png;*.jpg;*.jpeg;*.svg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.svg;*.bmp;*.gif"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var s = AppSettings.Load();
            s.ReportLogoPath = dialog.FileName;
            s.Save();
            ShowLogo(dialog.FileName);
        }
    }

    private void LogoClear_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.ReportLogoPath = string.Empty;
        s.Save();
        ShowLogo(string.Empty);
    }

    private void ShowLogo(string path)
    {
        LogoPreview.Source = null;
        LogoName.Text = path.Length == 0 ? "Aucun" : Path.GetFileName(path);
        LogoClear.Visibility = path.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (path.Length == 0 || !File.Exists(path) || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.UriSource = new Uri(path);
            img.EndInit();
            LogoPreview.Source = img;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
        {
            LogoName.Text = "Image illisible : " + Path.GetFileName(path);
        }
    }

    /// <summary>« Revoir l'accueil » (Prise en main).</summary>
    public event Action? WelcomeRequested;

    /// <summary>« Lancer la visite guidée » (Prise en main).</summary>
    public event Action? TourRequested;

    private void Welcome_Click(object sender, RoutedEventArgs e) => WelcomeRequested?.Invoke();

    private void Tour_Click(object sender, RoutedEventArgs e) => TourRequested?.Invoke();

    private void ShowFolders()
    {
        var s = AppSettings.Load();
        ReportsPath.Text = s.ReportsFolderOrDefault;
        LabelsPath.Text = s.LabelsFolderOrDefault;
    }

    private void PickReports_Click(object sender, RoutedEventArgs e) => PickFolder("Dossier des rapports PDF", ReportsPath.Text, (s, f) => s.ReportsFolder = f);

    private void PickLabels_Click(object sender, RoutedEventArgs e) => PickFolder("Dossier des programmes d'étiquettes", LabelsPath.Text, (s, f) => s.LabelsFolder = f);

    private void PickFolder(string title, string current, Action<AppSettings, string> apply)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localization.T(title), InitialDirectory = current };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            var s = AppSettings.Load();
            apply(s, dialog.FolderName);
            s.Save();
            ShowFolders();
        }
    }

    private void OpenReports_Click(object sender, RoutedEventArgs e) => OpenFolder(ReportsPath.Text);

    private void OpenLabels_Click(object sender, RoutedEventArgs e) => OpenFolder(LabelsPath.Text);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

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
            Title = Localization.T("Dossier de l'historique Scanalyse (local ou réseau)"),
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
