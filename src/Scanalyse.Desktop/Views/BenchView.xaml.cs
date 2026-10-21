using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Scanalyse.Core.Content;
using Scanalyse.Core.Diagnostics;
using Scanalyse.Core.Gs1;
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

    /// <summary>Dessine un code (texte, type de l'Atelier) : fourni par la fenêtre principale.</summary>
    public Func<string, string, Task<(byte[]? Png, string? Error)>>? Renderer { get; set; }

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

        if (request.ImagePng is { Length: > 0 } png)
        {
            ShowPreview(png, $"Vignette de l'image analysée · {request.Format}");
        }
        else
        {
            _ = GeneratePreviewAsync();
        }

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

        // Fin de lecture (suffixe Entrée) pendant l'agrandissement : on revient au résultat.
        if (e.Key == Key.Enter && ZoomOverlay.Visibility == Visibility.Visible)
        {
            CloseZoom();
        }
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
        ClearPreview();
    }

    // ------------------------------------------------------------------ Aperçu du code testé

    private async void Preview_Click(object sender, RoutedEventArgs e) => await GeneratePreviewAsync();

    private async Task GeneratePreviewAsync()
    {
        if (Renderer is null || string.IsNullOrWhiteSpace(ExpectedBox.Text))
        {
            return;
        }

        var chosen = PreviewTypes.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "auto";
        var text = ControlCharacters.FromVisualized(ExpectedBox.Text);
        var type = chosen == "auto" ? GuessType(text) : chosen;
        PreviewSource.Text = "Génération de l'aperçu…";
        var (png, error) = await Renderer(text, type);
        if (png is { Length: > 0 })
        {
            ShowPreview(png, $"Aperçu généré · {TypeLabel(type)}{(chosen == "auto" ? " (type choisi automatiquement)" : string.Empty)}");
        }
        else
        {
            ClearPreview();
            PreviewSource.Text = $"Aperçu impossible en {TypeLabel(type)} : {error}";
        }
    }

    private void ShowPreview(byte[] png, string caption)
    {
        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }

            bmp.Freeze();
            PreviewImage.Source = bmp;
            PreviewEmpty.Visibility = Visibility.Collapsed;
            PreviewSource.Text = caption;
            SetZoomAvailable(true);
        }
        catch (Exception ex) when (ex is NotSupportedException or IOException or InvalidOperationException)
        {
            ClearPreview();
        }
    }

    private void ClearPreview()
    {
        if (PreviewImage is null)
        {
            return;
        }

        PreviewImage.Source = null;
        PreviewEmpty.Visibility = Visibility.Visible;
        SetZoomAvailable(false);
        PreviewSource.Text = "La vignette s'affiche quand la référence vient d'une image analysée. Sinon, choisis le type et génère l'aperçu.";
    }

    // ------------------------------------------------------------------ Agrandissement

    private void SetZoomAvailable(bool on)
    {
        ZoomButton.IsEnabled = on;
        ZoomHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        PreviewBox.Cursor = on ? Cursors.Hand : null;
    }

    private void PreviewBox_Click(object sender, MouseButtonEventArgs e) => OpenZoom();

    private void Zoom_Click(object sender, RoutedEventArgs e) => OpenZoom();

    private void ZoomClose_Click(object sender, RoutedEventArgs e) => CloseZoom();

    /// <summary>Un clic hors de la feuille blanche ferme l'agrandissement.</summary>
    private void ZoomOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (!ZoomSheet.IsMouseOver)
        {
            CloseZoom();
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && ZoomOverlay.Visibility == Visibility.Visible)
        {
            CloseZoom();
            e.Handled = true;
        }
    }

    private void OpenZoom()
    {
        if (PreviewImage.Source is not BitmapSource bmp)
        {
            return;
        }

        ZoomImage.Source = bmp;
        ZoomCaption.Text = $"{PreviewSource.Text} · scanne l'écran avec la douchette : la lecture arrive directement dans « Données reçues ».";
        ZoomOverlay.Visibility = Visibility.Visible;
        FitZoom();
        FocusReception();
    }

    private void CloseZoom()
    {
        ZoomOverlay.Visibility = Visibility.Collapsed;
        FocusReception();
    }

    private void ZoomArea_SizeChanged(object sender, SizeChangedEventArgs e) => FitZoom();

    /// <summary>
    /// Agrandit d'un facteur entier en pixels écran : chaque module garde exactement la même largeur,
    /// sinon les barres fines et épaisses deviennent irrégulières et le code ne se lit plus.
    /// </summary>
    private void FitZoom()
    {
        if (ZoomImage.Source is not BitmapSource bmp || ZoomArea.ActualWidth <= 0 || ZoomArea.ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var availW = ZoomArea.ActualWidth * dpi.DpiScaleX;
        var availH = ZoomArea.ActualHeight * dpi.DpiScaleY;
        var fit = Math.Min(availW / bmp.PixelWidth, availH / bmp.PixelHeight);
        var factor = fit >= 1 ? Math.Floor(fit) : fit;
        ZoomImage.Width = bmp.PixelWidth * factor / dpi.DpiScaleX;
        ZoomImage.Height = bmp.PixelHeight * factor / dpi.DpiScaleY;
    }

    /// <summary>Type le plus probable pour dessiner une référence saisie à la main.</summary>
    internal static string GuessType(string text)
    {
        var looksGs1 = text.Contains(ControlCharacters.GroupSeparator) || text.StartsWith('(');
        if (looksGs1 && Gs1Parser.Parse(text) is { } msg && msg.Elements.All(e => e.Definition is not null))
        {
            return "gs1datamatrix";
        }

        if (text.Length > 0 && text.All(char.IsAsciiDigit))
        {
            if (text.Length is 12 or 13)
            {
                return "ean13";
            }

            if (text.Length == 8)
            {
                return "ean8";
            }
        }

        return text.Length is > 0 and <= 40 && text.All(c => c is >= ' ' and < '\u007F') ? "code128" : "datamatrix";
    }

    private static string TypeLabel(string id) => id switch
    {
        "gs1datamatrix" => "GS1 DataMatrix",
        "datamatrix" => "Data Matrix",
        "gs1qr" => "GS1 QR Code",
        "gs1128" => "GS1-128",
        "code128" => "Code 128",
        "ean13" => "EAN-13",
        "ean8" => "EAN-8",
        "pdf417" => "PDF417",
        _ => id,
    };

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
        if (PreviewImage.Source is null)
        {
            _ = GeneratePreviewAsync();
        }
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
