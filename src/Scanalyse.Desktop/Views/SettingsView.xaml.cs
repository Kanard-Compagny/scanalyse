using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop.Views;

/// <summary>Paramètres, par catégories : menu à gauche, une catégorie à la fois à droite.</summary>
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

    /// <summary>Affiche la catégorie choisie dans le menu de gauche (nom de son panneau dans CommandParameter).</summary>
    private void SettingsNav_Checked(object sender, RoutedEventArgs e)
    {
        if (Sections is null || sender is not RadioButton { CommandParameter: string target })
        {
            return;
        }

        foreach (var section in Sections.Children.OfType<FrameworkElement>())
        {
            section.Visibility = section.Name == target ? Visibility.Visible : Visibility.Collapsed;
        }

        SectionScroll.ScrollToTop();
    }

    /// <summary>Ouvre une catégorie des Paramètres (« SecUpdates », « SecAbout »…).</summary>
    public void ShowSection(string name)
    {
        foreach (var nav in new[] { NavGeneral, NavUpdates, NavHelp, NavReports, NavHistory, NavPrint, NavTeam, NavAbout, NavUninstall })
        {
            if (nav.CommandParameter as string == name)
            {
                nav.IsChecked = true;
            }
        }
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
        UpdateCheckBox.IsChecked = s.UpdateCheck;
        OpenWithCheck.IsChecked = s.OpenWith;
        ShowUpdateFolder(s.UpdateFolder);
        _loadingOptions = false;
    }

    /// <summary>Inscrit ou retire Scanalyse du menu « Ouvrir avec » (session de l'utilisateur).</summary>
    private void OpenWith_Click(object sender, RoutedEventArgs e)
    {
        var on = OpenWithCheck.IsChecked == true;
        try
        {
            if (on)
            {
                FileAssociation.Register();
            }
            else
            {
                FileAssociation.Unregister();
            }

            var s = AppSettings.Load();
            s.OpenWith = on;
            s.Save();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            OpenWithCheck.IsChecked = !on;
            _ = Dialogs.ConfirmAsync(Localization.T("Ouvrir avec Scanalyse"), Localization.T("Windows a refusé la modification : ") + ex.Message, "OK", null, danger: false);
        }
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
        s.UpdateCheck = UpdateCheckBox.IsChecked == true;
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
        s.LanguageChosen = true;
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
    /// <summary>Versions et emplacements (Paramètres → À propos, rapports d'erreur).</summary>
    internal static string Diagnostic()
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

    private void Changelog_Click(object sender, RoutedEventArgs e) => OpenEmbedded("Scanalyse.Changelog.md", "Quoi de neuf");

    private void License_Click(object sender, RoutedEventArgs e) => OpenEmbedded("Scanalyse.License.txt", "Licence GNU GPL v3");

    private void Licenses_Click(object sender, RoutedEventArgs e) => OpenEmbedded("Scanalyse.ThirdParty.md", "Composants tiers et licences");

    /// <summary>Ouvre un texte embarqué dans le programme, mis en forme dans une fenêtre aux couleurs de Scanalyse.</summary>
    private void OpenEmbedded(string resource, string title)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        new DocWindow(title, reader.ReadToEnd()) { Owner = Window.GetWindow(this) }.Show();
    }

    // ------------------------------------------------------------------ Mises à jour

    /// <summary>« Rechercher maintenant » : la fenêtre principale cherche et propose l'installation.</summary>
    public event Action? UpdateCheckRequested;

    /// <summary>Résultat de la dernière recherche de mise à jour.</summary>
    public void ShowUpdateStatus(string message, bool available)
    {
        UpdateStatus.Text = message;
        UpdateStatus.SetResourceReference(TextBlock.ForegroundProperty, available ? "BrandText" : "Muted");
    }

    private void ShowUpdateFolder(string folder)
    {
        UpdateFolderText.Text = string.IsNullOrWhiteSpace(folder) ? Localization.T("Aucun (GitHub seulement, si le dépôt est public)") : folder;
        UpdateFolderClear.Visibility = string.IsNullOrWhiteSpace(folder) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PickUpdateFolder_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = Localization.T("Dossier de diffusion de l'équipe (contient Scanalyse.exe)") };
        if (Directory.Exists(s.UpdateFolder))
        {
            dialog.InitialDirectory = s.UpdateFolder;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        s.UpdateFolder = dialog.FolderName;
        s.Save();
        ShowUpdateFolder(s.UpdateFolder);
        UpdateCheckRequested?.Invoke();
    }

    private void ClearUpdateFolder_Click(object sender, RoutedEventArgs e)
    {
        var s = AppSettings.Load();
        s.UpdateFolder = string.Empty;
        s.Save();
        ShowUpdateFolder(string.Empty);
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        UpdateStatus.Text = Localization.T("Recherche en cours…");
        UpdateCheckRequested?.Invoke();
    }

    // ------------------------------------------------------------------ Profil d'équipe

    private void ExportProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("Exporter le profil d'équipe"),
            FileName = "Profil Scanalyse" + TeamProfile.Extension,
            Filter = Localization.T("Profil d'équipe Scanalyse") + $" (*{TeamProfile.Extension})|*{TeamProfile.Extension}",
            DefaultExt = TeamProfile.Extension,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            TeamProfile.From(AppSettings.Load()).Save(dialog.FileName);
            ProfileStatus.Text = Localization.T("Profil enregistré : ") + dialog.FileName + " " + Localization.T("Dépose-le sur le partage de l'équipe, puis « Importer un profil » sur chaque poste.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ProfileStatus.Text = Localization.T("Export impossible : ") + ex.Message;
        }
    }

    private async void ImportProfile_Click(object sender, RoutedEventArgs e) => await ImportTeamProfileAsync();

    /// <summary>Choisit un profil d'équipe, montre ce qu'il règle, puis l'applique (aussi proposé à l'accueil).</summary>
    /// <returns><c>true</c> si un profil a été importé.</returns>
    public async Task<bool> ImportTeamProfileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("Importer un profil d'équipe"),
            Filter = Localization.T("Profil d'équipe Scanalyse") + $" (*{TeamProfile.Extension})|*{TeamProfile.Extension}",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return false;
        }

        return await ImportTeamProfileFileAsync(dialog.FileName);
    }

    /// <summary>Importe un profil d'équipe donné (double-clic sur le fichier, « Ouvrir avec »), après confirmation.</summary>
    public async Task<bool> ImportTeamProfileFileAsync(string path)
    {
        if (TeamProfile.Load(path) is not { } profile)
        {
            ProfileStatus.Text = Localization.T("Ce fichier n'est pas un profil d'équipe Scanalyse.");
            return false;
        }

        var lines = string.Join(Environment.NewLine, profile.Summary().Select(l => "• " + l));
        if (!await Dialogs.ConfirmAsync(Localization.T("Importer ce profil d'équipe ?"),
                lines + Environment.NewLine + Environment.NewLine + Localization.T("Ces réglages remplacent ceux de ce poste. Le nom du technicien et la langue ne changent pas."),
                Localization.T("Importer"), null, danger: false))
        {
            return false;
        }

        var s = AppSettings.Load();
        profile.ApplyTo(s);
        s.Save();
        if (profile.HistoryFolder.Length > 0 && !string.Equals(profile.HistoryFolder, s.HistoryFolder, StringComparison.OrdinalIgnoreCase))
        {
            HistoryFolderChangeRequested?.Invoke(profile.HistoryFolder);
        }

        LoadOptions();
        ShowFolders();
        ProfileStatus.Text = Localization.T("Profil importé.") + (profile.CreatedBy.Length > 0 ? " " + string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("(créé par {0})"), profile.CreatedBy) : string.Empty);
        if (profile.UpdateFolder.Length > 0)
        {
            UpdateCheckRequested?.Invoke();
        }

        return true;
    }

    // ------------------------------------------------------------------ Désinstallation

    /// <summary>Dossiers de données supprimés avec l'option : seulement les emplacements par défaut de Scanalyse.</summary>
    private static (List<string> Removed, List<string> Kept) UserDataFolders(AppSettings s)
    {
        var documents = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Scanalyse");
        var removed = new List<string>();
        var kept = new List<string>();
        void Add(string folder, bool isDefault)
        {
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            // Un dossier choisi à la main (partage réseau de l'équipe, dossier personnel…) n'est jamais effacé.
            (isDefault ? removed : kept).Add(folder);
        }

        Add(HistoryLocation.Current(s), string.IsNullOrWhiteSpace(s.HistoryFolder));
        Add(s.ReportsFolderOrDefault, string.IsNullOrWhiteSpace(s.ReportsFolder));
        Add(s.LabelsFolderOrDefault, string.IsNullOrWhiteSpace(s.LabelsFolder));
        removed.Add(documents);
        return (removed, kept);
    }

    private void UninstallData_Click(object sender, RoutedEventArgs e)
    {
        if (UninstallData.IsChecked != true)
        {
            UninstallFolders.Text = string.Empty;
            return;
        }

        var (removed, kept) = UserDataFolders(AppSettings.Load());
        var text = string.Join(Environment.NewLine, removed.Where(Directory.Exists).Select(f => "✕ " + f));
        if (kept.Count > 0)
        {
            text += (text.Length > 0 ? Environment.NewLine : string.Empty)
                + string.Join(Environment.NewLine, kept.Select(f => Localization.T("conservé (dossier choisi) : ") + f));
        }

        UninstallFolders.Text = text.Length > 0 ? text : Localization.T("Aucune donnée enregistrée pour l'instant.");
    }

    private void UninstallCancel_Click(object sender, RoutedEventArgs e)
    {
        UninstallWarning.Visibility = Visibility.Collapsed;
        UninstallCancel.Visibility = Visibility.Collapsed;
        UninstallButton.Content = Localization.T("Désinstaller…");
        UninstallButton.Style = (Style)FindResource("BtnSecondary");
    }

    private void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        if (UninstallWarning.Visibility != Visibility.Visible)
        {
            // Premier clic : on demande confirmation sur place.
            UninstallWarning.Visibility = Visibility.Visible;
            UninstallCancel.Visibility = Visibility.Visible;
            UninstallButton.Content = Localization.T("Confirmer la désinstallation");
            UninstallButton.Style = (Style)FindResource("BtnAction");
            return;
        }

        var s = AppSettings.Load();
        var folders = new List<string> { AppSettings.DataFolder };
        if (UninstallData.IsChecked == true)
        {
            folders.AddRange(UserDataFolders(s).Removed);
        }

        UninstallButton.IsEnabled = false;
        UninstallCancel.IsEnabled = false;
        UninstallWarning.Text = Localization.T("Nettoyage en cours…");
        UninstallRequested?.Invoke(folders);
    }

    /// <summary>
    /// Désinstallation confirmée : la fenêtre principale libère les appareils et le moteur web, efface ces dossiers elle-même,
    /// puis montre Scanalyse.exe dans l'Explorateur pour que l'utilisateur le supprime (aucun script caché).
    /// </summary>
    public event Action<IReadOnlyList<string>>? UninstallRequested;

    /// <summary>Règle de pare-feu du relais Wi-Fi (créée seulement si on l'a demandée) : retirée avec l'accord de Windows.</summary>
    public static void RemoveFirewallRule()
    {
        const string name = "Scanalyse - relais terminal";
        try
        {
            using var check = Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall show rule name=\"{name}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            });
            if (check is null)
            {
                return;
            }

            check.StandardOutput.ReadToEnd();
            check.WaitForExit(5000);
            if (check.ExitCode != 0)
            {
                return;
            }

            Process.Start(new ProcessStartInfo("netsh", $"advfirewall firewall delete rule name=\"{name}\"")
            {
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            })?.WaitForExit(15000);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Refusé ou indisponible : la règle reste, sans gêne (elle n'ouvre que le réseau local).
        }
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(AppSettings.DataFolder);
        Process.Start(new ProcessStartInfo(AppSettings.DataFolder) { UseShellExecute = true });
    }
}
