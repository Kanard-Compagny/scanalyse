using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Scanalyse.Core.Printing;
using Scanalyse.Core.Text;
using Scanalyse.Desktop.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Onglet « Code imprimante » : programme ZPL / TSPL / EPL2 d'une étiquette, aperçu, envoi à l'imprimante.
/// Alimenté depuis l'analyse d'image, le banc d'essai et « Créer un code ».
/// </summary>
public partial class PrintView : UserControl
{
    /// <summary>Échelle de l'aperçu : unités WPF par millimètre.</summary>
    private const double Scale = 4;

    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _loaded;
    private bool _updating;
    private bool _edited;
    private int _renderSeq;
    private double? _codeWidthMm;
    private double? _codeHeightMm;
    private string _renderedKey = string.Empty;
    private PrinterOutput? _output;

    public PrintView()
    {
        InitializeComponent();
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await RefreshAsync();
            SavePreferences();
        };

        var p = AppSettings.Load().Print;
        Check(LangChips, p.Language);
        Check(DpiChips, p.Dpi.ToString(CultureInfo.InvariantCulture));
        WidthBox.Text = p.WidthMm.ToString("0.#", CultureInfo.CurrentCulture);
        HeightBox.Text = p.HeightMm.ToString("0.#", CultureInfo.CurrentCulture);
        AddressBox.Text = p.Address;
        AddressBox.TextChanged += (_, _) => AddressHint.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddressHint.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _savedQueue = p.Queue;
        if (p.Mode == "queue")
        {
            ModeQueue.IsChecked = true;
        }

        DataBox.Text = "SCANALYSE-0001";
        _loaded = true;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ = RefreshAsync();
            }
        };
    }

    private string _savedQueue = string.Empty;

    /// <summary>Dessine un code (texte, type de l'Atelier) : fourni par la fenêtre principale.</summary>
    public Func<string, string, Task<(byte[]? Png, string? Error)>>? Renderer { get; set; }

    /// <summary>Message pour le journal des lectures.</summary>
    public event Action<LogKind, string>? Log;

    /// <summary>Reprend un code venu d'un autre onglet.</summary>
    /// <param name="text">Contenu brut (caractères GS réels) ou forme « (01)… ».</param>
    /// <param name="symbology">Symbologie à imprimer.</param>
    /// <param name="source">Origine, affichée au-dessus du champ (« Image analysée · code 2 »…).</param>
    public void Load(string text, LabelSymbology symbology, string source)
    {
        _updating = true;
        DataBox.Text = ControlCharacters.Visualize(text.TrimEnd('\r', '\n'));
        Check(SymChips, symbology.ToString());
        if (Is2D(symbology) && Int(ModuleBox, 3) < 3)
        {
            ModuleBox.Text = "4";
        }

        _updating = false;
        SourceText.Text = "Repris de : " + source;
        SourceText.Visibility = Visibility.Visible;
        _edited = false;
        _codeWidthMm = _codeHeightMm = null;
        _ = RefreshAsync();
    }

    /// <summary>
    /// Symbologie d'impression à partir d'un type de l'Atelier (« gs1datamatrix »…) ou d'un format lu (« EAN-13 »…).
    /// </summary>
    public static LabelSymbology SymbologyFor(string typeOrFormat, string text, bool gs1 = false) => typeOrFormat switch
    {
        "gs1datamatrix" => LabelSymbology.Gs1DataMatrix,
        "datamatrix" => LabelSymbology.DataMatrix,
        "gs1128" => LabelSymbology.Gs1_128,
        "code128" => LabelSymbology.Code128,
        "ean13" => LabelSymbology.Ean13,
        "ean8" => LabelSymbology.Ean8,
        "upca" => LabelSymbology.UpcA,
        "code39" => LabelSymbology.Code39,
        "itf" or "itf14" => LabelSymbology.Itf,
        "pdf417" => LabelSymbology.Pdf417,
        "aztec" => LabelSymbology.Aztec,
        "qr" or "qrplain" or "gs1qr" => LabelSymbology.QrCode,
        _ when gs1 && typeOrFormat.Contains("128", StringComparison.Ordinal) => LabelSymbology.Gs1_128,
        _ when gs1 && typeOrFormat.Replace(" ", string.Empty, StringComparison.Ordinal).Contains("DataMatrix", StringComparison.OrdinalIgnoreCase) => LabelSymbology.Gs1DataMatrix,
        _ => LabelCode.FromFormat(typeOrFormat, text),
    };

    private static string AtelierType(LabelSymbology s) => s switch
    {
        LabelSymbology.Code128 => "code128",
        LabelSymbology.Gs1_128 => "gs1128",
        LabelSymbology.Ean13 => "ean13",
        LabelSymbology.Ean8 => "ean8",
        LabelSymbology.UpcA => "upca",
        LabelSymbology.Code39 => "code39",
        LabelSymbology.Itf => "itf",
        LabelSymbology.QrCode => "qrplain",
        LabelSymbology.DataMatrix => "datamatrix",
        LabelSymbology.Gs1DataMatrix => "gs1datamatrix",
        LabelSymbology.Pdf417 => "pdf417",
        _ => "aztec",
    };

    private static bool Is2D(LabelSymbology s) => s is LabelSymbology.QrCode or LabelSymbology.DataMatrix or LabelSymbology.Gs1DataMatrix
        or LabelSymbology.Pdf417 or LabelSymbology.Aztec;

    // ------------------------------------------------------------------ Réglages

    private static void Check(Panel chips, string tag)
    {
        foreach (var r in chips.Children.OfType<RadioButton>())
        {
            if (string.Equals(r.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                r.IsChecked = true;
            }
        }
    }

    private static string Selected(Panel chips, string fallback) =>
        chips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? fallback;

    private static double Num(TextBox box, double fallback, double min, double max)
    {
        var t = box.Text.Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= min && v <= max ? v : fallback;
    }

    private static int Int(TextBox box, int fallback) => int.TryParse(box.Text.Trim(), out var v) && v > 0 ? v : fallback;

    private PrinterLanguage Language => Enum.TryParse<PrinterLanguage>(Selected(LangChips, "Zpl"), out var l) ? l : PrinterLanguage.Zpl;

    private LabelSymbology Symbology => Enum.TryParse<LabelSymbology>(Selected(SymChips, "Code128"), out var s) ? s : LabelSymbology.Code128;

    private int Dpi => int.TryParse(Selected(DpiChips, "203"), out var d) ? d : 203;

    private LabelJob Job()
    {
        var sym = Symbology;
        var y = Num(YBox, 5, 0, 1000);
        double? captionY = null;
        if (!string.IsNullOrWhiteSpace(CaptionBox.Text) && _codeHeightMm is { } h)
        {
            captionY = y + h + 2;
        }

        return new LabelJob(
            DataBox.Text,
            sym,
            WidthMm: Num(WidthBox, 100, 5, 1000),
            HeightMm: Num(HeightBox, 50, 5, 2000),
            Dpi: Dpi,
            XMm: Num(XBox, 5, 0, 1000),
            YMm: y,
            ModuleDots: Math.Clamp(Int(ModuleBox, 3), 1, 10),
            HeightMmBars: Num(BarsBox, 15, 1, 500),
            HumanReadable: HriCheck.IsChecked == true,
            Rotation: int.TryParse(Selected(RotChips, "0"), out var r) ? r : 0,
            Copies: Math.Clamp(Int(CopiesBox, 1), 1, 9999),
            Caption: string.IsNullOrWhiteSpace(CaptionBox.Text) ? null : CaptionBox.Text.Trim(),
            CaptionYMm: captionY);
    }

    private void SavePreferences()
    {
        var settings = AppSettings.Load();
        var p = settings.Print;
        p.Language = Language.ToString();
        p.Dpi = Dpi;
        p.WidthMm = Num(WidthBox, 100, 5, 1000);
        p.HeightMm = Num(HeightBox, 50, 5, 2000);
        p.Mode = ModeQueue.IsChecked == true ? "queue" : "tcp";
        p.Address = AddressBox.Text.Trim();
        p.Queue = SelectedQueue ?? _savedQueue;
        settings.Save();
    }

    // ------------------------------------------------------------------ Génération et aperçu

    private void Any_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded || _updating)
        {
            return;
        }

        _edited = false;
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RefreshAsync()
    {
        if (!_loaded)
        {
            return;
        }

        var sym = Symbology;
        var is2D = Is2D(sym);
        BarsLabel.Text = sym == LabelSymbology.Pdf417 ? "Hauteur (indicative)" : "Hauteur des barres";
        BarsLabel.Opacity = BarsPanel.Opacity = is2D && sym != LabelSymbology.Pdf417 ? 0.4 : 1;
        HriCheck.IsEnabled = !is2D;
        var moduleMm = Int(ModuleBox, 3) * 25.4 / Dpi;
        ModuleMm.Text = $"points ({moduleMm.ToString("0.###", CultureInfo.CurrentCulture)} mm)";

        await RenderPreviewAsync();
        Generate();
    }

    private void Generate()
    {
        var job = Job();
        _output = LabelCode.Generate(Language, job);
        var warnings = _output.Warnings.ToList();
        if (_codeWidthMm is { } w && _codeHeightMm is { } h)
        {
            var (rw, rh) = job.Rotation is 90 or 270 ? (h, w) : (w, h);
            if (job.XMm + rw > job.WidthMm + 0.5 || job.YMm + rh > job.HeightMm + 0.5)
            {
                warnings.Add($"Le code (≈ {Mm(rw)} × {Mm(rh)} mm) dépasse de l'étiquette : réduis le module, la hauteur ou déplace-le.");
            }
        }

        var moduleMm = job.ModuleDots * 25.4 / job.Dpi;
        if (job.Symbology is LabelSymbology.Ean13 or LabelSymbology.Ean8 or LabelSymbology.UpcA && moduleMm < 0.264)
        {
            warnings.Add($"Module de {Mm(moduleMm)} mm : GS1 demande au moins 0,264 mm en point de vente (0,33 mm recommandé).");
        }
        else if (job.Symbology is LabelSymbology.Gs1_128 or LabelSymbology.Itf && moduleMm < 0.495)
        {
            warnings.Add($"Module de {Mm(moduleMm)} mm : pour la logistique, GS1 demande au moins 0,495 mm.");
        }

        WarningsList.ItemsSource = warnings;
        if (!_edited)
        {
            _updating = true;
            CodeBox.Text = _output.Code;
            _updating = false;
            EditedNote.Visibility = Visibility.Collapsed;
        }
    }

    private static string Mm(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    private async Task RenderPreviewAsync()
    {
        var job = Job();
        LabelCanvas.Width = job.WidthMm * Scale;
        LabelCanvas.Height = job.HeightMm * Scale;
        PreviewInfo.Text = $"{Mm(job.WidthMm)} × {Mm(job.HeightMm)} mm · {job.Dpi} dpi";

        var raw = ControlCharacters.FromVisualized(job.Data);
        var key = $"{AtelierType(job.Symbology)}|{raw}";
        if (Renderer is not null && raw.Length > 0 && key != _renderedKey)
        {
            var seq = ++_renderSeq;
            var (png, error) = await Renderer(raw, AtelierType(job.Symbology));
            if (seq != _renderSeq)
            {
                return;
            }

            _renderedKey = png is null ? string.Empty : key;
            if (png is null)
            {
                CodeImage.Source = null;
                _codeWidthMm = _codeHeightMm = null;
                PreviewNote.Text = "Aperçu impossible : " + (error ?? "contenu refusé pour ce type de code.");
            }
            else
            {
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = new MemoryStream(png);
                img.EndInit();
                img.Freeze();
                CodeImage.Source = img;
                PreviewNote.Text = "Aperçu indicatif, dessiné par Scanalyse : le rendu final dépend du firmware de l'imprimante.";
            }
        }

        PlaceCode(Job());
    }

    /// <summary>Place et dimensionne le code sur l'étiquette, comme l'imprimante (origine du champ en haut à gauche).</summary>
    private void PlaceCode(LabelJob job)
    {
        if (CodeImage.Source is not BitmapSource bmp)
        {
            CaptionPreview.Text = string.Empty;
            return;
        }

        // L'Atelier dessine 4 pixels par module, zones de silence comprises.
        var moduleMm = job.ModuleDots * 25.4 / job.Dpi;
        var w = bmp.PixelWidth / 4.0 * moduleMm;
        var h = Is2D(job.Symbology) && job.Symbology != LabelSymbology.Pdf417
            ? bmp.PixelHeight / 4.0 * moduleMm
            : job.Symbology == LabelSymbology.Pdf417 ? bmp.PixelHeight / 4.0 * moduleMm * 3
            : job.HeightMmBars + (job.HumanReadable ? 3.5 : 0);
        var heightChanged = _codeHeightMm is not { } old || Math.Abs(old - h) > 0.05;
        _codeWidthMm = w;
        _codeHeightMm = h;

        CodeImage.Width = w * Scale;
        CodeImage.Height = h * Scale;
        var rot = job.Symbology == LabelSymbology.QrCode && Language == PrinterLanguage.Zpl ? 0 : job.Rotation;
        var t = new TransformGroup();
        t.Children.Add(new RotateTransform(rot));
        t.Children.Add(rot switch
        {
            90 => new TranslateTransform(h * Scale, 0),
            180 => new TranslateTransform(w * Scale, h * Scale),
            270 => new TranslateTransform(0, w * Scale),
            _ => new TranslateTransform(0, 0),
        });
        CodeImage.RenderTransform = t;
        Canvas.SetLeft(CodeImage, job.XMm * Scale);
        Canvas.SetTop(CodeImage, job.YMm * Scale);

        CaptionPreview.Text = job.Caption ?? string.Empty;
        CaptionPreview.FontSize = 3.5 * Scale;
        Canvas.SetLeft(CaptionPreview, job.XMm * Scale);
        var rh = rot is 90 or 270 ? w : h;
        Canvas.SetTop(CaptionPreview, (job.YMm + rh + 2) * Scale);
        if (heightChanged && job.Caption is not null && !_edited)
        {
            // La position du texte dépend de la taille réelle du code : on régénère avec la bonne valeur.
            Dispatcher.BeginInvoke(new Action(Generate));
        }
    }

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || !_loaded)
        {
            return;
        }

        _edited = _output is null || CodeBox.Text != _output.Code;
        EditedNote.Visibility = _edited ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Regenerate_Click(object sender, RoutedEventArgs e)
    {
        _edited = false;
        Generate();
    }

    // ------------------------------------------------------------------ Sortie

    private byte[] Bytes() =>
        (Language == PrinterLanguage.Epl2 ? Encoding.Latin1 : new UTF8Encoding(false)).GetBytes(CodeBox.Text);

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (CodeBox.Text.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(CodeBox.Text);
            Status("Programme copié dans le presse-papiers.", ok: true);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Status("Le presse-papiers est occupé par une autre application : réessaie.", ok: false);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (CodeBox.Text.Length == 0)
        {
            return;
        }

        var ext = LabelCode.Extension(Language);
        var name = new string(ControlCharacters.FromVisualized(DataBox.Text).Where(char.IsAsciiLetterOrDigit).Take(30).ToArray());
        var dialog = new SaveFileDialog
        {
            Title = "Enregistrer le programme d'étiquette",
            FileName = $"etiquette-{(name.Length > 0 ? name : "code")}{ext}",
            Filter = $"Programme {Language.ToString().ToUpperInvariant()} (*{ext})|*{ext}|Fichier d'impression (*.prn)|*.prn|Tous les fichiers (*.*)|*.*",
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, Bytes());
            Status("Enregistré : " + dialog.FileName, ok: true);
            Log?.Invoke(LogKind.Ok, "Programme enregistré : " + dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status("Enregistrement impossible : " + ex.Message, ok: false);
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (CodeBox.Text.Trim().Length == 0)
        {
            Status("Rien à envoyer : la combinaison langage / symbologie n'est pas prise en charge.", ok: false);
            return;
        }

        var data = Bytes();
        var lang = Language.ToString().ToUpperInvariant();
        try
        {
            if (ModeQueue.IsChecked == true)
            {
                var queue = SelectedQueue;
                if (queue is null)
                {
                    Status("Choisis d'abord une imprimante dans la liste.", ok: false);
                    return;
                }

                await Task.Run(() => PrinterTransport.SendToQueue(queue, data, "Scanalyse - étiquette"));
                Status($"Envoyé à « {queue} » ({data.Length} octets, {lang}).", ok: true);
                Log?.Invoke(LogKind.Ok, $"Étiquette {lang} envoyée à « {queue} ».");
            }
            else
            {
                var address = AddressBox.Text.Trim();
                if (address.Length == 0)
                {
                    Status("Indique l'adresse IP de l'imprimante (menu réseau de l'imprimante ou étiquette de configuration).", ok: false);
                    AddressBox.Focus();
                    return;
                }

                Status($"Envoi à {address}…", ok: true);
                await PrinterTransport.SendTcpAsync(address, data);
                var (host, port) = PrinterTransport.ParseTarget(address);
                Status($"Envoyé à {host}:{port} ({data.Length} octets, {lang}). Si rien ne sort, vérifie que l'imprimante est bien réglée en {lang}.", ok: true);
                Log?.Invoke(LogKind.Ok, $"Étiquette {lang} envoyée à {host}:{port}.");
            }

            SavePreferences();
        }
        catch (IOException ex)
        {
            Status(ex.Message, ok: false);
            Log?.Invoke(LogKind.Error, ex.Message);
        }
    }

    private void Status(string text, bool ok)
    {
        SendStatus.Text = text;
        SendStatus.Foreground = (Brush)FindResource(ok ? "OkFg" : "BadFg");
        SendStatus.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------ Choix de l'imprimante

    private string? SelectedQueue => QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (QueuePanel is null || AddressPanel is null)
        {
            return;
        }

        var queue = ModeQueue.IsChecked == true;
        QueuePanel.Visibility = queue ? Visibility.Visible : Visibility.Collapsed;
        AddressPanel.Visibility = queue ? Visibility.Collapsed : Visibility.Visible;
        if (queue)
        {
            LoadQueues();
        }
    }

    private void RefreshQueues_Click(object sender, RoutedEventArgs e) => LoadQueues();

    // ------------------------------------------------------------------ Programme agrandi

    private bool _expanded;

    private void Expand_Click(object sender, RoutedEventArgs e) => SetExpanded(!_expanded);

    /// <summary>Programme sur toute la page : réglages et aperçu masqués, la zone de code prend toute la place.</summary>
    private void SetExpanded(bool on)
    {
        _expanded = on;
        SettingsCard.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        PreviewCard.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetColumn(RightPane, on ? 0 : 2);
        Grid.SetColumnSpan(RightPane, on ? 3 : 1);
        ExpandButton.Content = on ? "Réduire" : "Agrandir";
        CodeBox.FontSize = on ? 14 : 13;
        if (on)
        {
            CodeBox.Focus();
        }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && _expanded)
        {
            SetExpanded(false);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void LoadQueues()
    {
        var current = SelectedQueue ?? _savedQueue;
        QueueChips.Children.Clear();
        foreach (var q in PrinterTransport.Queues())
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "Queue",
                Content = q,
                Tag = q,
                IsChecked = string.Equals(q, current, StringComparison.OrdinalIgnoreCase),
            };
            chip.Checked += (_, _) => _savedQueue = q;
            QueueChips.Children.Add(chip);
        }

        var n = QueueChips.Children.Count;
        QueueCount.Text = n switch
        {
            0 => "Aucune imprimante trouvée",
            1 => "1 imprimante installée",
            _ => $"{n} imprimantes installées" + (n > 6 ? " (fais défiler la liste)" : string.Empty),
        };
        QueueScroll.ScrollToTop();
        var selected = QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true);
        if (selected is not null)
        {
            Dispatcher.BeginInvoke(new Action(() => selected.BringIntoView()), DispatcherPriority.Loaded);
        }
        if (n == 0)
        {
            QueueHint.Text = "Aucune imprimante installée sur ce PC. Utilise l'envoi réseau, ou installe le pilote de l'imprimante d'étiquettes.";
        }
    }
}
