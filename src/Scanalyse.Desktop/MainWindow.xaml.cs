using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;
using Scanalyse.Core.Text;

namespace Scanalyse.Desktop;

/// <summary>Fenêtre principale : banc d'essai « contenu du code ↔ données reçues ».</summary>
public partial class MainWindow : Window
{
    private readonly ContentAnalyzer _analyzer = new();

    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"version {version.Major}.{version.Minor}.{version.Build}";
        Loaded += (_, _) => ReceivedBox.Focus();
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
        var analysis = _analyzer.Analyze(new ScanInput(text));
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
