using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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

    private bool _startupFinished;

    /// <summary>
    /// Levé une seule fois, quand la fenêtre est affichée et l'Atelier prêt (ou en échec) : l'écran de démarrage peut
    /// alors se fermer. Un délai de sécurité le lève quoi qu'il arrive.
    /// </summary>
    public event Action? StartupFinished;

    private void FinishStartup()
    {
        if (_startupFinished)
        {
            return;
        }

        _startupFinished = true;
        StartupFinished?.Invoke();
    }

    public MainWindow()
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = v is null ? string.Empty : $"version {v.Major}.{v.Minor}.{v.Build}";
        LogList.ItemsSource = _log;

        _atelier = new AtelierHost(Web);
        _atelier.Ready += OnAtelierReady;
        _atelier.CompareRequested += OnCompareRequested;
        _atelier.Analyzed += OnAnalyzed;
        // Le moteur web s'affiche toujours au-dessus des éléments WPF : on le masque le temps d'une confirmation.
        // Le moteur web s'affiche toujours au-dessus : masqué tant qu'une fenêtre intérieure (confirmation, accueil, visite) est ouverte.
        void Airspace()
        {
            var covered = Confirm.IsVisible || Welcome.IsVisible || Tour.IsVisible;
            if (covered && Web.Visibility == Visibility.Visible)
            {
                Web.Visibility = Visibility.Hidden;
            }
            else if (!covered && Web.Visibility == Visibility.Hidden)
            {
                Web.Visibility = Visibility.Visible;
            }
        }

        Confirm.IsVisibleChanged += (_, _) => Airspace();
        Welcome.IsVisibleChanged += (_, _) => Airspace();
        Tour.IsVisibleChanged += (_, _) => Airspace();
        Tour.Ended += () =>
        {
            if (Tour.Completed)
            {
                // Visite terminée : retour au point de départ habituel.
                NavAnalyse.IsChecked = true;
            }
        };
        Welcome.HistoryFolder = () => _history.Folder;
        Welcome.HistoryFolderChangeRequested += ChangeHistoryFolderAsync;
        Welcome.ImportProfile = SettingsPage.ImportTeamProfileAsync;
        Welcome.Finished += tour =>
        {
            if (tour)
            {
                StartTour();
            }
        };
        SettingsPage.WelcomeRequested += () => Welcome.Show();
        SettingsPage.TourRequested += StartTour;
        SettingsPage.UninstallRequested += async folders => await UninstallAsync(folders);
        SettingsPage.UpdateCheckRequested += async () => await CheckUpdatesAsync(silent: false);
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
        PrintPage.Renderer = (text, type, hri) => _atelier.RenderAsync(text, type, hri);
        PrintPage.Log += (kind, text) => AddLog(kind, "IMPRESSION", text);
        ConfigPage.Log += (kind, text) => AddLog(kind, "CONFIGURATION", text);
        PrintPage.LabelRenderer = json => _atelier.RenderLabelAsync(json);
        PrintPage.AnalyzeRequested += async (png, source) =>
        {
            NavAnalyse.IsChecked = true;
            await _atelier.LoadImageAsync(png, source);
            AddLog(LogKind.Info, "IMPRESSION", "Étiquette du programme ZPL envoyée à l'analyse : chaque code est relu comme sur une photo.");
        };
        _atelier.PrintRequested += r => OpenPrint(r.Text, PrintView.SymbologyFor(r.Format, r.Text, r.Gs1), r.Source);
        _atelier.ModeChanged += OnAtelierModeChanged;
        _atelier.BatchRequested += async files => await StartBatchAsync(files);
        _atelier.ActionRequested += action =>
        {
            switch (action)
            {
                case "paste":
                    Paste_Click(this, new RoutedEventArgs());
                    break;
                case "capture":
                    Capture_Click(this, new RoutedEventArgs());
                    break;
                case "import":
                    Import_Click(this, new RoutedEventArgs());
                    break;
                case "camera":
                    Camera_Click(this, new RoutedEventArgs());
                    break;
            }
        };
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
        var startup = AppSettings.Load();
        (startup.StartPage switch
        {
            "create" => NavCreate,
            "print" => NavPrint,
            "config" => NavConfig,
            "bench" => NavBench,
            "devices" => NavDevices,
            "history" => NavHistory,
            _ => NavAnalyse,
        }).IsChecked = true;
        if (!startup.LogOpen)
        {
            ToggleLog_Click(this, new RoutedEventArgs());
        }

        ApplyDisplaySettings();
        SettingsPage.DisplaySettingsChanged += ApplyDisplaySettings;
        OnThemeChanged();
        AddLog(LogKind.Info, "SCANALYSE", "Prêt. L'analyse est faite sur ce PC, rien n'est envoyé sur Internet.");

        Loaded += async (_, _) =>
        {
            var safety = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
            safety.Tick += (_, _) =>
            {
                safety.Stop();
                FinishStartup();
            };
            safety.Start();
            if (!AppSettings.Load().WelcomeDone)
            {
                Welcome.Show();
            }

            // L'Atelier d'abord ; la recherche des douchettes tourne à côté sans jamais le bloquer.
            _ = InitializeDevicesAsync();
            await LoadHistoryAsync();
            await StartAtelierAsync();
            if (AppSettings.Load().UpdateCheck)
            {
                _ = CheckUpdatesAsync(silent: true);
            }
        };
        Closing += async (_, e) =>
        {
            if (_cleanedUp)
            {
                return;
            }

            // Fermeture propre avant de quitter : douchettes, relais et moteur web (processus msedgewebview2)
            // sont libérés, sans jamais attendre plus de 2 secondes un appareil qui ne répond pas.
            e.Cancel = true;
            _cleanedUp = true;
            Hide();
            ThemeManager.Changed -= OnThemeChanged;
            try
            {
                await Devices.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or ObjectDisposedException or COMException)
            {
            }

            try
            {
                Web.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or COMException)
            {
            }

            Close();
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
            FinishStartup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            ShowWebError("Impossible de démarrer l'Atelier : " + ex.Message);
            FinishStartup();
        }
    }

    private async void OnAtelierReady()
    {
        await _atelier.SetThemeAsync(ThemeManager.IsDark);
        await _atelier.SetLanguageAsync();
        await _atelier.SetModeAsync(_atelierMode);
        FinishStartup();
        if (_pendingImages.Count > 0)
        {
            var images = _pendingImages.ToList();
            _pendingImages.Clear();
            await OpenFilesAsync(images);
        }
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

    /// <summary>Ouvre l'onglet « Étiquette imprimante » avec un code venu d'un autre onglet.</summary>
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

        if (_batch is not null && a.Source == _batchCurrent)
        {
            _batchCodes += codes.Count;
            Dispatcher.InvokeAsync(NextBatchItemAsync, DispatcherPriority.Background);
        }
    }

    // ------------------------------------------------------------------ Analyse en lot

    private Queue<(string Label, byte[] Data, string Mime)>? _batch;
    private string? _batchCurrent;
    private int _batchTotal;
    private int _batchCodes;

    /// <summary>
    /// Analyse à la suite plusieurs images ou les pages d'un PDF : chacune est enregistrée dans le dossier SAV en cours,
    /// puis le rapport du dossier est proposé.
    /// </summary>
    private async Task StartBatchAsync(IReadOnlyList<(string Name, byte[] Data)> files)
    {
        var items = new Queue<(string Label, byte[] Data, string Mime)>();
        foreach (var (name, data) in files)
        {
            var ext = Path.GetExtension(name);
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || (data.Length > 4 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F'))
            {
                try
                {
                    var pages = await PdfPages.RenderAsync(data);
                    for (var i = 0; i < pages.Count; i++)
                    {
                        items.Enqueue(($"{name} · page {i + 1}/{pages.Count}", pages[i], "image/png"));
                    }

                    AddLog(LogKind.Info, "LOT", $"{name} : {pages.Count} page{(pages.Count > 1 ? "s" : string.Empty)}.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException or InvalidOperationException)
                {
                    AddLog(LogKind.Error, "LOT", $"{name} : PDF illisible ({ex.Message}).");
                }
            }
            else if (ImageTypes.TryGetValue(ext, out var mime) && mime != "image/tiff")
            {
                items.Enqueue((name, data, mime));
            }
            else
            {
                try
                {
                    // TIFF et autres : Windows décode, on transmet du PNG.
                    var frame = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(frame));
                    using var ms = new MemoryStream();
                    encoder.Save(ms);
                    items.Enqueue((name, ms.ToArray(), "image/png"));
                }
                catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException)
                {
                    AddLog(LogKind.Warning, "LOT", $"{name} : format non reconnu, ignoré.");
                }
            }
        }

        if (items.Count == 0)
        {
            return;
        }

        if (items.Count == 1)
        {
            var single = items.Dequeue();
            NavAnalyse.IsChecked = true;
            await _atelier.LoadImageAsync(single.Data, single.Label, single.Mime);
            return;
        }

        _batch = items;
        _batchTotal = items.Count;
        _batchCodes = 0;
        NavAnalyse.IsChecked = true;
        AddLog(LogKind.Info, "LOT", $"Analyse en lot : {items.Count} images, enregistrées dans le dossier « {CaseLabel.Text.Replace("Dossier : ", string.Empty, StringComparison.Ordinal)} ».");
        await NextBatchItemAsync();
    }

    private async Task NextBatchItemAsync()
    {
        if (_batch is null)
        {
            return;
        }

        if (_batch.Count == 0)
        {
            var total = _batchTotal;
            var codes = _batchCodes;
            _batch = null;
            _batchCurrent = null;
            AddLog(LogKind.Ok, "LOT", $"Analyse en lot terminée : {total} images, {codes} code{(codes > 1 ? "s" : string.Empty)} lu{(codes > 1 ? "s" : string.Empty)}.");
            var settings = AppSettings.Load();
            if (_history.FindCase(settings.ActiveCaseId) is { } current && await Confirm.AskAsync(
                    Localization.T("Analyse en lot terminée"),
                    string.Format(CultureInfo.InvariantCulture, Localization.T("{0} images analysées, {1} codes lus. Générer maintenant le rapport PDF du dossier « {2} » ?"), total, codes, current.DisplayName),
                    Localization.T("Générer le rapport"), null, danger: false))
            {
                await GenerateReportAsync(current, false);
            }

            return;
        }

        var (label, data, mime) = _batch.Dequeue();
        _batchCurrent = label;
        AddLog(LogKind.Info, "LOT", $"{_batchTotal - _batch.Count}/{_batchTotal} · {label}");
        await _atelier.LoadImageAsync(data, label, mime);

        // Filet de sécurité : une image qui ne renvoie aucun résultat ne bloque pas le lot.
        await Task.Delay(TimeSpan.FromSeconds(45));
        if (_batch is not null && _batchCurrent == label)
        {
            AddLog(LogKind.Warning, "LOT", $"{label} : pas de résultat après 45 s, image suivante.");
            await NextBatchItemAsync();
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
            PurgeOldEntries();
            HistoryPage.Attach(_history, AppSettings.Load().ActiveCaseId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AddLog(LogKind.Warning, "HISTORIQUE", "Historique illisible : " + ex.Message);
        }
    }

    /// <summary>Paramètres → Historique : suppression des analyses plus anciennes que N jours.</summary>
    private void PurgeOldEntries()
    {
        var days = AppSettings.Load().RetentionDays;
        if (days <= 0)
        {
            return;
        }

        var limit = DateTimeOffset.Now.AddDays(-days);
        var old = _history.Entries.Where(e => e.At < limit).ToList();
        foreach (var e in old)
        {
            _history.Delete(e);
        }

        if (old.Count > 0)
        {
            AddLog(LogKind.Info, "HISTORIQUE", $"{old.Count} analyse{(old.Count > 1 ? "s" : string.Empty)} de plus de {days} jours supprimée{(old.Count > 1 ? "s" : string.Empty)} (Paramètres → Historique).");
        }
    }

    /// <summary>Applique les réglages d'affichage : taille de l'interface, journal.</summary>
    public void ApplyDisplaySettings()
    {
        var s = AppSettings.Load();
        var scale = Math.Clamp(s.UiScale, 1, 1.5);
        // Tout ce qui est dessiné par WPF est agrandi ; l'Atelier (moteur web) suit par son propre zoom.
        var t = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        foreach (var el in new FrameworkElement[] { ToolbarHost, Sidebar, AtelierHeader, Bench, Devices, HistoryPage, Symbols, SettingsPage, PrintPage, ConfigPage, LogHost, Confirm, Welcome })
        {
            el.LayoutTransform = t;
        }

        try
        {
            Web.ZoomFactor = scale;
        }
        catch (InvalidOperationException)
        {
            // Moteur web pas encore prêt : le zoom sera appliqué par l'Atelier au chargement.
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
        if (!AppSettings.Load().AutoRecord)
        {
            return;
        }

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
        if (!AppSettings.Load().AutoRecord)
        {
            return;
        }

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
    /// <summary>Société, technicien, coordonnées et logo des Paramètres, pour l'en-tête des rapports.</summary>
    private static ReportIssuer ReportIssuerFromSettings()
    {
        var s = AppSettings.Load();
        var logo = string.Empty;
        try
        {
            if (File.Exists(s.ReportLogoPath) && new FileInfo(s.ReportLogoPath).Length < 2_000_000)
            {
                var ext = Path.GetExtension(s.ReportLogoPath).ToLowerInvariant();
                var mime = ext switch { ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".svg" => "image/svg+xml", ".gif" => "image/gif", ".bmp" => "image/bmp", _ => "image/png" };
                logo = $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(s.ReportLogoPath))}";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return new ReportIssuer(s.ReportCompany.Trim(), s.ReportTechnician.Trim(), s.ReportContact.Trim(), logo);
    }

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
            Title = Localization.T("Enregistrer le rapport"),
            FileName = name + ".pdf",
            DefaultExt = ".pdf",
            Filter = Localization.T("Rapport PDF|*.pdf|Page web (HTML)|*.html"),
            InitialDirectory = AppSettings.Load().ReportsFolderForSave(),
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        var html = ReportBuilder.Build(header, entries, ImageDataUrl, DateTimeOffset.Now, v is null ? string.Empty : $"{v.Major}.{v.Minor}.{v.Build}", cases, HistoryPage.ReportContent,
            ReportIssuerFromSettings());
        html = Localization.TranslateHtml(html);
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

        // La lecture s'affiche au banc d'essai, y compris depuis l'onglet de connexion des terminaux.
        if (Bench.Visibility != Visibility.Visible)
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
        ConfigPage.Visibility = Visibility.Collapsed;

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
        else if (sender == NavConfig)
        {
            ConfigPage.Visibility = Visibility.Visible;
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

    /// <summary>
    /// Visite guidée : chaque page est ouverte et présentée tour à tour, avec la barre du haut, le dossier SAV, le journal
    /// et les paramètres. Les pages de l'Atelier (moteur web, toujours au premier plan) sont présentées par leur menu.
    /// </summary>
    private void StartTour()
    {
        NavAnalyse.IsChecked = true;
        Action Page(RadioButton nav) => () => nav.IsChecked = true;
        var printerCard = ConfigPage.FindName("PrinterCard") as FrameworkElement;
        Tour.Start(
        [
            new(null, "Bienvenue dans Scanalyse",
                "En quelques étapes, voici tout ce que le programme sait faire. Avance avec « Suivant » ou les flèches du clavier, quitte avec Échap. La visite se relance à tout moment depuis les Paramètres."),
            new(Sidebar, "Le menu",
                "Chaque outil a sa page. Le bouton « MENU » réduit cette barre à ses icônes pour gagner de la place sur un petit écran.",
                Page(NavAnalyse)),
            new(ToolbarLeft, "La barre du haut",
                "« Source » indique comment la douchette est reliée au PC. Importer, Coller (Ctrl + Maj + V), Capturer une zone de l'écran ou Caméra : l'image part dans « Analyser une image »."),
            new(NavAnalyse, "Analyser une image",
                "Photo, capture d'écran ou PDF du client, même de plusieurs pages : chaque code est lu et expliqué (lien, Wi-Fi, données GS1 avec dates, lots, numéros de série…). Les défauts sont signalés : clé de contrôle fausse, GS1 mal construit, contraste faible, zone blanche trop petite."),
            new(NavCreate, "Créer un code",
                "Plus de 25 types, choisis par catégorie : QR personnalisé (couleurs, logo), Data Matrix, GS1, EAN, Code 128, PDF417, MaxiCode… L'aperçu suit la saisie, puis export en image."),
            new(PrintPage, "Étiquette imprimante",
                "Lis le programme d'un client en ZPL, TSPL, EPL2, DPL, SBPL ou Fingerprint : aperçu de l'étiquette, contenu de chaque code, erreurs. Pars d'un modèle prêt à l'emploi (palette SSCC, colis, produit…), envoie à une imprimante réseau ou installée, et entretiens-la d'ici : état, calibrage, page de configuration.",
                Page(NavPrint)),
            new(ConfigPage, "Conf imprimante",
                "Les fichiers de configuration, toutes marques (Zebra, TSC, Honeywell, SATO, Toshiba…). Trois façons de travailler : Analyser un fichier (chaque ligne expliquée, erreurs en couleur, propositions pendant la frappe), le Convertir vers le langage d'une autre marque, ou en Créer un nouveau à partir d'un formulaire simple.",
                Page(NavConfig)),
            new(printerCard, "L'imprimante de référence",
                "Indique l'imprimante visée : Scanalyse reconnaît sa marque et son modèle, puis vérifie que chaque réglage du fichier est bien accepté par elle avant l'envoi."),
            new(Bench, "Banc d'essai douchette",
                "Scanne un code avec la douchette du client : Scanalyse compare ce qu'elle transmet au vrai contenu et explique chaque écart (clavier AZERTY ou QWERTY, préfixe en trop, séparateur GS1 perdu…). Des codes de configuration prêts à scanner corrigent les réglages courants.",
                Page(NavBench)),
            new(Devices, "Appareils de lecture",
                "Douchettes USB, série (port COM) ou HID POS, terminaux Android en USB ou en Wi-Fi. Un téléphone ou un PDA se connecte en scannant un QR code : sa page de relais sert de douchette, avec sa caméra s'il n'a pas de lecteur. Chaque lecture arrive au banc d'essai, octet par octet.",
                Page(NavDevices)),
            new(HistoryPage, "Historique et rapports",
                "Toutes les analyses, rangées par dossier client. Rouvre-les, renomme-les, commente-les et produis le rapport PDF à ton en-tête. Le dossier peut être partagé sur le réseau avec toute l'équipe.",
                Page(NavHistory)),
            new(CaseButton, "Dossier SAV en cours",
                "Chaque analyse est rangée dans le dossier choisi ici : choisis-le en début d'intervention pour que tout ce que tu fais pour ce client se retrouve au même endroit."),
            new(Symbols, "Symbologies",
                "L'aide-mémoire des codes-barres : à quoi sert chaque type, ce qu'il peut contenir et ses pièges. Pratique pour conseiller un client sur le bon code.",
                Page(NavSymbols)),
            new(LogHost, "Le journal",
                "Tout ce qui se passe s'affiche ici : lectures, envois à l'imprimante, erreurs. La flèche le réduit ou le déplie."),
            new(SettingsPage, "Paramètres",
                "Langue, thème, taille d'affichage, dossiers, en-tête et logo des rapports, profil d'équipe, « Ouvrir avec », mises à jour automatiques. « Sur téléphone » donne le QR code de la version pour téléphone. L'accueil et cette visite se relancent d'ici.",
                Page(NavSettings)),
        ]);
    }

    // ------------------------------------------------------------------ Mises à jour

    private Scanalyse.Core.Updates.AvailableUpdate? _update;

    /// <summary>Cherche une nouvelle version ; en silence au démarrage (seul le bouton de la barre de titre apparaît).</summary>
    private async Task CheckUpdatesAsync(bool silent)
    {
        var (update, message) = await Updater.CheckAsync(AppSettings.Load());
        _update = update;
        UpdateButton.Content = update is null ? string.Empty : string.Format(CultureInfo.InvariantCulture, Localization.T("Mise à jour {0}"), update.Version);
        UpdateButton.Visibility = update is null ? Visibility.Collapsed : Visibility.Visible;
        SettingsPage.ShowUpdateStatus(message, update is not null);
        if (update is not null)
        {
            AddLog(LogKind.Info, "MISE À JOUR", message);
            if (!silent)
            {
                await OfferUpdateAsync();
            }
        }
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await OfferUpdateAsync();

    /// <summary>
    /// Nouveautés de la version proposée, en texte simple : seulement sa section du journal des versions (pas l'empreinte
    /// ni les versions précédentes), une puce par nouveauté, sans la mise en forme Markdown.
    /// </summary>
    private static string UpdateNotes(string markdown)
    {
        var lines = markdown.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        // Sans section « ## » (notes écrites à la main), tout le texte est repris.
        var start = Array.FindIndex(lines, l => l.StartsWith("## ", StringComparison.Ordinal));

        var items = new List<string>();
        for (var i = start + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                break;
            }

            var text = line.Trim();
            if (text.Length == 0 || text.StartsWith('#') || text.StartsWith("**SHA-256", StringComparison.Ordinal))
            {
                continue;
            }

            var sub = line.StartsWith("  ", StringComparison.Ordinal);
            text = text.TrimStart('-', '*', ' ').Replace("**", string.Empty, StringComparison.Ordinal).Replace("`", string.Empty, StringComparison.Ordinal);
            items.Add((sub ? "    – " : "• ") + text);
        }

        return string.Join(Environment.NewLine, items);
    }

    /// <summary>Propose d'installer la version trouvée (nouveautés en résumé), puis remplace le programme et redémarre.</summary>
    private async Task OfferUpdateAsync()
    {
        if (_update is not { } u)
        {
            return;
        }

        var notes = UpdateNotes(u.Notes);
        var ok = await Confirm.AskAsync(
            string.Format(CultureInfo.InvariantCulture, Localization.T("Installer Scanalyse {0} ?"), u.Version),
            (notes.Length > 0 ? notes + Environment.NewLine + Environment.NewLine : string.Empty)
                + string.Format(CultureInfo.InvariantCulture, Localization.T("Version actuelle : {0}. Scanalyse va se fermer puis redémarrer dans la nouvelle version ; l'historique et les réglages sont conservés."), Updater.Current),
            Localization.T("Installer et redémarrer"),
            null,
            danger: false);
        if (!ok)
        {
            return;
        }

        AddLog(LogKind.Info, "MISE À JOUR", Localization.T("Installation de la mise à jour…"));
        if (await Updater.InstallAsync(u) is { } error)
        {
            AddLog(LogKind.Error, "MISE À JOUR", error);
            SettingsPage.ShowUpdateStatus(error, true);
        }
    }

    // ------------------------------------------------------------------ Désinstallation

    /// <summary>
    /// Désinstallation sans script caché : Scanalyse libère ses appareils et son moteur web, efface lui-même ses dossiers,
    /// ouvre l'Explorateur sur Scanalyse.exe (et son dossier temporaire) pour que l'utilisateur les supprime, puis se ferme.
    /// </summary>
    private async Task UninstallAsync(IReadOnlyList<string> folders)
    {
        AppSettings.Frozen = true;
        _cleanedUp = true;
        ThemeManager.Changed -= OnThemeChanged;
        try
        {
            await Devices.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or InvalidOperationException or ObjectDisposedException or COMException)
        {
        }

        // Le moteur web garde son cache ouvert tant que ses processus tournent : on attend leur fin.
        try
        {
            var env = Web.CoreWebView2?.Environment;
            var exited = new TaskCompletionSource();
            if (env is not null)
            {
                env.BrowserProcessExited += (_, _) => exited.TrySetResult();
            }

            Web.Dispose();
            if (env is not null)
            {
                await Task.WhenAny(exited.Task, Task.Delay(TimeSpan.FromSeconds(6)));
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
        }

        Views.SettingsView.RemoveFirewallRule();
        FileAssociation.Unregister();
        var failed = new List<string>();
        foreach (var folder in folders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!await DeleteFolderAsync(folder))
            {
                failed.Add(folder);
            }
        }

        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{exe}\"") { UseShellExecute = true });
            }

            var extract = Path.Combine(Path.GetTempPath(), ".net", "Scanalyse");
            if (Directory.Exists(extract))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{extract}\"") { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this,
                Localization.T("Ces dossiers n'ont pas pu être effacés (fichier ouvert ailleurs ?) ; supprime-les à la main :") + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, failed),
                "Scanalyse", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        Close();
    }

    private static async Task<bool> DeleteFolderAsync(string folder)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    // Fichiers en lecture seule : sinon Windows refuse de les effacer.
                    foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }

                    Directory.Delete(folder, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(500);
            }
        }

        return !Directory.Exists(folder);
    }

    /// <summary>Appareils et moteur web déjà libérés : la fenêtre peut se fermer.</summary>
    private bool _cleanedUp;

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

    private readonly List<string> _pendingImages = [];

    /// <summary>
    /// Fichiers passés à Scanalyse (« Ouvrir avec », glisser sur Scanalyse.exe, double-clic sur un profil d'équipe) :
    /// images et PDF dans « Analyser une image », profil d'équipe à l'import, le reste dans « Étiquette imprimante »,
    /// qui reconnaît le langage (ZPL, TSPL, EPL2, DPL, SBPL, Fingerprint).
    /// </summary>
    public async Task OpenFilesAsync(IReadOnlyList<string> paths)
    {
        var images = new List<string>();
        foreach (var path in paths)
        {
            var ext = Path.GetExtension(path);
            if (ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || ImageTypes.ContainsKey(ext))
            {
                images.Add(path);
            }
            else if (ext.Equals(TeamProfile.Extension, StringComparison.OrdinalIgnoreCase))
            {
                NavSettings.IsChecked = true;
                SettingsPage.ShowSection("SecTeam");
                await SettingsPage.ImportTeamProfileFileAsync(path);
            }
            else if (LooksLikeConfiguration(path))
            {
                NavConfig.IsChecked = true;
                ConfigPage.OpenFile(path);
            }
            else
            {
                NavPrint.IsChecked = true;
                PrintPage.OpenFile(path);
            }
        }

        if (images.Count == 0)
        {
            return;
        }

        // L'Atelier démarre encore : les images sont ouvertes dès qu'il est prêt.
        if (!_atelier.IsReady)
        {
            _pendingImages.AddRange(images);
            return;
        }

        NavAnalyse.IsChecked = true;
        await OpenImagesAsync(images);
    }

    /// <summary>Fichier de configuration (SGD Zebra, JSON SGD, SETUP Fingerprint, réglages TSPL) plutôt qu'étiquette : onglet « Conf imprimante ».</summary>
    private static bool LooksLikeConfiguration(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var buffer = new char[16384];
            var head = new string(buffer, 0, reader.ReadBlock(buffer, 0, buffer.Length));
            if (head.Contains("! U1 ", StringComparison.Ordinal) || head.Contains("! U ", StringComparison.Ordinal) || head.Contains("{}{\"", StringComparison.Ordinal)
                || head.Contains("SETUP \"", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Réglages sans étiquette : ^JU / ~SD sans champ ^FD, ou TSPL SET / INITIALPRINTER sans PRINT.
            var label = head.Contains("^FD", StringComparison.OrdinalIgnoreCase) || head.Contains("PRINT ", StringComparison.OrdinalIgnoreCase);
            return !label && (head.Contains("^JU", StringComparison.OrdinalIgnoreCase) || head.Contains("~SD", StringComparison.OrdinalIgnoreCase)
                || head.Contains("INITIALPRINTER", StringComparison.OrdinalIgnoreCase) || head.Contains("SET ", StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("Importer une ou plusieurs images à analyser"),
            Filter = Localization.T("Images et PDF (PNG, JPG, BMP, GIF, WebP, TIFF, PDF)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.pdf|Tous les fichiers|*.*"),
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await OpenImagesAsync(dialog.FileNames);
    }

    /// <summary>Une image : analyse directe. Plusieurs images ou un PDF : analyse en lot (une entrée d'historique par image ou page).</summary>
    private async Task OpenImagesAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count > 1 || paths.Any(f => Path.GetExtension(f).Equals(".pdf", StringComparison.OrdinalIgnoreCase)))
        {
            var files = new List<(string Name, byte[] Data)>();
            foreach (var f in paths)
            {
                try
                {
                    files.Add((Path.GetFileName(f), await File.ReadAllBytesAsync(f)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    AddLog(LogKind.Error, "LOT", $"{Path.GetFileName(f)} : {ex.Message}");
                }
            }

            await StartBatchAsync(files);
            return;
        }

        var path = paths[0];
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
