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
using Scanalyse.Desktop.Scanners;

namespace Scanalyse.Desktop.Views;

/// <summary>Banc d'essai : contenu du code ↔ données reçues de la douchette.</summary>
public partial class BenchView : UserControl
{
    /// <summary>« Code testé » : sous 600 points de large, le texte et les choix passent sous la vignette.</summary>
    private void TestedGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 600;
        Grid.SetRow(TestedInfo, narrow ? 1 : 0);
        Grid.SetColumn(TestedInfo, narrow ? 0 : 2);
        Grid.SetColumnSpan(TestedInfo, narrow ? 3 : 1);
        TestedInfo.Margin = narrow ? new Thickness(0, 14, 0, 0) : new Thickness(0);
    }

    private void ConfigCodes_Click(object sender, RoutedEventArgs e) =>
        new ConfigCodesWindow(Renderer) { Owner = Window.GetWindow(this) }.Show();

    private readonly ContentAnalyzer _analyzer = new();
    private string? _expectedAim;
    private bool _settingExpected;
    private bool _settingReceived;
    private ScanEvent? _lastScan;

    public BenchView()
    {
        InitializeComponent();
        HintAligner.Attach(ExpectedBox, ExpectedHint);
        ScrollChaining.Enable(PageScroll);
    }

    /// <summary>Dessine un code (texte, type de l'Atelier) : fourni par la fenêtre principale.</summary>
    public Func<string, string, Task<(byte[]? Png, string? Error)>>? Renderer { get; set; }

    /// <summary>Déclenché après chaque comparaison, avec tout ce qu'il faut pour l'historique.</summary>
    public event Action<BenchSnapshot>? Compared;

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

    /// <summary>
    /// Lecture reçue en direct (port série ou HID POS) : les octets exacts, sans traduction clavier.
    /// Si le contenu attendu est connu, la comparaison est lancée tout de suite.
    /// </summary>
    public void ReceiveScan(ScanEvent scan)
    {
        // Lecture vide (touches de modification seules, déclenchement sans code) : rien à comparer.
        if (string.IsNullOrEmpty(scan.Text))
        {
            return;
        }

        if (ZoomOverlay.Visibility == Visibility.Visible)
        {
            ZoomOverlay.Visibility = Visibility.Collapsed;
        }

        _settingReceived = true;
        ReceivedBox.Text = scan.Text;
        _settingReceived = false;
        _lastScan = scan;
        ReceivedOrigin.Text = $"Reçu en direct · {scan.Source} · {scan.Raw.Length} octets"
            + (scan.Symbology is null ? string.Empty : $" · symbologie annoncée : {scan.Symbology}")
            + (string.IsNullOrEmpty(scan.Detail) ? string.Empty : $" · {scan.Detail}");
        HexBox.Text = scan.Hex;
        ReceivedOrigin.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(ExpectedBox.Text))
        {
            if (AppSettings.Load().AutoCompare)
            {
                Compare_Click(this, new RoutedEventArgs());
            }
        }
        else
        {
            // Pas de référence : on n'invente pas d'écart, on montre ce que contient la lecture.
            AnalyzeText(scan.Text, null, "Lecture reçue");
        }
    }

    /// <summary>Analyse le contenu d'une lecture (ou de la référence) et l'affiche comme résultat.</summary>
    private void AnalyzeText(string text, string? aim, string prefix)
    {
        var payload = text.TrimEnd('\r', '\n', '\t');
        var analysis = _analyzer.Analyze(new ScanInput(payload, SymbologyIdentifier: aim));
        ResultTitle.Text = $"{prefix} : {analysis.Title}";
        ResultSubtitle.Text = $"{payload.Length} caractères · {ControlCharacters.Visualize(text)}";
        FindingsList.ItemsSource = analysis.Alerts
            .Select(a => FindingView.From(new Finding("ALERT", a.Severity, a.Message, string.Empty)))
            .ToList();
        FieldsList.ItemsSource = analysis.Fields
            .Select(f => new FieldView(f.Label, f.Sensitive ? "•••••• (masqué)" : f.Value, f.Hint ?? string.Empty))
            .ToList();
        ResultActions.Visibility = Visibility.Collapsed;
        ShowStructure(text);
        ResultScroll.ScrollToTop();
    }

    // ------------------------------------------------------------------ Vue structurée

    private bool _structOn = true;
    private string _structRaw = string.Empty;

    /// <summary>Découpe le contenu en sections et champs (séparateurs RS, GS, US) ; bouton pour revenir au texte brut.</summary>
    private void ShowStructure(string text, bool touchSubtitle = true)
    {
        StructPanel.Children.Clear();
        var view = StructuredData.Parse(text);
        _structRaw = touchSubtitle ? ResultSubtitle.Text : string.Empty;
        StructToggle.Visibility = view is null ? Visibility.Collapsed : Visibility.Visible;
        if (view is null)
        {
            StructPanel.Visibility = Visibility.Collapsed;
            return;
        }

        if (view.Envelope is not null)
        {
            StructPanel.Children.Add(Muted(view.Envelope, 12.5, new Thickness(0, 0, 0, 6)));
        }

        foreach (var section in view.Sections)
        {
            var box = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8) };
            box.SetResourceReference(Border.BackgroundProperty, "Surface2");
            var stack = new StackPanel();
            var head = new TextBlock { Text = $"{section.Title} · {section.Fields.Count} champ{(section.Fields.Count > 1 ? "s" : string.Empty)}", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };
            head.SetResourceReference(TextBlock.ForegroundProperty, "BrandText");
            stack.Children.Add(head);
            foreach (var f in section.Fields)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var label = new StackPanel();
                label.Children.Add(Muted(f.Identifier is null ? $"#{f.Index}" : $"#{f.Index} {f.Identifier}", 12, default));
                if (f.IdentifierName is not null)
                {
                    label.Children.Add(Muted(f.IdentifierName, 10.5, default));
                }

                row.Children.Add(label);
                var raw = f.Identifier is null ? f.Value : f.Value[f.Identifier.Length..];
                var shown = f.Parts.Count > 1 ? string.Join("   │   ", f.Parts.Select(ControlCharacters.Visualize)) : ControlCharacters.Visualize(raw);
                var value = new TextBox
                {
                    Text = shown.Length == 0 ? "(vide)" : shown,
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Padding = new Thickness(0),
                    FontFamily = (FontFamily)FindResource("MonoFont"),
                    FontSize = 12.5,
                    TextWrapping = TextWrapping.Wrap,
                };
                value.SetResourceReference(Control.ForegroundProperty, shown.Length == 0 ? "Muted" : "Ink");
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                stack.Children.Add(row);
            }

            box.Child = stack;
            StructPanel.Children.Add(box);
        }

        foreach (var r in view.Remarks)
        {
            StructPanel.Children.Add(Muted("• " + r, 12, new Thickness(0, 2, 0, 0)));
        }

        StructToggle.IsChecked = _structOn;
        ApplyStructure();
    }

    private TextBlock Muted(string text, double size, Thickness margin)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private void StructToggle_Changed(object sender, RoutedEventArgs e)
    {
        _structOn = StructToggle.IsChecked == true;
        ApplyStructure();
    }

    /// <summary>Vue structurée : sections affichées et texte brut condensé ; sinon texte brut complet.</summary>
    private void ApplyStructure()
    {
        var on = _structOn && StructToggle.Visibility == Visibility.Visible;
        StructPanel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (_structRaw.Length > 0)
        {
            var count = _structRaw.Split(" · ")[0];
            ResultSubtitle.Text = on ? $"{count} · affichage par sections (décoche pour le texte brut)" : _structRaw;
        }
    }

    /// <summary>« Recréer ce code » : la lecture (sinon la référence) part dans « Créer un code ».</summary>
    public event Action<string, string>? RecreateRequested;

    private void Recreate_Click(object sender, RoutedEventArgs e)
    {
        var source = ReceivedBox.Text.Length > 0 ? ReceivedBox.Text : ControlCharacters.FromVisualized(ExpectedBox.Text);
        var text = source.TrimEnd('\r', '\n', '\t');
        if (text.Length == 0)
        {
            return;
        }

        var chosen = PreviewTypes.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "auto";
        RecreateRequested?.Invoke(text, chosen == "auto" ? GuessType(text) : chosen);
    }

    /// <summary>« Code imprimante » : la lecture (sinon la référence) part dans l'onglet d'impression.</summary>
    public event Action<string, string>? PrintRequested;

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var source = ReceivedBox.Text.Length > 0 ? ReceivedBox.Text : ControlCharacters.FromVisualized(ExpectedBox.Text);
        var text = source.TrimEnd('\r', '\n', '\t');
        if (text.Length == 0)
        {
            return;
        }

        var chosen = PreviewTypes.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "auto";
        PrintRequested?.Invoke(text, chosen == "auto" ? GuessType(text) : chosen);
    }

    private void HexToggle_Changed(object sender, RoutedEventArgs e) =>
        HexBox.Visibility = HexToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>La lecture devient la référence : on rescanne ensuite pour vérifier la régularité.</summary>
    private void UseAsReference_Click(object sender, RoutedEventArgs e)
    {
        var received = ReceivedBox.Text;
        if (received.Length == 0)
        {
            return;
        }

        // Les suffixes du scanner (Entrée, Tab) ne font pas partie du code.
        var reference = received.TrimEnd('\r', '\n', '\t');
        _settingExpected = true;
        ExpectedBox.Text = ControlCharacters.Visualize(reference);
        _settingExpected = false;
        _expectedAim = null;
        ExpectedSource.Text = "Référence reprise d'une lecture : scanne à nouveau le code pour vérifier qu'il se lit toujours pareil.";
        ExpectedSource.Visibility = Visibility.Visible;
        ExpectedHint.Visibility = Visibility.Collapsed;
        ClearPreview();
        _ = GeneratePreviewAsync();
        ClearReception();
        ResultTitle.Text = "Référence enregistrée";
        ResultSubtitle.Text = "Scanne le même code : la lecture sera comparée à cette référence.";
        FocusReception();
    }

    private void AnalyzeReceived_Click(object sender, RoutedEventArgs e) => AnalyzeText(ReceivedBox.Text, null, "Lecture reçue");

    /// <summary>Tout effacer pour un nouveau code.</summary>
    private void NewTest_Click(object sender, RoutedEventArgs e)
    {
        _settingExpected = true;
        ExpectedBox.Clear();
        _settingExpected = false;
        _expectedAim = null;
        ExpectedSource.Visibility = Visibility.Collapsed;
        ExpectedHint.Visibility = Visibility.Visible;
        ClearPreview();
        ClearReception();
        ResultTitle.Text = "Résultat";
        ResultSubtitle.Text = "Scanne un code : il est analysé. Avec une référence, la lecture est comparée au contenu attendu.";
        FocusReception();
    }

    private void ClearReception()
    {
        _settingReceived = true;
        ReceivedBox.Clear();
        _settingReceived = false;
        _lastScan = null;
        ReceivedOrigin.Visibility = Visibility.Collapsed;
        FindingsList.ItemsSource = null;
        FieldsList.ItemsSource = null;
        ResultActions.Visibility = Visibility.Collapsed;
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

    private void ReceivedBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReceivedVisual.Text = ReceivedBox.Text.Length == 0
            ? string.Empty
            : $"Reçu ({ReceivedBox.Text.Length} caractères) : {ControlCharacters.Visualize(ReceivedBox.Text)}";

        // Saisie ou lecture clavier : l'origine « en direct » ne s'applique plus.
        if (!_settingReceived && ReceivedOrigin is not null)
        {
            ReceivedOrigin.Visibility = Visibility.Collapsed;
            _lastScan = null;
            HexBox.Text = Scanalyse.Core.Devices.RawData.ToHex(System.Text.Encoding.UTF8.GetBytes(ReceivedBox.Text));
        }

        if (HexToggle is not null)
        {
            HexToggle.Visibility = ReceivedBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ReceivedBox.Text.Length == 0)
            {
                HexToggle.IsChecked = false;
            }
        }
    }

    private void ExpectedBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ExpectedHint is not null)
        {
            ExpectedHint.Visibility = ExpectedBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

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

    private async void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ExpectedBox.Text))
        {
            if (ReceivedBox.Text.Length > 0)
            {
                AnalyzeText(ReceivedBox.Text, null, "Lecture reçue");
            }

            return;
        }

        var result = ScanComparer.Compare(ExpectedBox.Text, ReceivedBox.Text);
        ResultActions.Visibility = result.OtherCode ? Visibility.Visible : Visibility.Collapsed;
        ShowStructure(ReceivedBox.Text.Length > 0 ? ReceivedBox.Text : ControlCharacters.FromVisualized(ExpectedBox.Text), touchSubtitle: false);
        ResultScroll.ScrollToTop();
        ResultTitle.Text = result.OtherCode ? "Lecture d'un autre code" : result.WorstSeverity switch
        {
            Severity.Ok => "Lecture conforme",
            Severity.Info => result.DataMatches ? "Données conformes, avec ajouts du scanner" : "À vérifier",
            Severity.Warning => "À vérifier",
            _ => "Problème de lecture",
        };
        ResultSubtitle.Text = result.OtherCode
            ? "La lecture ne correspond pas à la référence affichée : vérifie le code scanné, ou prends cette lecture comme nouvelle référence."
            : result.DataMatches
            ? "Les données utiles sont identiques au contenu du code."
            : "Les données reçues ne correspondent pas au contenu du code.";
        FindingsList.ItemsSource = result.Findings.Select(FindingView.From).ToList();
        FieldsList.ItemsSource = null;
        var expected = ExpectedBox.Text;
        var received = ReceivedBox.Text;
        var scan = _lastScan;
        if (PreviewImage.Source is null)
        {
            await GeneratePreviewAsync();
        }

        Compared?.Invoke(new BenchSnapshot(
            ControlCharacters.Visualize(ControlCharacters.FromVisualized(expected)),
            ControlCharacters.Visualize(received),
            scan?.Source ?? "Douchette en mode clavier",
            scan?.Symbology,
            ResultTitle.Text,
            PreviewPng(),
            result));
    }

    /// <summary>Vignette du code testé en PNG (pour l'historique).</summary>
    private byte[]? PreviewPng()
    {
        if (PreviewImage.Source is not BitmapSource bmp)
        {
            return null;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
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
        ClearReception();
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

/// <summary>Comparaison terminée, telle qu'elle est enregistrée dans l'historique.</summary>
/// <param name="Expected">Contenu du code (notation lisible).</param>
/// <param name="Received">Données reçues (notation lisible).</param>
/// <param name="Via">Liaison de la douchette (« COM5 · 9600 8N1 », mode clavier…).</param>
/// <param name="Symbology">Symbologie annoncée (HID POS).</param>
/// <param name="Verdict">Titre du résultat.</param>
/// <param name="Image">Vignette du code testé (PNG).</param>
/// <param name="Result">Résultat détaillé.</param>
public sealed record BenchSnapshot(string Expected, string Received, string Via, string? Symbology, string Verdict, byte[]? Image, ComparisonResult Result);

/// <summary>Information extraite prête à afficher.</summary>
public sealed record FieldView(string Label, string Value, string Hint);
