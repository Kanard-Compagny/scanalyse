using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;
using Scanalyse.Core.History;
using Scanalyse.Core.Text;
using Scanalyse.Desktop.Scanners;
using Scanalyse.Desktop.Views;

namespace Scanalyse.Desktop;

/// <summary>
/// Fenêtre principale : barre de titre, barre d'outils, menu latéral, pages et journal des lectures.
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxLogEntries = 500;
    private const double SidebarWide = 236;
    private const double SidebarNarrow = 66;

    private readonly AtelierHost _atelier;
    private HistoryStore _history = new(HistoryLocation.Current(AppSettings.Load()));
    private int _lastImageSequence = -1;
    private HistoryEntry? _lastImageEntry;
    private HistoryEntry? _lastComparison;
    private HistoryEntry? _reopened;
    private readonly ObservableCollection<LogEntry> _log = [];
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private uint _clipboardSequence;
    private DateTime _captureDeadline;
    private string _atelierMode = "scan";
    private bool _ready;

    public MainWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? string.Empty : $"version {v.Major}.{v.Minor}.{v.Build}";
        LogList.ItemsSource = _log;

        _atelier = new AtelierHost(Web);
        _atelier.Ready += OnAtelierReady;
        _atelier.CompareRequested += OnCompareRequested;
        _atelier.Analyzed += OnAnalyzed;
        // Le moteur web s'affiche toujours au-dessus des éléments WPF : on le masque le temps d'une confirmation.
        Confirm.IsVisibleChanged += (_, _) =>
        {
            if (Confirm.IsVisible && Web.Visibility == Visibility.Visible)
            {
                Web.Visibility = Visibility.Hidden;
            }
            else if (!Confirm.IsVisible && Web.Visibility == Visibility.Hidden)
            {
                Web.Visibility = Visibility.Visible;
            }
        };
        HistoryPage.ActiveCaseChanged += OnActiveCaseChanged;
        HistoryPage.ReopenRequested += OnReopenRequested;
        HistoryPage.ReportRequested = GenerateReportAsync;
        HistoryPage.Log += (kind, text) => AddLog(kind, "HISTORIQUE", text);
        SettingsPage.HistoryFolderChangeRequested += async path => await ChangeHistoryFolderAsync(path);
        Bench.Compared += OnBenchCompared;
        Bench.Renderer = (text, type) => _atelier.RenderAsync(text, type);
        Devices.Renderer = (text, type) => _atelier.RenderAsync(text, type);
        Bench.RecreateRequested += async (text, type) =>
        {
            NavCreate.IsChecked = true;
            await _atelier.CreateFromAsync(text, type);
            AddLog(LogKind.Info, "CRÉER", "Contenu repris du banc d'essai : choisis le type de code si besoin, puis exporte ou imprime.");
        };
        Bench.PrintRequested += (text, type) => OpenPrint(text, PrintView.SymbologyFor(type, text), "banc d'essai");
        PrintPage.Renderer = (text, type) => _atelier.RenderAsync(text, type);
        PrintPage.Log += (kind, text) => AddLog(kind, "IMPRESSION", text);
        PrintPage.LabelRenderer = json => _atelier.RenderLabelAsync(json);
        PrintPage.AnalyzeRequested += async (png, source) =>
        {
            NavAnalyse.IsChecked = true;
            await _atelier.LoadImageAsync(png, source);
            AddLog(LogKind.Info, "IMPRESSION", "Étiquette du programme ZPL envoyée à l'analyse : chaque code est relu comme sur une photo.");
        };
        _atelier.PrintRequested += r => OpenPrint(r.Text, PrintView.SymbologyFor(r.Format, r.Text, r.Gs1), r.Source);
        _atelier.ModeChanged += OnAtelierModeChanged;
        Symbols.CreateRequested += OnCreateRequested;
        Devices.Scanned += OnDeviceScanned;
        Devices.StatusChanged += OnScannerStatus;
        Devices.Log += (kind, text) => AddLog(kind, "DOUCHETTE", text);
        Devices.CameraRequested += async name => await StartCameraAsync(name);
        ThemeManager.Changed += OnThemeChanged;
        _captureTimer.Tick += CaptureTimer_Tick;

        InputBindings.Add(new KeyBinding(new RelayCommand(() => Paste_Click(this, new RoutedEventArgs())), Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(new RelayCommand(() => Import_Click(this, new RoutedEventArgs())), Key.O, ModifierKeys.Control));

        _ready = true;
        NavAnalyse.IsChecked = true;
        OnThemeChanged();
        AddLog(LogKind.Info, "SCANALYSE", "Prêt. L'analyse est faite sur ce PC, rien n'est envoyé sur Internet.");

        Loaded += async (_, _) =>
        {
            // L'Atelier d'abord ; la recherche des douchettes tourne à côté sans jamais le bloquer.
            _ = InitializeDevicesAsync();
            await LoadHistoryAsync();
            await StartAtelierAsync();
        };
        Closed += async (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            await Devices.ShutdownAsync();
        };
    }

    // ------------------------------------------------------------------ Atelier

    private async Task StartAtelierAsync()
    {
        try
        {
            await _atelier.InitializeAsync();
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWebError("Le composant Microsoft Edge WebView2 n'est pas installé sur ce PC. Il est présent d'office sur Windows 11 et sur les Windows 10 à jour ; sinon, installe le « WebView2 Runtime » depuis le site de Microsoft. Le banc d'essai reste utilisable.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            ShowWebError("Impossible de démarrer l'Atelier : " + ex.Message);
        }
    }

    private async void OnAtelierReady()
    {
        await _atelier.SetThemeAsync(ThemeManager.IsDark);
        await _atelier.SetModeAsync(_atelierMode);
    }

    /// <summary>L'Atelier a changé de mode tout seul (ex. « Recréer ce code ») : le menu suit.</summary>
    private void OnAtelierModeChanged(string mode)
    {
        if (AtelierPage.Visibility != Visibility.Visible)
        {
            return;
        }

        if (mode == "create" && NavCreate.IsChecked != true)
        {
            NavCreate.IsChecked = true;
        }
        else if (mode == "scan" && NavAnalyse.IsChecked != true)
        {
            NavAnalyse.IsChecked = true;
        }
    }

    /// <summary>« Créer ce type de code » depuis la page Symbologies.</summary>
    private async void OnCreateRequested(string typeId)
    {
        NavCreate.IsChecked = true;
        await _atelier.SetCreateTypeAsync(typeId);
    }

    /// <summary>Ouvre l'onglet « Code imprimante » avec un code venu d'un autre onglet.</summary>
    private void OpenPrint(string text, Scanalyse.Core.Printing.LabelSymbology symbology, string source)
    {
        PrintPage.Load(text, symbology, source);
        NavPrint.IsChecked = true;
        AddLog(LogKind.Info, "IMPRESSION", $"Code repris ({source}) : choisis le langage de l'imprimante, puis copie, enregistre ou envoie le programme.");
    }

    private void ShowWebError(string message)
    {
        Web.Visibility = Visibility.Collapsed;
        WebErrorText.Text = message;
        WebError.Visibility = Visibility.Visible;
        AddLog(LogKind.Error, "ATELIER", message);
    }

    private void OnCompareRequested(CompareRequest request)
    {
        Bench.SetExpected(request);
        NavBench.IsChecked = true;
        AddLog(LogKind.Info, "COMPARER", $"Référence reprise de l'image ({request.Format}) : scanne le même code avec la douchette.");
    }

    private void OnAnalyzed(ImageAnalysis a)
    {
        var codes = a.Codes;
        if (codes.Count == 0)
        {
            AddLog(LogKind.Warning, "IMAGE", $"{a.Source} : aucun code détecté.");
        }
        else
        {
            AddLog(LogKind.Ok, "IMAGE", $"{a.Source} : {codes.Count} code{(codes.Count > 1 ? "s" : string.Empty)} détecté{(codes.Count > 1 ? "s" : string.Empty)}.");
            foreach (var c in codes)
            {
                AddLog(LogKind.Info, c.Format.ToUpperInvariant(), c.Text);
            }
        }

        if (a.Label is { } label)
        {
            OnLabelChecked(label);
        }

        if (a.Record)
        {
            RecordImage(a);
        }
    }

    private void OnLabelChecked(LabelCheck check)
    {
        if (check.Verdict == "good")
        {
            AddLog(LogKind.Ok, "ÉTIQUETTE", "Étiquette cohérente : les données sont identiques d'un code à l'autre et respectent les règles GS1.");
            return;
        }

        var kind = check.Verdict == "bad" ? LogKind.Error : LogKind.Warning;
        AddLog(kind, "ÉTIQUETTE", check.Verdict == "bad" ? "Incohérences entre les codes de l'étiquette :" : "Étiquette à vérifier :");
        foreach (var p in check.Problems)
        {
            AddLog(kind, "ÉTIQUETTE", p);
        }
    }

    private void OnBenchCompared(BenchSnapshot snapshot)
    {
        var result = snapshot.Result;
        RecordComparison(snapshot);
        AddLog(LogKind.Info, "REÇU", snapshot.Received.Length > 0 ? snapshot.Received : "(rien)");
        var main = result.Findings.FirstOrDefault();
        if (main is null)
        {
            return;
        }

        var kind = result.WorstSeverity switch
        {
            Severity.Ok => LogKind.Ok,
            Severity.Info => result.DataMatches ? LogKind.Ok : LogKind.Info,
            Severity.Warning => LogKind.Warning,
            _ => LogKind.Error,
        };
        AddLog(kind, "DIAGNOSTIC", main.Title);
    }

    // ------------------------------------------------------------------ Historique et rapports

    private async Task LoadHistoryAsync()
    {
        try
        {
            // Première ouverture après la mise à jour : l'historique du profil Windows rejoint le nouvel emplacement.
            var settings = AppSettings.Load();
            if (string.IsNullOrWhiteSpace(settings.HistoryFolder) && _history.Folder != HistoryLocation.LegacyFolder
                && HistoryLocation.HasData(HistoryLocation.LegacyFolder) && !HistoryLocation.HasData(_history.Folder))
            {
                var copied = await Task.Run(() => HistoryLocation.CopyAll(HistoryLocation.LegacyFolder, _history.Folder));
                AddLog(LogKind.Info, "HISTORIQUE", $"Historique déplacé à côté du programme ({copied} fichiers) : {_history.Folder}");
            }

            ShowHistoryFolder();
            await Task.Run(_history.Load);
            HistoryPage.Attach(_history, AppSettings.Load().ActiveCaseId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Warning, "HISTORIQUE", "Historique illisible : " + ex.Message);
        }
    }

    private void ShowHistoryFolder() =>
        SettingsPage.ShowHistoryFolder(_history.Folder, string.IsNullOrWhiteSpace(AppSettings.Load().HistoryFolder));

    /// <summary>Change le dossier de l'historique : l'existant y est copié s'il est vide, puis tout est relu.</summary>
    private async Task ChangeHistoryFolderAsync(string path)
    {
        var target = string.IsNullOrWhiteSpace(path) ? HistoryLocation.DefaultFolder : path;
        if (!HistoryLocation.IsWritable(target))
        {
            AddLog(LogKind.Error, "HISTORIQUE", $"Impossible d'écrire dans {target} : choisis un dossier où tu as les droits d'écriture.");
            return;
        }

        try
        {
            var copied = HistoryLocation.HasData(target) ? 0 : await Task.Run(() => HistoryLocation.CopyAll(_history.Folder, target));
            var settings = AppSettings.Load();
            settings.HistoryFolder = path;
            settings.Save();
            _history = new HistoryStore(target);
            _lastImageEntry = null;
            _lastComparison = null;
            await Task.Run(_history.Load);
            HistoryPage.Attach(_history, settings.ActiveCaseId);
            ShowHistoryFolder();
            AddLog(LogKind.Ok, "HISTORIQUE", copied > 0
                ? $"Historique déplacé vers {target} ({copied} fichiers copiés ; l'ancien dossier est conservé)."
                : $"Historique : {target}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Error, "HISTORIQUE", "Changement de dossier impossible : " + ex.Message);
        }
    }

    private void OnActiveCaseChanged(ServiceCase c)
    {
        CaseLabel.Text = "Dossier : " + c.DisplayName;
        var settings = AppSettings.Load();
        if (settings.ActiveCaseId != c.Id)
        {
            settings.ActiveCaseId = c.Id;
            settings.Save();
        }
    }

    private void CaseButton_Click(object sender, RoutedEventArgs e) => NavHistory.IsChecked = true;

    /// <summary>
    /// Enregistre une analyse d'image. Une même image réanalysée (zone, recherche approfondie) met à jour
    /// son entrée au lieu d'en créer une nouvelle.
    /// </summary>
    private void RecordImage(ImageAnalysis a)
    {
        try
        {
            var worst = a.Codes.Count == 0 ? Severity.Warning
                : a.Label?.Verdict == "bad" ? Severity.Error
                : a.Label?.Verdict == "warn" ? Severity.Warning
                : Severity.Ok;
            var codes = a.Codes.Select(c => new HistoryCode(c.Format, c.Text, c.SymbologyIdentifier, c.Kind,
                c.Health, c.HealthClass, c.Content, c.Technical, c.Alerts, c.Hex)).ToList();
            // Analyse rouverte depuis l'historique : elle complète son entrée (fiches, image annotée) au lieu d'en créer une.
            if (_reopened is { } reopened && a.Source.StartsWith("Historique · ", StringComparison.Ordinal))
            {
                _reopened = null;
                _lastImageSequence = a.Sequence;
                _lastImageEntry = reopened;
            }

            if (a.Sequence == _lastImageSequence && _lastImageEntry is { } existing && _history.Entries.Contains(existing))
            {
                existing.Codes = codes;
                existing.Worst = worst;
                existing.LabelVerdict = a.Label?.Verdict ?? string.Empty;
                existing.LabelNotes = [.. a.Label?.Problems ?? []];
                _history.Update(existing, existing.ImageFile is null ? a.Image : null, a.Annotated);
            }
            else
            {
                _lastImageSequence = a.Sequence;
                _lastImageEntry = _history.Add(new HistoryEntry
                {
                    Kind = HistoryKind.ImageAnalysis,
                    CaseId = HistoryPage.ActiveCaseId,
                    Source = a.Source,
                    Codes = codes,
                    Worst = worst,
                    LabelVerdict = a.Label?.Verdict ?? string.Empty,
                    LabelNotes = [.. a.Label?.Problems ?? []],
                }, a.Image, ".jpg", a.Annotated);
            }

            HistoryPage.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Warning, "HISTORIQUE", "Analyse non enregistrée : " + ex.Message);
        }
    }

    /// <summary>Enregistre une comparaison ; la même comparaison relancée dans les 2 minutes met à jour l'entrée.</summary>
    private void RecordComparison(BenchSnapshot s)
    {
        // Lecture d'un autre code que la référence : ce n'est pas un résultat de test, on ne l'archive pas.
        if (s.Result.OtherCode)
        {
            return;
        }

        try
        {
            var findings = s.Result.Findings.Select(f => new HistoryFinding(f.Severity, f.Title, f.Explanation, f.Fix)).ToList();
            if (_lastComparison is { } last && _history.Entries.Contains(last) && last.Expected == s.Expected && last.Received == s.Received
                && DateTimeOffset.Now - last.At < TimeSpan.FromMinutes(2))
            {
                last.Findings = findings;
                last.Verdict = s.Verdict;
                last.Worst = s.Result.WorstSeverity;
                _history.Update(last);
            }
            else
            {
                _lastComparison = _history.Add(new HistoryEntry
                {
                    Kind = HistoryKind.Comparison,
                    CaseId = HistoryPage.ActiveCaseId,
                    Source = s.Via,
                    Expected = s.Expected,
                    Received = s.Received,
                    AnnouncedSymbology = s.Symbology,
                    Verdict = s.Verdict,
                    Worst = s.Result.WorstSeverity,
                    Findings = findings,
                }, s.Image, ".png");
            }

            HistoryPage.Refresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Warning, "HISTORIQUE", "Comparaison non enregistrée : " + ex.Message);
        }
    }

    private async void OnReopenRequested(HistoryEntry entry)
    {
        var path = _history.ImagePath(entry);
        if (entry.Kind == HistoryKind.ImageAnalysis)
        {
            if (path is null)
            {
                AddLog(LogKind.Warning, "HISTORIQUE", "L'image de cette analyse n'est plus disponible.");
                return;
            }

            NavAnalyse.IsChecked = true;
            var mime = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            _reopened = entry;
            await _atelier.LoadImageAsync(await File.ReadAllBytesAsync(path), $"Historique · {entry.Source}", mime, record: true);
            return;
        }

        var png = path is null ? null : await File.ReadAllBytesAsync(path);
        Bench.SetExpected(new CompareRequest(ControlCharacters.FromVisualized(entry.Expected), "historique", string.Empty, png));
        NavBench.IsChecked = true;
    }

    /// <summary>
    /// Rapport HTML autonome converti en PDF par le moteur Edge, puis ouvert : un dossier, ou tous les dossiers du client.
    /// Seules les analyses cochées « dans le rapport » sont reprises.
    /// </summary>
    private async Task GenerateReportAsync(ServiceCase serviceCase, bool wholeClient)
    {
        IReadOnlyList<ServiceCase> cases = wholeClient ? _history.CasesOfClient(serviceCase.Client) : [serviceCase];
        var entries = _history.Entries.Where(e => e.InReport && cases.Any(c => c.Id == e.CaseId)).ToList();
        var header = wholeClient
            ? new ServiceCase
            {
                Id = "client",
                Client = serviceCase.Client,
                Reference = $"{cases.Count} dossiers",
                Technician = string.Join(", ", cases.Select(c => c.Technician).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct()),
                Notes = string.Join("\n", cases.Where(c => !string.IsNullOrWhiteSpace(c.Reference)).Select(c => $"• {c.Reference} : {c.Notes.Split('\n')[0]}".TrimEnd(' ', ':'))),
            }
            : serviceCase;
        if (entries.Count == 0)
        {
            AddLog(LogKind.Warning, "RAPPORT", "Aucune analyse à mettre dans le rapport (dossier vide, ou toutes les analyses sont décochées).");
            return;
        }

        var name = string.Join(" - ", new[] { "Rapport Scanalyse", header.Client, wholeClient ? "tous les dossiers" : header.Reference, DateTime.Now.ToString("yyyy-MM-dd") }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        foreach (var bad in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(bad, '_');
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Enregistrer le rapport",
            FileName = name + ".pdf",
            DefaultExt = ".pdf",
            Filter = "Rapport PDF|*.pdf|Page web (HTML)|*.html",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var html = ReportBuilder.Build(header, entries, ImageDataUrl, DateTimeOffset.Now, v is null ? string.Empty : $"{v.Major}.{v.Minor}.{v.Build}", cases, HistoryPage.ReportContent);
        try
        {
            if (dialog.FileName.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
            {
                await File.WriteAllTextAsync(dialog.FileName, html);
            }
            else
            {
                if (_atelier.Environment is null)
                {
                    AddLog(LogKind.Error, "RAPPORT", "Le moteur Edge WebView2 n'est pas disponible : enregistre le rapport en HTML et imprime-le depuis le navigateur.");
                    return;
                }

                AddLog(LogKind.Info, "RAPPORT", "Création du PDF…");
                await ReportPrinter.SavePdfAsync(_atelier.Environment, new System.Windows.Interop.WindowInteropHelper(this).Handle, html, dialog.FileName);
            }

            AddLog(LogKind.Ok, "RAPPORT", $"Rapport enregistré : {dialog.FileName}");
            Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException or Win32Exception)
        {
            AddLog(LogKind.Error, "RAPPORT", "Rapport non créé : " + ex.Message);
        }
    }

    private string? ImageDataUrl(HistoryEntry entry)
    {
        var path = _history.DisplayImagePath(entry);
        if (path is null)
        {
            return null;
        }

        try
        {
            var mime = path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
            return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ Appareils de lecture

    /// <summary>Lecture reçue en direct : journal, puis banc d'essai (qui compare si la référence est connue).</summary>
    private void OnDeviceScanned(ScanEvent scan)
    {
        AddLog(LogKind.Info, scan.Symbology?.ToUpperInvariant() ?? "LECTURE", $"{ControlCharacters.Visualize(scan.Text)}   [{scan.Hex}]");
        Bench.ReceiveScan(scan);
        if (Bench.Visibility != Visibility.Visible && Devices.Visibility != Visibility.Visible)
        {
            NavBench.IsChecked = true;
        }
    }

    private async Task InitializeDevicesAsync()
    {
        try
        {
            await Devices.InitializeAsync();
        }
        catch (Exception ex)
        {
            AddLog(LogKind.Warning, "DOUCHETTE", "Recherche des douchettes impossible : " + ex.Message);
        }
    }

    private void OnScannerStatus(ScannerStatus status)
    {
        SourceText.Text = status.Text;
        SourceDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            status.Error ? "BadFg" : status.Kind == ScanSourceKind.Keyboard ? "Muted" : "OkFg");
    }

    private void Source_Click(object sender, RoutedEventArgs e) => NavDevices.IsChecked = true;

    private async void Camera_Click(object sender, RoutedEventArgs e) => await StartCameraAsync(null);

    private async Task StartCameraAsync(string? name)
    {
        NavAnalyse.IsChecked = true;
        await _atelier.StartCameraAsync(name);
        AddLog(LogKind.Info, "CAMÉRA", "Lecture en direct : présente le code devant la caméra, il est analysé dès qu'il est lu.");
    }

    // ------------------------------------------------------------------ Navigation

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        if (sender != NavAnalyse)
        {
            await _atelier.StopCameraAsync();
        }

        AtelierPage.Visibility = Visibility.Collapsed;
        Bench.Visibility = Visibility.Collapsed;
        Symbols.Visibility = Visibility.Collapsed;
        Devices.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        HistoryPage.Visibility = Visibility.Collapsed;
        PrintPage.Visibility = Visibility.Collapsed;

        if (sender == NavAnalyse || sender == NavCreate)
        {
            var create = sender == NavCreate;
            _atelierMode = create ? "create" : "scan";
            AtelierTitle.Text = create ? "Créer un code" : "Analyser une image";
            AtelierSubtitle.Text = create
                ? "Tous les types de codes : QR personnalisés, Data Matrix, GS1, EAN, Code 128, PDF417… avec vérification par relecture et export."
                : "Importe, colle ou capture une image : chaque code est lu, détaillé et peut être comparé à une lecture douchette.";
            AtelierPage.Visibility = Visibility.Visible;
            await _atelier.SetModeAsync(_atelierMode);
        }
        else if (sender == NavBench)
        {
            Bench.Visibility = Visibility.Visible;
            Bench.FocusReception();
        }
        else if (sender == NavPrint)
        {
            PrintPage.Visibility = Visibility.Visible;
        }
        else if (sender == NavHistory)
        {
            HistoryPage.Visibility = Visibility.Visible;
            HistoryPage.Refresh();
        }
        else if (sender == NavDevices)
        {
            Devices.Visibility = Visibility.Visible;
        }
        else if (sender == NavSymbols)
        {
            Symbols.Visibility = Visibility.Visible;
        }
        else if (sender == NavSettings)
        {
            SettingsPage.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Le menu a été replié ou déplié à la main : on ne le change plus selon la largeur.</summary>
    private bool _menuManual;

    /// <summary>
    /// Fenêtre étroite : la barre d'outils garde ses icônes (avec bulles d'aide) sans les libellés, et le menu se replie,
    /// pour que rien ne se chevauche ni ne disparaisse.
    /// </summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var w = e.NewSize.Width;
        var compact = w < 1320;
        foreach (var label in new FrameworkElement[] { ImportLabel, PasteLabel, CaptureLabel, CameraLabel, ThemeLabel })
        {
            label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        }

        CaseLabel.MaxWidth = w < 1180 ? 150 : 260;
        SourceText.MaxWidth = w < 1180 ? 140 : double.PositiveInfinity;
        SourceText.TextTrimming = TextTrimming.CharacterEllipsis;
        if (!_menuManual && e.WidthChanged)
        {
            var wantNarrow = w < 1180;
            if (wantNarrow != Sidebar.Width <= SidebarNarrow)
            {
                SetMenuNarrow(wantNarrow);
            }
        }
    }

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        _menuManual = true;
        SetMenuNarrow(Sidebar.Width > SidebarNarrow);
    }

    private void SetMenuNarrow(bool narrow)
    {
        Sidebar.Width = narrow ? SidebarNarrow : SidebarWide;
        MenuLabel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        MenuChevron.RenderTransformOrigin = new Point(0.5, 0.5);
        MenuChevron.RenderTransform = narrow ? new RotateTransform(180) : Transform.Identity;
    }

    // ------------------------------------------------------------------ Images : presse-papiers et capture

    private async void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetClipboardPng(out var png))
        {
            await SendImageAsync(png, "Image collée");
        }
        else
        {
            AddLog(LogKind.Warning, "PRESSE-PAPIERS", "Aucune image à coller. Fais une capture (Win + Maj + S) ou copie une image, puis réessaie.");
        }
    }

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".bmp"] = "image/bmp",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff",
    };

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importer une image à analyser",
            Filter = "Images (PNG, JPG, BMP, GIF, WebP, TIFF)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff|Tous les fichiers|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var path = dialog.FileName;
        try
        {
            byte[] data;
            string mime;
            if (ImageTypes.TryGetValue(Path.GetExtension(path), out var known) && known != "image/tiff")
            {
                data = await File.ReadAllBytesAsync(path);
                mime = known;
            }
            else
            {
                // TIFF et formats inconnus du moteur web : Windows les décode, on transmet du PNG.
                data = ToPng(path);
                mime = "image/png";
            }

            NavAnalyse.IsChecked = true;
            await _atelier.LoadImageAsync(data, Path.GetFileName(path), mime);
            AddLog(LogKind.Info, "IMAGE", $"Analyse de {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException)
        {
            AddLog(LogKind.Error, "IMAGE", $"Impossible d'ouvrir {Path.GetFileName(path)} : {ex.Message}");
        }
    }

    private static byte[] ToPng(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        _clipboardSequence = NativeMethods.GetClipboardSequenceNumber();
        _captureDeadline = DateTime.Now.AddSeconds(90);
        try
        {
            Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            AddLog(LogKind.Warning, "CAPTURE", "L'outil Capture de Windows est indisponible : fais Win + Maj + S, puis « Coller une image ».");
            return;
        }

        AddLog(LogKind.Info, "CAPTURE", "Sélectionne la zone qui contient le code : elle sera analysée automatiquement.");
        _captureTimer.Start();
    }

    private async void CaptureTimer_Tick(object? sender, EventArgs e)
    {
        if (DateTime.Now > _captureDeadline)
        {
            _captureTimer.Stop();
            AddLog(LogKind.Warning, "CAPTURE", "Aucune capture reçue.");
            return;
        }

        var sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == _clipboardSequence)
        {
            return;
        }

        _clipboardSequence = sequence;
        if (TryGetClipboardPng(out var png))
        {
            _captureTimer.Stop();
            Activate();
            await SendImageAsync(png, "Capture d'écran");
        }
    }

    private async Task SendImageAsync(byte[] png, string label)
    {
        NavAnalyse.IsChecked = true;
        await _atelier.LoadImageAsync(png, label);
    }

    /// <summary>
    /// Récupère l'image du presse-papiers en PNG. Le format PNG est préféré quand il existe (outil Capture) ;
    /// sinon l'image est convertie sans couche alpha, car certaines captures arrivent avec une transparence totale.
    /// </summary>
    private static bool TryGetClipboardPng(out byte[] png)
    {
        png = [];
        try
        {
            if (Clipboard.ContainsData("PNG") && Clipboard.GetData("PNG") is MemoryStream stream)
            {
                png = stream.ToArray();
                return png.Length > 0;
            }

            if (!Clipboard.ContainsImage() || Clipboard.GetImage() is not BitmapSource image)
            {
                return false;
            }

            var opaque = new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(opaque));
            using var output = new MemoryStream();
            encoder.Save(output);
            png = output.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is COMException or ExternalException or InvalidOperationException)
        {
            // Presse-papiers occupé par une autre application : on réessaiera.
            return false;
        }
    }

    // ------------------------------------------------------------------ Thème

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        var next = ThemeManager.Mode switch
        {
            AppTheme.System => AppTheme.Light,
            AppTheme.Light => AppTheme.Dark,
            _ => AppTheme.System,
        };
        ThemeManager.Apply(next, save: true);
    }

    private async void OnThemeChanged()
    {
        ThemeLabel.Text = "Thème : " + ThemeManager.Label(ThemeManager.Mode);
        if (TryFindResource("BgColor") is Color bg)
        {
            Web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(bg.A, bg.R, bg.G, bg.B);
        }

        await _atelier.SetThemeAsync(ThemeManager.IsDark);
    }

    // ------------------------------------------------------------------ Journal

    private void AddLog(LogKind kind, string tag, string text)
    {
        _log.Add(new LogEntry(DateTime.Now, kind, tag, text));
        while (_log.Count > MaxLogEntries)
        {
            _log.RemoveAt(0);
        }

        LogCount.Text = $"{_log.Count} ligne{(_log.Count > 1 ? "s" : string.Empty)}";
        LogScroll.ScrollToEnd();
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, _log.Select(l => l.ToString())));
        }
        catch (COMException)
        {
            AddLog(LogKind.Warning, "JOURNAL", "Presse-papiers occupé : réessaie.");
        }
    }

    private async void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        if (_log.Count == 0 || !await Confirm.AskAsync(
                "Effacer le journal ?",
                $"Les {_log.Count} lignes du journal des lectures seront effacées. Les analyses et comparaisons enregistrées dans l'historique ne sont pas touchées.",
                "Effacer le journal",
                "Le journal n'est pas sauvegardé : copie-le avant si tu en as besoin."))
        {
            return;
        }

        _log.Clear();
        LogCount.Text = string.Empty;
    }

    private void ToggleLog_Click(object sender, RoutedEventArgs e)
    {
        var hide = LogScroll.Visibility == Visibility.Visible;
        LogScroll.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;
        LogChevron.RenderTransform = hide ? new RotateTransform(180) : Transform.Identity;
    }

    // ------------------------------------------------------------------ Fenêtre

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Une fenêtre sans cadre agrandie déborde de l'écran : on compense par une marge.</summary>
    private void Window_StateChanged(object? sender, EventArgs e)
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2.5 0.5h7v7M0.5 2.5h7v7h-7z" : "M0.5 0.5h9v9h-9z");
    }
}

/// <summary>Commande simple pour les raccourcis clavier.</summary>
internal sealed class RelayCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute();
}

internal static class NativeMethods
{
    /// <summary>Numéro qui change à chaque modification du presse-papiers.</summary>
    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();
}
