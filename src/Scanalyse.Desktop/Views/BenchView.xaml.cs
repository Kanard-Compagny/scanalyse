using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;
using Scanalyse.Core.Text;

namespace Scanalyse.Desktop.Views;

/// <summary>Banc d'essai : contenu du code ↔ données reçues de la douchette.</summary>
public partial class BenchView : UserControl
{
    private readonly ContentAnalyzer _analyzer = new();
    private string? _expectedAim;
    private bool _settingExpected;

    public BenchView()
    {
        InitializeComponent();
    }

    /// <summary>Déclenché après chaque comparaison (résultat, données reçues en notation lisible).</summary>
    public event Action<ComparisonResult, string>? Compared;

    /// <summary>Place un code lu dans l'Atelier comme référence, puis attend la lecture douchette.</summary>
    public void SetExpected(CompareRequest request)
    {
        _settingExpected = true;
        ExpectedBox.Text = ControlCharacters.Visualize(request.Text);
        _settingExpected = false;

        _expectedAim = string.IsNullOrEmpty(request.SymbologyIdentifier) ? null : request.SymbologyIdentifier;
        var aim = _expectedAim is null ? string.Empty : $" · identifiant {_expectedAim}";
        ExpectedSource.Text = $"Repris de l'analyse d'image : {request.Format}{aim}";
        ExpectedSource.Visibility = Visibility.Visible;

        ReceivedBox.Clear();
        FindingsList.ItemsSource = null;
        FieldsList.ItemsSource = null;
        ResultTitle.Text = "Scanne maintenant le même code";
        ResultSubtitle.Text = "Avec la douchette, dans le champ « Données reçues », puis clique sur Comparer.";
        Dispatcher.InvokeAsync(() => ReceivedBox.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Donne le focus au champ de réception, prêt pour un scan.</summary>
    public void FocusReception() => Dispatcher.InvokeAsync(() => ReceivedBox.Focus(), DispatcherPriority.Input);

    /// <summary>
    /// Entrée et Tab sont insérés tels quels (CR, HT) pour voir exactement ce que la douchette envoie,
    /// au lieu de valider ou de changer de champ.
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

    private void ExpectedBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_settingExpected || ExpectedSource is null)
        {
            return;
        }

        _expectedAim = null;
        ExpectedSource.Visibility = Visibility.Collapsed;
    }

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
        Compared?.Invoke(result, ControlCharacters.Visualize(ReceivedBox.Text));
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

/// <summary>Constat prêt à afficher ; ses couleurs sont choisies par le thème selon <see cref="Severity"/>.</summary>
public sealed record FindingView(Severity Severity, string Title, string Explanation, string Fix)
{
    public Visibility ExplanationVisibility => string.IsNullOrWhiteSpace(Explanation) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility FixVisibility => string.IsNullOrWhiteSpace(Fix) ? Visibility.Collapsed : Visibility.Visible;

    public static FindingView From(Finding f) =>
        new(f.Severity, f.Title, f.Explanation, string.IsNullOrWhiteSpace(f.Fix) ? string.Empty : "À faire : " + f.Fix);
}

/// <summary>Information extraite prête à afficher.</summary>
public sealed record FieldView(string Label, string Value, string Hint);
