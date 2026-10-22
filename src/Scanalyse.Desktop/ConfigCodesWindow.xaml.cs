using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Scanalyse.Core.Scanners;

namespace Scanalyse.Desktop;

/// <summary>
/// Codes-barres de configuration des douchettes (Honeywell, Datalogic), à scanner sur l'écran (lecteurs 2D) ou sur papier
/// (lecteurs laser) ; pour Zebra, renvoi vers le guide officiel du modèle.
/// </summary>
public partial class ConfigCodesWindow : Window
{
    private const string ZebraGuide = "https://www.zebra.com/content/dam/support-dam/en/documentation/unrestricted/guide/product/ds2208-prg-en.pdf";

    public ConfigCodesWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => Show("Honeywell");
    }

    private void Brand_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded && sender is RadioButton { Tag: string brand })
        {
            Show(brand);
        }
    }

    private void Show(string brand)
    {
        CodesPanel.Children.Clear();
        BrandNote.Text = Localization.T(brand switch
        {
            "Honeywell" => "Codes de menu Honeywell (Voyager, Xenon, Granit, Hyperion…) : un seul code suffit, le réglage est enregistré. Scanne-les sur l'écran (lecteurs 2D) ou imprime-les pour un lecteur laser.",
            "Datalogic" => "Datalogic (QuickScan, Gryphon, PowerScan…) : « Réglages par défaut » se scanne seul. Pour un autre réglage tiré du guide du modèle : entrer en programmation, scanner les codes du guide, puis quitter en enregistrant.",
            _ => "Zebra ne publie pas le contenu de ses codes de programmation : ils se trouvent dans le guide de référence (PRG) de chaque modèle, ou se créent avec l'outil gratuit 123Scan.",
        });
        if (brand == "Zebra")
        {
            var open = new Button { Style = (Style)FindResource("BtnSecondary"), Content = Localization.T("Ouvrir le guide du DS2208 (zebra.com)"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            open.Click += (_, _) => Process.Start(new ProcessStartInfo(ZebraGuide) { UseShellExecute = true });
            CodesPanel.Children.Add(open);
            CodesPanel.Children.Add(new TextBlock
            {
                Text = Localization.T("Dans le guide : « Set Defaults » (chapitre Configuration), « Country Keyboard Types » → French, « Scan Data Transmission Format » pour les suffixes, « Function Key Mapping » / « FN1 Substitution » pour le séparateur GS1."),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(0, 12, 0, 0),
            });
            return;
        }

        var target = brand == "Honeywell" ? ScannerBrand.Honeywell : ScannerBrand.Datalogic;
        foreach (var group in ScannerConfigCodes.All.Where(c => c.Brand == target).GroupBy(c => c.Group))
        {
            CodesPanel.Children.Add(new TextBlock { Text = Localization.T(group.Key), Style = (Style)FindResource("CardTitle"), Margin = new Thickness(0, 14, 0, 8) });
            var wrap = new WrapPanel();
            foreach (var code in group)
            {
                wrap.Children.Add(Card(code));
            }

            CodesPanel.Children.Add(wrap);
        }
    }

    private FrameworkElement Card(ConfigCode code)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = Localization.T(code.Title), FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        var white = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        white.Child = Barcode(code, 2.0, 64);
        panel.Children.Add(white);
        panel.Children.Add(new TextBlock
        {
            Text = (code.Fnc3 ? "FNC3 + " : string.Empty) + code.Data,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(0, 6, 0, 0),
        });
        panel.Children.Add(new TextBlock { Text = Localization.T(code.Hint), FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 4, 0, 0) });
        return new Border
        {
            Child = panel,
            Width = 400,
            Background = (Brush)FindResource("Surface"),
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 0, 12, 12),
        };
    }

    /// <summary>Le code dessiné en vectoriel : modules nets à l'écran comme à l'impression.</summary>
    private static Image Barcode(ConfigCode code, double module, double height)
    {
        var widths = Code128.Widths(code.Data, code.Fnc3);
        var quiet = 10;
        var total = widths.Sum() + quiet * 2;
        var bars = new GeometryGroup();
        double x = quiet;
        for (var i = 0; i < widths.Count; i++)
        {
            if (i % 2 == 0)
            {
                bars.Children.Add(new RectangleGeometry(new Rect(x, 0, widths[i], 1)));
            }

            x += widths[i];
        }

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, total, 1))));
        drawing.Children.Add(new GeometryDrawing(Brushes.Black, null, bars));
        var image = new Image { Source = new DrawingImage(drawing), Width = total * module, Height = height, Stretch = Stretch.Fill, SnapsToDevicePixels = true };
        RenderOptions.SetEdgeMode(image, EdgeMode.Aliased);
        return image;
    }

    /// <summary>Imprime les codes de la marque affichée (une feuille à garder près du poste).</summary>
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var brand = BrandChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "Honeywell";
        if (brand == "Zebra")
        {
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var target = brand == "Honeywell" ? ScannerBrand.Honeywell : ScannerBrand.Datalogic;
        var doc = new FlowDocument { PagePadding = new Thickness(48), ColumnWidth = double.PositiveInfinity, FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };
        doc.Blocks.Add(new Paragraph(new Run(Localization.T("Codes de configuration") + " · " + brand)) { FontSize = 20, FontWeight = FontWeights.Bold });
        foreach (var code in ScannerConfigCodes.All.Where(c => c.Brand == target))
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            block.Children.Add(new TextBlock { Text = Localization.T(code.Title), FontWeight = FontWeights.SemiBold, FontSize = 13 });
            block.Children.Add(Barcode(code, 1.6, 48));
            block.Children.Add(new TextBlock { Text = (code.Fnc3 ? "FNC3 + " : string.Empty) + code.Data, FontFamily = new FontFamily("Consolas"), FontSize = 10 });
            doc.Blocks.Add(new BlockUIContainer(block) { BreakPageBefore = false, Margin = new Thickness(0, 0, 0, 14) });
        }

        dialog.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, "Scanalyse - codes de configuration");
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2.5 0.5h7v7M0.5 2.5h7v7h-7z" : "M0.5 0.5h9v9h-9z");
    }
}
