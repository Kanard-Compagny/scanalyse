using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;
using Scanalyse.Core.Text;

namespace Scanalyse.Desktop;

/// <summary>Fenêtre principale : l'Atelier (création et analyse d'images) et le banc d'essai douchette.</summary>
public partial class MainWindow : Window
{
    private readonly ContentAnalyzer _analyzer = new();
    private readonly AtelierHost _atelier;
    private string? _expectedAim;
    private bool _settingExpected;

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"version {version.Major}.{version.Minor}.{version.Build}";

        _atelier = new AtelierHost(Web);
        _atelier.CompareRequested += OnCompareRequested;
        Loaded += async (_, _) => await StartAtelierAsync();
    }

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

    private void ShowWebError(string message)
    {
        Web.Visibility = Visibility.Collapsed;
        WebErrorText.Text = message;
        WebError.Visibility = Visibility.Visible;
    }

    /// <summary>L'Atelier envoie un code lu sur une image : on le place comme référence dans le banc d'essai.</summary>
    private void OnCompareRequested(CompareRequest request)
    {
        _settingExpected = true;
        ExpectedBox.Text = ControlCharacters.Visualize(request.Text);
        _settingExpected = false;

        _expectedAim = string.IsNullOrEmpty(request.SymbologyIdentifier) ? null : request.SymbologyIdentifier;
        var aim = _expectedAim is null ? string.Empty : $" · identifiant {_expectedAim}";
        ExpectedSource.Text = $"Repris de l'Atelier : {request.Format}{aim}";
        ExpectedSource.Visibility = Visibility.Visible;

        ReceivedBox.Clear();
        FindingsList.ItemsSource = null;
        FieldsList.ItemsSource = null;
        ResultTitle.Text = "Scanne maintenant le même code";
        ResultSubtitle.Text = "Avec la douchette, dans le champ « Données reçues », puis clique sur Comparer.";
        Tabs.SelectedItem = BenchTab;
        Dispatcher.InvokeAsync(() => ReceivedBox.Focus(), DispatcherPriority.Input);
    }

    private void ExpectedBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingExpected || ExpectedSource is null)
        {
            return;
        }

        _expectedAim = null;
        ExpectedSource.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Entrée et Tab sont insérés tels quels (CR, HT) pour voir exactement ce que la douchette envoie,
    /// au lieu de valider la fenêtre ou de changer de champ.
    /// </summary>
    private void ReceivedBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var insert = e.Key switch
        {
            Key.Enter => "\r",
            Key.Tab => "\t",
            _ => null,
        };

        if (insert is null)
        {
            return;
        }

        var box = (TextBox)sender;
        var caret = box.CaretIndex;
        box.Text = box.Text.Insert(caret, insert);
        box.CaretIndex = caret + insert.Length;
        e.Handled = true;
    }

    private void ReceivedBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ReceivedVisual.Text = ReceivedBox.Text.Length == 0
            ? string.Empty
            : $"Reçu ({ReceivedBox.Text.Length} caractères) : {ControlCharacters.Visualize(ReceivedBox.Text)}";

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        var result = ScanComparer.Compare(ExpectedBox.Text, ReceivedBox.Text);
        ResultTitle.Text = result.WorstSeverity switch
        {
            Severity.Ok => "Lecture conforme",
            Severity.Info => result.DataMatches ? "Données conformes, avec ajouts du scanner" : "À vérifier",
            Severity.Warning => "À vérifier",
            _ => "Problème de lecture",
        };
        ResultSubtitle.Text = result.DataMatches
            ? "Les données utiles sont identiques au contenu du code."
            : "Les données reçues ne correspondent pas au contenu du code.";
        FindingsList.ItemsSource = result.Findings.Select(FindingView.From).ToList();
        FieldsList.ItemsSource = null;
    }

    private void Analyze_Click(object sender, RoutedEventArgs e)
    {
        var text = ControlCharacters.FromVisualized(ExpectedBox.Text);
        var analysis = _analyzer.Analyze(new ScanInput(text, SymbologyIdentifier: _expectedAim));
        ResultTitle.Text = analysis.Title;
        ResultSubtitle.Text = $"{text.Length} caractères · {ControlCharacters.Visualize(text)}";
        FindingsList.ItemsSource = analysis.Alerts
            .Select(a => FindingView.From(new Finding("ALERT", a.Severity, a.Message, string.Empty)))
            .ToList();
        FieldsList.ItemsSource = analysis.Fields
            .Select(f => new FieldView(f.Label, f.Sensitive ? "•••••• (masqué)" : f.Value, f.Hint ?? string.Empty))
            .ToList();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ReceivedBox.Clear();
        ReceivedBox.Focus();
    }
}

/// <summary>Constat prêt à afficher.</summary>
public sealed record FindingView(string Title, string Explanation, string Fix, Brush Accent, Brush Tint, Visibility FixVisibility)
{
    private static readonly Brush OkAccent = Freeze(new SolidColorBrush(Color.FromRgb(0x1D, 0x7A, 0x4A)));
    private static readonly Brush OkTint = Freeze(new SolidColorBrush(Color.FromRgb(0xE3, 0xF4, 0xEA)));
    private static readonly Brush InfoAccent = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x3F, 0xD6)));
    private static readonly Brush InfoTint = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEB, 0xFB)));
    private static readonly Brush WarnAccent = Freeze(new SolidColorBrush(Color.FromRgb(0xA8, 0x5A, 0x00)));
    private static readonly Brush WarnTint = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE0)));
    private static readonly Brush ErrorAccent = Freeze(new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)));
    private static readonly Brush ErrorTint = Freeze(new SolidColorBrush(Color.FromRgb(0xFD, 0xE7, 0xE5)));

    public static FindingView From(Finding f)
    {
        var (accent, tint) = f.Severity switch
        {
            Severity.Ok => (OkAccent, OkTint),
            Severity.Info => (InfoAccent, InfoTint),
            Severity.Warning => (WarnAccent, WarnTint),
            _ => (ErrorAccent, ErrorTint),
        };
        var fix = string.IsNullOrWhiteSpace(f.Fix) ? string.Empty : "À faire : " + f.Fix;
        return new FindingView(f.Title, f.Explanation, fix, accent, tint, fix.Length > 0 ? Visibility.Visible : Visibility.Collapsed);
    }

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }
}

/// <summary>Information extraite prête à afficher.</summary>
public sealed record FieldView(string Label, string Value, string Hint);
