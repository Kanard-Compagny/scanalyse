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
        _atelier.Decoded += OnDecoded;
        Bench.Compared += OnBenchCompared;
        Bench.Renderer = (text, type) => _atelier.RenderAsync(text, type);
        _atelier.ModeChanged += OnAtelierModeChanged;
        Symbols.CreateRequested += OnCreateRequested;
        Devices.Scanned += OnDeviceScanned;
        Devices.StatusChanged += OnScannerStatus;
        Devices.Log += (kind, text) => AddLog(kind, "DOUCHETTE", text);
        ThemeManager.Changed += OnThemeChanged;
        _captureTimer.Tick += CaptureTimer_Tick;

        InputBindings.Add(new KeyBinding(new RelayCommand(() => Paste_Click(this, new RoutedEventArgs())), Key.V, ModifierKeys.Control | ModifierKeys.Shift));

        _ready = true;
        NavAnalyse.IsChecked = true;
        OnThemeChanged();
        AddLog(LogKind.Info, "SCANALYSE", "Prêt. L'analyse est faite sur ce PC, rien n'est envoyé sur Internet.");

        Loaded += async (_, _) =>
        {
            await Devices.InitializeAsync();
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

    private void OnDecoded(string source, IReadOnlyList<DecodedCode> codes)
    {
        if (codes.Count == 0)
        {
            AddLog(LogKind.Warning, "IMAGE", $"{source} : aucun code détecté.");
            return;
        }

        AddLog(LogKind.Ok, "IMAGE", $"{source} : {codes.Count} code{(codes.Count > 1 ? "s" : string.Empty)} détecté{(codes.Count > 1 ? "s" : string.Empty)}.");
        foreach (var c in codes)
        {
            AddLog(LogKind.Info, c.Format.ToUpperInvariant(), c.Text);
        }
    }

    private void OnBenchCompared(ComparisonResult result, string receivedVisual)
    {
        AddLog(LogKind.Info, "REÇU", receivedVisual.Length > 0 ? receivedVisual : "(rien)");
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

    // ------------------------------------------------------------------ Douchettes

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

    private void OnScannerStatus(ScannerStatus status)
    {
        SourceText.Text = status.Text;
        SourceDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
            status.Error ? "BadFg" : status.Kind == ScanSourceKind.Keyboard ? "Muted" : "OkFg");
    }

    private void Source_Click(object sender, RoutedEventArgs e) => NavDevices.IsChecked = true;

    // ------------------------------------------------------------------ Navigation

    private async void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
        {
            return;
        }

        AtelierPage.Visibility = Visibility.Collapsed;
        Bench.Visibility = Visibility.Collapsed;
        Symbols.Visibility = Visibility.Collapsed;
        Devices.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;

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

    private void ToggleMenu_Click(object sender, RoutedEventArgs e)
    {
        var narrow = Sidebar.Width > SidebarNarrow;
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

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
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
