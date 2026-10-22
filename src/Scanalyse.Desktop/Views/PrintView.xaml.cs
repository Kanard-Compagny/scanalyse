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
        Check(TemplateLangChips, p.Language);
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
        // Numéros de ligne et texte coloré : ils suivent le défilement du programme.
        CodeBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => AlignOverlay()));
        CodeBox.SizeChanged += (_, _) => AlignOverlay();
        CodeBox.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
        _colorTimer.Tick += (_, _) => Recolor();
        ScrollChaining.Enable(PageScroll);
        HintAligner.Attach(AddressBox, AddressHint);
        HintAligner.Attach(QueueSearch, QueueSearchHint);
        HintAligner.Attach(CodeBox, CodeHint);
        BuildLegend();
        _readTimer.Tick += async (_, _) =>
        {
            _readTimer.Stop();
            await ReadProgramAsync();
        };
        _loaded = true;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && !_readMode)
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
        if (!_loaded || _readMode)
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
        ProgramTitle.Text = Language switch { PrinterLanguage.Tspl => "Programme TSPL", PrinterLanguage.Epl2 => "Programme EPL2", PrinterLanguage.Dpl => "Programme DPL", _ => "Programme ZPL" };
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
        UpdateEditorChrome();
        if (_updating || !_loaded)
        {
            return;
        }

        if (_readMode)
        {
            _readTimer.Stop();
            _readTimer.Start();
            return;
        }

        // Un programme ZPL collé (et non une retouche du programme généré) : on passe en lecture.
        var pasted = e.Changes.Sum(c => c.AddedLength);
        if (pasted > 20 && ProgramFile.Detect(CodeBox.Text) != ProgramKind.Unknown && (_output is null || !CodeBox.Text.Contains(_output.Code.Trim(), StringComparison.Ordinal)))
        {
            var program = CodeBox.Text;
            _updating = true;
            CodeBox.Text = _output?.Code ?? string.Empty;
            _updating = false;
            _edited = false;
            LoadProgram(program);
            Log?.Invoke(LogKind.Info, $"Programme {ProgramFile.Name(ProgramFile.Detect(program))} collé : affiché en lecture (« Lire un programme »).");
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

    /// <summary>
    /// Octets envoyés : UTF-8 pour un programme ^CI28 (ou TSPL), sinon octet par octet (Latin-1), comme le fichier d'origine d'un client.
    /// </summary>
    private byte[] Bytes()
    {
        var text = CodeBox.Text;
        var utf8 = _readMode
            ? text.Contains("^CI28", StringComparison.OrdinalIgnoreCase) || text.Contains("CODEPAGE UTF-8", StringComparison.OrdinalIgnoreCase)
            : Language == PrinterLanguage.Tspl || (Language == PrinterLanguage.Zpl && text.Contains("^CI28", StringComparison.OrdinalIgnoreCase));
        return (utf8 ? new UTF8Encoding(false) : Encoding.Latin1).GetBytes(text);
    }

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

        var ext = _readMode
            ? ProgramFile.Detect(CodeBox.Text) switch { ProgramKind.Zpl => ".zpl", ProgramKind.Epl2 => ".epl", ProgramKind.Unknown => ".txt", _ => ".prn" }
            : LabelCode.Extension(Language);
        var name = new string(ControlCharacters.FromVisualized(DataBox.Text).Where(char.IsAsciiLetterOrDigit).Take(30).ToArray());
        var dialog = new SaveFileDialog
        {
            Title = Localization.T("Enregistrer le programme d'étiquette"),
            InitialDirectory = AppSettings.Load().LabelsFolderForSave(),
            FileName = $"etiquette-{(name.Length > 0 ? name : "code")}{ext}",
            Filter = Localization.T($"Programme {Language.ToString().ToUpperInvariant()} (*{ext})|*{ext}|Fichier d'impression (*.prn)|*.prn|Tous les fichiers (*.*)|*.*"),
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
        var lang = _readMode ? "ZPL" : Language.ToString().ToUpperInvariant();
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

    private string? SelectedQueue => QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? NullIfEmpty(_savedQueue);

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

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

    private void QueueButton_Click(object sender, RoutedEventArgs e)
    {
        if (QueueChips.Children.Count == 0)
        {
            LoadQueues();
        }

        QueueSearch.Text = string.Empty;
        QueuePopup.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            QueueSearch.Focus();
            QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.BringIntoView();
        }), DispatcherPriority.Loaded);
    }

    private void QueueSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var q = QueueSearch.Text.Trim();
        QueueSearchHint.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var chip in QueueChips.Children.OfType<RadioButton>())
        {
            chip.Visibility = q.Length == 0 || ((string)chip.Tag).Contains(q, StringComparison.CurrentCultureIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void LoadQueues()
    {
        var current = SelectedQueue;
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
            chip.Checked += (_, _) =>
            {
                _savedQueue = q;
                QueueSelected.Text = q;
                QueuePopup.IsOpen = false;
                SavePreferences();
            };
            QueueChips.Children.Add(chip);
        }

        var n = QueueChips.Children.Count;
        QueueCount.Text = n switch
        {
            0 => "aucune imprimante installée",
            1 => "1 imprimante",
            _ => $"{n} imprimantes",
        };
        QueueSelected.Text = QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "Choisir une imprimante…";
        QueueScroll.ScrollToTop();
        if (n == 0)
        {
            QueueHint.Text = "Aucune imprimante installée sur ce PC. Utilise l'envoi réseau, ou installe le pilote de l'imprimante d'étiquettes.";
        }
    }

    // ------------------------------------------------------------------ Agrandir le programme ou l'aperçu

    private enum Zoom
    {
        None,
        Program,
        Preview,
    }

    private Zoom _zoom;

    private void Expand_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom == Zoom.Program ? Zoom.None : Zoom.Program);

    private void ExpandPreview_Click(object sender, RoutedEventArgs e) => SetZoom(_zoom == Zoom.Preview ? Zoom.None : Zoom.Preview);

    /// <summary>Programme ou aperçu sur toute la page ; les autres cartes sont masquées le temps de la lecture.</summary>
    private void SetZoom(Zoom zoom)
    {
        _zoom = zoom;
        ApplyLayout();
        if (zoom == Zoom.Program)
        {
            CodeBox.Focus();
        }
    }

    /// <summary>Fenêtre étroite : les choix de l'en-tête passent sous le titre au lieu de l'écraser.</summary>
    private void PageGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 1180;
        Grid.SetRow(HeaderChips, narrow ? 1 : 0);
        Grid.SetColumn(HeaderChips, narrow ? 0 : 1);
        Grid.SetColumnSpan(HeaderChips, narrow ? 2 : 1);
        HeaderChips.Margin = narrow ? new Thickness(0, 12, 0, 0) : new Thickness(16, 0, 0, 0);
        HeaderChips.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    // ------------------------------------------------------------------ Affichage : aperçu, programme, ou les deux

    private enum PaneView
    {
        Both,
        Preview,
        Program,
    }

    private PaneView _view = PaneView.Both;
    private Window? _programWindow;

    private bool Detached => _programWindow is not null;

    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _view = ViewPreview.IsChecked == true ? PaneView.Preview : ViewProgram.IsChecked == true ? PaneView.Program : PaneView.Both;
        if (_zoom != Zoom.None)
        {
            _zoom = Zoom.None;
        }

        ApplyLayout();
    }

    /// <summary>Place les cartes selon le mode, l'agrandissement, le choix « Afficher » et le programme détaché.</summary>
    private void ApplyLayout()
    {
        var zoom = _zoom;
        var any = zoom != Zoom.None;
        SettingsCard.Visibility = !any && !_readMode ? Visibility.Visible : Visibility.Collapsed;
        ReadCard.Visibility = !any && _readMode && !_templateMode ? Visibility.Visible : Visibility.Collapsed;
        TemplateCard.Visibility = !any && _templateMode ? Visibility.Visible : Visibility.Collapsed;

        var showPreview = zoom == Zoom.Preview || (zoom == Zoom.None && (Detached || _view != PaneView.Program));
        var showProgram = !Detached && (zoom == Zoom.Program || (zoom == Zoom.None && _view != PaneView.Preview));
        PreviewCard.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
        if (!Detached)
        {
            ProgramCard.Visibility = showProgram ? Visibility.Visible : Visibility.Collapsed;
        }

        var previewFills = showPreview && !showProgram;
        PreviewCard.Margin = new Thickness(0, 0, 0, showPreview && showProgram ? 14 : 0);
        Grid.SetColumn(RightPane, any ? 0 : 2);
        Grid.SetColumnSpan(RightPane, any ? 3 : 1);
        RightPane.RowDefinitions[0].Height = previewFills ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        RightPane.RowDefinitions[1].Height = showProgram ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        PreviewBox.Height = previewFills ? double.NaN : _readMode ? 330 : 170;
        ExpandButton.Content = zoom == Zoom.Program ? "Réduire" : "Agrandir";
        ExpandPreviewButton.Content = zoom == Zoom.Preview ? "Réduire" : "Agrandir";
        ExpandButton.Visibility = Detached ? Visibility.Collapsed : Visibility.Visible;
        ViewChips.IsEnabled = !Detached;
        ViewChips.Opacity = Detached ? 0.45 : 1;
        ViewChips.ToolTip = Detached ? "Le programme est dans sa propre fenêtre : l'aperçu prend toute la place ici." : null;
        CodeBox.FontSize = CodeColors.FontSize = LineNumbers.FontSize = zoom == Zoom.Program || Detached ? 14 : 13;
        Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
        if (_zplZoom is null && ZplImage.Source is not null)
        {
            Dispatcher.BeginInvoke(new Action(ApplyZplTransform), DispatcherPriority.Loaded);
        }
    }

    /// <summary>
    /// Sort le programme dans sa propre fenêtre (deuxième écran) : on voit l'étiquette et le code en grand en même temps.
    /// Fermer la fenêtre le remet à sa place.
    /// </summary>
    private void Detach_Click(object sender, RoutedEventArgs e)
    {
        if (_programWindow is { } open)
        {
            open.Close();
            return;
        }

        var owner = Window.GetWindow(this);
        RightPane.Children.Remove(ProgramCard);
        ProgramCard.Visibility = Visibility.Visible;
        var w = new ProgramWindow
        {
            Heading = ProgramTitle.Text,
            Owner = owner,
            Width = Math.Max(720, (owner?.ActualWidth ?? 1400) * 0.5),
            Height = Math.Max(600, (owner?.ActualHeight ?? 900) * 0.85),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        w.Host.Child = ProgramCard;
        w.Closed += (_, _) =>
        {
            w.Host.Child = null;
            RightPane.Children.Add(ProgramCard);
            Grid.SetRow(ProgramCard, 1);
            _programWindow = null;
            DetachButton.Content = "Détacher";
            DetachButton.ToolTip = DetachTip;
            ApplyLayout();
            owner?.Activate();
        };
        _programWindow = w;
        DetachButton.Content = "Rattacher";
        DetachButton.ToolTip = "Remet le programme dans la page (ou ferme simplement cette fenêtre)";
        _zoom = Zoom.None;
        ApplyLayout();
        w.Show();
        CodeBox.Focus();
    }

    private const string DetachTip = "Ouvre le programme dans une fenêtre à part (idéal sur un deuxième écran) : l'aperçu prend toute la place ici";

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Escape && _zoom != Zoom.None)
        {
            SetZoom(Zoom.None);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    // ------------------------------------------------------------------ Lecture d'un programme ZPL

    private readonly DispatcherTimer _readTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private bool _readMode;
    private ZplLabel? _label;
    private byte[]? _labelPng;
    private int _readSeq;
    private string _createProgram = string.Empty;
    private string _readProgram = string.Empty;

    /// <summary>Dessine une étiquette ZPL (description JSON) : fourni par la fenêtre principale.</summary>
    public Func<string, Task<LabelRender>>? LabelRenderer { get; set; }

    /// <summary>« Analyse complète de l'étiquette » : l'aperçu part dans « Analyser une image ».</summary>
    public event Action<byte[], string>? AnalyzeRequested;

    private int ReadDpi => int.TryParse(Selected(ReadDpiChips, "203"), out var d) ? d : 203;

    private void PageMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        SetPageMode(ModeRead.IsChecked == true ? PageMode.Read : ModeTemplate.IsChecked == true ? PageMode.Template : PageMode.Create);
    }

    /// <summary>Numéros de ligne et invitation à coller quand la zone est vide.</summary>
    private void UpdateEditorChrome()
    {
        var count = Math.Max(1, CodeBox.Text.Count(c => c == '\n') + 1);
        LineNumbers.Text = string.Join("\n", Enumerable.Range(1, count));
        CodeHint.Visibility = CodeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _colorTimer.Stop();
        _colorTimer.Start();
    }

    // ------------------------------------------------------------------ Coloration du programme

    private readonly DispatcherTimer _colorTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };

    private static string ColorKey(SyntaxKind k) => k switch
    {
        SyntaxKind.Structure => "SynStruct",
        SyntaxKind.Position => "SynPos",
        SyntaxKind.Text => "SynText",
        SyntaxKind.Barcode => "SynCode",
        SyntaxKind.Graphic => "SynGfx",
        SyntaxKind.Setting => "SynSet",
        SyntaxKind.Parameter => "SynParam",
        SyntaxKind.Comment => "SynComment",
        _ => "SynData",
    };

    /// <summary>Redessine le texte coloré sous la zone de saisie (couleurs suivant le thème).</summary>
    private void Recolor()
    {
        _colorTimer.Stop();
        var text = CodeBox.Text;
        CodeColors.Inlines.Clear();
        if (text.Length > 300_000)
        {
            // Programme énorme (images ^GF) : pas de coloration, texte affiché normalement.
            CodeBox.Foreground = Res("Ink");
            return;
        }

        CodeBox.Foreground = Brushes.Transparent;
        foreach (var t in ProgramSyntax.Tokenize(text))
        {
            var run = new System.Windows.Documents.Run(text.Substring(t.Start, t.Length));
            run.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, ColorKey(t.Kind));
            CodeColors.Inlines.Add(run);
        }

        Legend.Visibility = text.Contains('^', StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Cale le texte coloré et les numéros de ligne sur la position réelle du texte dans la zone de saisie
    /// (mesurée par la zone elle-même : marges internes, défilement et hauteur de ligne compris).
    /// </summary>
    private void AlignOverlay()
    {
        if (!CodeBox.IsLoaded || CodeBox.Template?.FindName("PART_ContentHost", CodeBox) is not ScrollViewer host)
        {
            return;
        }

        try
        {
            var origin = host.TranslatePoint(new Point(0, 0), CodeBox);
            // Largeur : zone de texte sans la barre de défilement. Hauteur : tout l'intérieur du champ (marges comprises),
            // pour que la première et la dernière ligne restent entières.
            var bottom = host.ComputedHorizontalScrollBarVisibility == Visibility.Visible
                ? origin.Y + host.ViewportHeight
                : CodeBox.ActualHeight - 1;
            CodeOverlay.Clip = new RectangleGeometry(new Rect(origin.X, 1, Math.Max(0, host.ViewportWidth), Math.Max(0, bottom - 1)));
            var first = CodeBox.GetRectFromCharacterIndex(0);
            if (first.IsEmpty || double.IsInfinity(first.Top))
            {
                return;
            }

            if (CodeBox.LineCount > 1)
            {
                var second = CodeBox.GetRectFromCharacterIndex(CodeBox.GetCharacterIndexFromLineIndex(1));
                var lineHeight = second.Top - first.Top;
                if (lineHeight > 4 && lineHeight < 80)
                {
                    CodeColors.LineStackingStrategy = LineNumbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    CodeColors.LineHeight = LineNumbers.LineHeight = lineHeight;
                }
            }

            Canvas.SetLeft(CodeColors, first.Left);
            Canvas.SetTop(CodeColors, first.Top);
            // La gouttière commence sous la bordure de la zone (1 point) : même référence verticale.
            Canvas.SetTop(LineNumbers, first.Top - 1);
        }
        catch (InvalidOperationException)
        {
            // Mise en page pas encore prête : on recalera au prochain passage.
        }
    }

    private void BuildLegend()
    {
        (string Key, string Label)[] items =
        [
            ("SynStruct", "Début, fin (^XA ^FS ^XZ)"),
            ("SynPos", "Position (^FO ^FT)"),
            ("SynText", "Police, texte (^A ^CF)"),
            ("SynCode", "Code-barres (^B…)"),
            ("SynGfx", "Graphique (^GB)"),
            ("SynParam", "Paramètres"),
            ("SynData", "Données (^FD)"),
            ("SynSet", "Réglages"),
        ];
        foreach (var (key, label) in items)
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
            sp.Children.Add(dot);
            sp.Children.Add(new TextBlock { Text = label, FontSize = 11.5, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center });
            Legend.Children.Add(sp);
        }
    }

    /// <summary>Sélectionne une ligne du programme et la fait défiler à l'écran.</summary>
    private void GoToLine(int line)
    {
        if (_zoom == Zoom.Preview)
        {
            SetZoom(Zoom.None);
        }

        var index = Math.Clamp(line - 1, 0, Math.Max(0, CodeBox.LineCount - 1));
        var start = CodeBox.GetCharacterIndexFromLineIndex(index);
        if (start < 0)
        {
            return;
        }

        CodeBox.Focus();
        CodeBox.Select(start, Math.Max(0, CodeBox.GetLineLength(index) - Environment.NewLine.Length));
        CodeBox.ScrollToLine(index);
    }

    private void PasteProgram_Click(object sender, RoutedEventArgs e)
    {
        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            text = string.Empty;
        }

        if (text.Trim().Length == 0)
        {
            Status("Le presse-papiers ne contient pas de texte : copie d'abord le programme du client (Ctrl + A puis Ctrl + C dans son fichier), ou utilise « Ouvrir un fichier ».", ok: false);
            return;
        }

        SendStatus.Visibility = Visibility.Collapsed;
        LoadProgram(text);
    }

    private enum PageMode
    {
        Create,
        Template,
        Read,
    }

    private PageMode _pageMode = PageMode.Create;
    private bool _templateMode;
    private string _templateProgram = string.Empty;

    private void SetReadMode(bool on) => SetPageMode(on ? PageMode.Read : PageMode.Create);

    /// <summary>
    /// Trois façons d'utiliser la page : créer une étiquette (un code), partir d'un modèle (étiquette complète), lire un programme.
    /// Chaque mode garde son propre programme : on le retrouve en revenant.
    /// </summary>
    private void SetPageMode(PageMode mode)
    {
        if (_pageMode == mode)
        {
            return;
        }

        switch (_pageMode)
        {
            case PageMode.Create:
                _createProgram = CodeBox.Text;
                break;
            case PageMode.Template:
                _templateProgram = CodeBox.Text;
                break;
            default:
                _readProgram = CodeBox.Text;
                break;
        }

        _pageMode = mode;
        var on = mode != PageMode.Create; // modèle et lecture : programme ZPL complet dessiné par l'Atelier
        _readMode = on;
        _templateMode = mode == PageMode.Template;
        _updating = true;
        ModeRead.IsChecked = mode == PageMode.Read;
        ModeTemplate.IsChecked = mode == PageMode.Template;
        ModeCreate.IsChecked = mode == PageMode.Create;
        CodeBox.Text = mode switch { PageMode.Create => _createProgram, PageMode.Template => _templateProgram, _ => _readProgram };
        _updating = false;

        PageSubtitle.Text = mode switch
        {
            PageMode.Read => "Colle ou ouvre le programme d'un client (ZPL, TSPL, EPL2, DPL…) : aperçu de l'étiquette, codes-barres et leur contenu, points à corriger, puis réimpression ou envoi.",
            PageMode.Template => "Étiquettes complètes prêtes à l'emploi : remplis les champs, les clés de contrôle et les codes GS1 sont calculés, le programme ZPL est prêt à envoyer.",
            _ => "Génère le programme ZPL, TSPL, EPL2 ou DPL d'une étiquette pour recréer le code d'un client sur son imprimante, et l'envoie directement.",
        };
        LangChips.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ZplTools.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ProgramTitle.Text = mode switch { PageMode.Read => "Programme du client", PageMode.Template => "Programme ZPL du modèle", _ => "Programme ZPL" };
        ProgramHint.Text = mode switch
        {
            PageMode.Read => "Modifie les lignes directement : l'aperçu et l'analyse se mettent à jour. « Voir la ligne » sur un code te place sur sa ligne.",
            PageMode.Template => "Généré depuis le modèle : modifie les champs à gauche, ou retouche les lignes ici (un changement de champ régénère le programme).",
            _ => "Généré depuis les réglages. Tu peux modifier les lignes directement, ou coller le programme d'un client pour le lire.",
        };
        PasteProgramButton.Content = mode == PageMode.Read ? "Coller un autre programme" : "Coller un programme client";
        PageGrid.MinHeight = on ? 910 : 850;
        RegenerateButton.Visibility = mode == PageMode.Create ? Visibility.Visible : Visibility.Collapsed;
        WarningsList.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        EditedNote.Visibility = Visibility.Collapsed;
        CreateViewbox.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        SetZoom(Zoom.None);
        if (mode == PageMode.Template)
        {
            if (TemplateChips.Children.Count == 0)
            {
                BuildTemplateChips();
            }
            else
            {
                RegenerateTemplate();
            }
        }
        else if (on)
        {
            _ = ReadProgramAsync();
        }
        else
        {
            ZplScroll.Visibility = ZplEmptyBox.Visibility = Visibility.Collapsed;
            PreviewNote.Text = "Aperçu indicatif, dessiné par Scanalyse : le rendu final dépend du firmware de l'imprimante.";
            _renderedKey = string.Empty;
            _ = RefreshAsync();
        }
    }

    // ------------------------------------------------------------------ Modèles d'étiquettes

    private LabelTemplate _template = LabelTemplates.All[0];
    private readonly Dictionary<string, Dictionary<string, string>> _templateValues = [];
    private readonly DispatcherTimer _templateTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };

    private void BuildTemplateChips()
    {
        _templateTimer.Tick += (_, _) =>
        {
            _templateTimer.Stop();
            RegenerateTemplate();
        };
        foreach (var t in LabelTemplates.All)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "Template",
                Content = t.Name,
                Tag = t.Id,
                ToolTip = $"{t.Description} ({t.WidthMm:0} × {t.HeightMm:0} mm)",
            };
            chip.Checked += (_, _) => SelectTemplate(t);
            TemplateChips.Children.Add(chip);
        }

        ((RadioButton)TemplateChips.Children[0]).IsChecked = true;
    }

    private void SelectTemplate(LabelTemplate t)
    {
        _template = t;
        if (!_templateValues.TryGetValue(t.Id, out var values))
        {
            values = LabelTemplates.Defaults(t);
            _templateValues[t.Id] = values;
        }

        TemplateDescription.Text = $"{t.Description} Étiquette {t.WidthMm:0} × {t.HeightMm:0} mm, valeurs d'exemple à remplacer.";
        TemplateFields.Children.Clear();
        foreach (var f in t.Fields)
        {
            TemplateFields.Children.Add(new TextBlock
            {
                Text = f.Label + (f.Required ? string.Empty : " (facultatif)"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 10, 0, 4),
            });
            var multi = f.Kind == TemplateFieldKind.MultiLine;
            var box = new TextBox
            {
                Style = (Style)FindResource("FieldInput"),
                Text = values.TryGetValue(f.Key, out var v) ? v : f.Default,
                AcceptsReturn = multi,
                TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap,
                MinHeight = multi ? 74 : 0,
                VerticalContentAlignment = multi ? VerticalAlignment.Top : VerticalAlignment.Center,
            };
            box.TextChanged += (_, _) =>
            {
                values[f.Key] = box.Text;
                _templateTimer.Stop();
                _templateTimer.Start();
            };
            TemplateFields.Children.Add(box);
            if (f.Hint.Length > 0)
            {
                TemplateFields.Children.Add(new TextBlock { Text = f.Hint, FontSize = 11.5, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) });
            }
        }

        _newProgram = true;
        RegenerateTemplate();
    }

    private void Template_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded && _templateMode)
        {
            _templateTimer.Stop();
            _templateTimer.Start();
        }
    }

    /// <summary>Produit le programme du modèle avec les valeurs saisies, puis le dessine comme un programme client.</summary>
    private void RegenerateTemplate()
    {
        if (!_templateMode)
        {
            return;
        }

        var dpi = int.TryParse(Selected(TemplateDpiChips, "203"), out var d) ? d : 203;
        var values = _templateValues.TryGetValue(_template.Id, out var v) ? v : LabelTemplates.Defaults(_template);
        var copies = Math.Clamp(Int(TemplateCopies, 1), 1, 9999);
        var output = LabelTemplates.Build(_template, values, dpi, copies);
        var language = Enum.TryParse<PrinterLanguage>(Selected(TemplateLangChips, "Zpl"), out var l) ? l : PrinterLanguage.Zpl;
        var converted = LabelConverter.Convert(output.Zpl, language, dpi, copies);
        _updating = true;
        Check(ReadDpiChips, dpi.ToString(CultureInfo.InvariantCulture));
        CodeBox.Text = converted.Program;
        ProgramTitle.Text = language switch { PrinterLanguage.Tspl => "Programme TSPL du modèle", PrinterLanguage.Epl2 => "Programme EPL2 du modèle", PrinterLanguage.Dpl => "Programme DPL du modèle", _ => "Programme ZPL du modèle" };
        _updating = false;
        UpdateEditorChrome();
        TemplateWarnings.Children.Clear();
        foreach (var w in output.Warnings)
        {
            TemplateWarnings.Children.Add(AlertBox(new Scanalyse.Core.Content.Alert(Scanalyse.Core.Content.Severity.Warning, w)));
        }

        foreach (var w in converted.Warnings)
        {
            TemplateWarnings.Children.Add(AlertBox(new Scanalyse.Core.Content.Alert(Scanalyse.Core.Content.Severity.Info, w)));
        }

        if (output.Warnings.Count == 0)
        {
            TemplateWarnings.Children.Add(AlertBox(new Scanalyse.Core.Content.Alert(Scanalyse.Core.Content.Severity.Ok,
                "Données vérifiées : clés de contrôle et codes GS1 conformes.")));
        }

        _ = ReadProgramAsync();
    }

    /// <summary>Ouvre un programme ZPL en lecture (collé, ou reçu d'un autre onglet).</summary>
    public void LoadProgram(string zpl)
    {
        _readProgram = zpl;
        _newProgram = true;
        _selected = -1;
        if (_pageMode == PageMode.Read)
        {
            _updating = true;
            CodeBox.Text = zpl;
            _updating = false;
            _ = ReadProgramAsync();
        }
        else
        {
            SetReadMode(true);
        }
    }

    private void ReadDpi_Changed(object sender, RoutedEventArgs e)
    {
        if (_loaded && _readMode)
        {
            // TSPL / DPL : tailles en millimètres ou pouces, converties selon la résolution : on relit.
            if (ProgramFile.Detect(CodeBox.Text) is ProgramKind.Tspl or ProgramKind.Dpl)
            {
                _ = ReadProgramAsync();
            }
            else
            {
                ShowLabelSize();
            }
        }
    }

    private async Task ReadProgramAsync()
    {
        var seq = ++_readSeq;
        var text = CodeBox.Text;
        ReadPanel.Children.Clear();
        var kind = ProgramFile.Detect(text);
        if (!_templateMode)
        {
            ProgramTitle.Text = kind is ProgramKind.Unknown ? "Programme du client" : $"Programme {ProgramFile.Name(kind)} du client";
        }
        if (kind != ProgramKind.Zpl || !ZplReader.LooksLikeZpl(text))
        {
            _label = null;
            _labelPng = null;
            _boxes = [];
            ZplScroll.Visibility = Visibility.Collapsed;
            ZplEmptyBox.Visibility = Visibility.Visible;
            PreviewInfo.Text = string.Empty;
            if (text.Trim().Length == 0)
            {
                ZplEmpty.Text = "Colle le programme du client dans la zone « Programme », ou ouvre son fichier (.zpl, .prn, .txt…) : l'étiquette s'affiche ici.";
                ZplEmptyActions.Visibility = Visibility.Visible;
                ReadSummary.Text = "Colle ici (zone « Programme ») le fichier .zpl, .prn ou .txt envoyé par le client, ouvre-le avec « Ouvrir un fichier », ou glisse-le sur la page.";
                return;
            }

            ZplEmptyActions.Visibility = Visibility.Collapsed;
            _rows.Clear();
            _cards.Clear();
            _selected = -1;
            ShowOtherProgram(text, kind);

            // TSPL, EPL2, DPL : l'aperçu est dessiné à partir des mêmes éléments qu'une étiquette ZPL.
            if (ForeignLabelReader.Read(text, kind, ReadDpi) is { Label.Elements.Count: > 0 } foreign && LabelRenderer is not null)
            {
                ZplEmptyBox.Visibility = Visibility.Collapsed;
                await DrawLabelAsync(foreign.Label, foreign.RenderJson(), seq, ProgramFile.Name(kind));
            }

            return;
        }

        ZplEmpty.Text = string.Empty;

        var label = ZplReader.Read(text);
        _label = label;
        BuildReadPanel(label);
        ZplEmptyBox.Visibility = Visibility.Collapsed;
        if (LabelRenderer is null)
        {
            return;
        }

        await DrawLabelAsync(label, ZplReader.RenderJson(label), seq, null);
    }

    /// <summary>Dessine l'aperçu d'une étiquette lue (ZPL, ou TSPL / EPL2 / DPL convertis) et l'affiche.</summary>
    private async Task DrawLabelAsync(ZplLabel label, string json, int seq, string? otherLanguage)
    {
        _label = label;
        PreviewNote.Text = "Dessin de l'étiquette…";
        var render = await LabelRenderer!(json);
        var (png, error) = (render.Png, render.Error);
        if (seq != _readSeq)
        {
            return;
        }

        _labelPng = png;
        _boxes = render.Boxes;
        if (png is null)
        {
            ZplScroll.Visibility = Visibility.Collapsed;
            PreviewNote.Text = "Aperçu impossible : " + (error ?? "erreur inconnue.");
            return;
        }

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = new MemoryStream(png);
        img.EndInit();
        img.Freeze();
        var firstRender = ZplImage.Source is null;
        ZplImage.Source = img;
        ZplScroll.Visibility = Visibility.Visible;
        if (firstRender || _newProgram)
        {
            // Sens de lecture : on tourne l'aperçu pour que la plupart des textes se lisent à l'endroit.
            var main = label.Texts.GroupBy(t => t.Orientation).OrderByDescending(g => g.Sum(t => t.Text.Trim().Length)).FirstOrDefault()?.Key ?? ZplOrientation.N;
            _zplRotation = main switch { ZplOrientation.B => 90, ZplOrientation.R => 270, ZplOrientation.I => 180, _ => 0 };
            _zplZoom = null;
            _newProgram = false;
        }

        ApplyZplTransform();
        ShowSelection();
        // Phrases traduites une à une (interface en anglais).
        PreviewNote.Text = Localization.T(otherLanguage is null
                ? "Aperçu indicatif (polices approchées)."
                : $"Aperçu indicatif du programme {otherLanguage} (polices et positions approchées : le rendu exact dépend de l'imprimante).")
            + " " + Localization.T("Clique sur un élément pour voir sa ligne ; ↺ ↻ pour pivoter, Ctrl + molette pour zoomer.")
            + (label.Inverted ? " " + Localization.T("L'imprimante sort cette étiquette tournée de 180°.") : string.Empty);
        ShowLabelSize();
    }

    /// <summary>
    /// Programme TSPL, EPL2, DPL, SBPL… : pas d'aperçu (dessiné pour le ZPL seulement), mais le langage, les codes-barres
    /// trouvés (TSPL, EPL2) et ce qu'ils contiennent ; le fichier peut être envoyé tel quel.
    /// </summary>
    private void ShowOtherProgram(string text, ProgramKind kind)
    {
        var name = ProgramFile.Name(kind);
        ZplEmpty.Text = kind == ProgramKind.Unknown
            ? "Langage non reconnu : ce texte n'est pas un programme ZPL, TSPL, EPL2, DPL, SBPL ou Fingerprint connu."
            : $"Programme {name} : l'aperçu de l'étiquette est dessiné pour le ZPL uniquement. Les codes-barres du programme sont listés à gauche."
              + (kind == ProgramKind.Dpl ? " En DPL, la position « y » se compte depuis le bas de l'étiquette (0,1 mm ou 0,01 pouce)." : string.Empty);
        var lines = text.Split('\n').Length;
        ReadSummary.Text = kind == ProgramKind.Unknown
            ? $"{lines} ligne{(lines > 1 ? "s" : string.Empty)} · langage non reconnu. Vérifie que c'est bien le fichier d'impression (et non un export PDF ou image)."
            : $"Programme {name} · {lines} lignes. Il peut être envoyé tel quel à une imprimante qui parle {name}.";

        if (kind is ProgramKind.Sbpl or ProgramKind.Fingerprint)
        {
            ReadPanel.Children.Add(AlertBox(new Scanalyse.Core.Content.Alert(Scanalyse.Core.Content.Severity.Info,
                $"{name} : lecture détaillée non disponible. Pour retrouver le contenu des codes, imprime l'étiquette et passe-la dans « Analyser une image » (photo ou capture), ou scanne-la au banc d'essai.")));
            return;
        }

        var codes = ProgramFile.Barcodes(text, kind);
        ReadPanel.Children.Add(Section(codes.Count == 0 ? "Aucun code-barres trouvé" : codes.Count == 1 ? "Code-barres" : $"Codes-barres ({codes.Count})"));
        var n = 0;
        foreach (var c in codes)
        {
            n++;
            var card = new StackPanel();
            var head = new DockPanel();
            var again = new Button
            {
                Style = (Style)FindResource("BtnGhost"),
                Content = "Reprendre dans Créer une étiquette",
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Reprend ce code dans « Créer une étiquette » pour générer un programme ZPL, TSPL ou EPL2",
            };
            DockPanel.SetDock(again, Dock.Right);
            again.Click += (_, _) =>
            {
                SetReadMode(false);
                Load(ProgramFile.ScannerContent(c), LabelCode.FromFormat(c.Symbology, c.Data), $"programme {name} lu, ligne {c.Line}");
            };
            var show = new Button
            {
                Style = (Style)FindResource("BtnGhost"),
                Content = $"Voir la ligne {c.Line}",
                Padding = new Thickness(8, 2, 8, 2),
            };
            DockPanel.SetDock(show, Dock.Right);
            show.Click += (_, _) => GoToLine(c.Line);
            head.Children.Add(again);
            head.Children.Add(show);
            head.Children.Add(new TextBlock { Text = $"Code {n} · {c.Symbology}", FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            card.Children.Add(head);
            card.Children.Add(new TextBlock { Text = $"{c.Command} · ligne {c.Line} · x {c.X}, y {c.Y}", FontSize = 12, Foreground = Res("Muted"), Margin = new Thickness(0, 2, 0, 8) });
            card.Children.Add(FieldCaption("Données lues par une douchette"));
            var content = ProgramFile.ScannerContent(c);
            card.Children.Add(Mono(ControlCharacters.Visualize(content)));
            if (c.Gs1 && Scanalyse.Core.Gs1.Gs1Parser.Parse(content) is { Elements.Count: > 0 } g)
            {
                foreach (var el in g.Elements)
                {
                    card.Children.Add(new TextBlock
                    {
                        Text = $"({el.Ai}) {el.Name} : {el.Display}" + (el.IsValid ? string.Empty : "  ⚠ " + string.Join(" ; ", el.Issues)),
                        FontSize = 12.5,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = Res(el.IsValid ? "Ink" : "BadFg"),
                        Margin = new Thickness(0, 0, 0, 2),
                    });
                }
            }

            ReadPanel.Children.Add(new Border
            {
                Child = card,
                BorderBrush = Res("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 8),
                Margin = new Thickness(0, 0, 0, 10),
            });
        }
    }

    private void ShowLabelSize()
    {
        if (_label is null)
        {
            return;
        }

        var dpi = ReadDpi;
        var w = _label.WidthDots ?? (ZplImage.Source as BitmapSource)?.PixelWidth ?? 0;
        var h = _label.LengthDots ?? (ZplImage.Source as BitmapSource)?.PixelHeight ?? 0;
        string Mm(int dots) => (dots * 25.4 / dpi).ToString("0", CultureInfo.CurrentCulture);
        var sized = _label.WidthDots is not null && _label.LengthDots is not null;
        PreviewInfo.Text = w > 0 ? $"{(sized ? string.Empty : "≈ ")}{Mm(w)} × {Mm(h)} mm · {dpi} dpi" : string.Empty;
        var codes = _label.Barcodes.Count();
        var texts = _label.Texts.Count();
        ReadSummary.Text = $"{codes} code{(codes > 1 ? "s" : string.Empty)}-barres{(codes > 1 ? string.Empty : string.Empty)}, {texts} texte{(texts > 1 ? "s" : string.Empty)}, "
            + $"{_label.Elements.OfType<ZplBox>().Count()} cadre(s) ou trait(s) · {(sized ? "taille fixée par le programme" : "taille réglée dans l'imprimante")}"
            + $" · {(_label.Utf8 ? "UTF-8 (^CI28)" : "page de codes 850")} · {_label.Copies} exemplaire{(_label.Copies > 1 ? "s" : string.Empty)}."
            + " Choisis la résolution de l'imprimante du client pour avoir les tailles en mm.";
        foreach (var t in ReadPanel.Children.OfType<FrameworkElement>().Where(c => c.Tag is ZplBarcode))
        {
            if (t is Border { Child: StackPanel sp } && sp.Children.Count > 1 && sp.Children[1] is TextBlock meta)
            {
                meta.Text = BarcodeMeta((ZplBarcode)t.Tag, dpi);
            }
        }
    }

    private static string BarcodeMeta(ZplBarcode c, int dpi)
    {
        var mm = c.ModuleWidth * 25.4 / dpi;
        var rot = c.Orientation switch { ZplOrientation.R => " · tourné 90°", ZplOrientation.I => " · tourné 180°", ZplOrientation.B => " · tourné 270°", _ => string.Empty };
        var size = c.Command is "^BQ" or "^BX" or "^B0" or "^BO"
            ? $"module {c.ModuleWidth} points ({mm.ToString("0.###", CultureInfo.CurrentCulture)} mm)"
            : $"module {c.ModuleWidth} points ({mm.ToString("0.###", CultureInfo.CurrentCulture)} mm) · hauteur {(c.Height * 25.4 / dpi).ToString("0.#", CultureInfo.CurrentCulture)} mm";
        var gs1 = c.Gs1 && c.Command is "^BC" or "^B2" && mm < 0.495 ? " · trop fin pour la logistique GS1 (0,495 mm min.)" : string.Empty;
        return $"{c.Command} · ligne {c.Line} · x {c.X}, y {c.Y} ({(c.Typeset ? "^FT" : "^FO")}){rot} · {size}{gs1}";
    }

    private void BuildReadPanel(ZplLabel label)
    {
        ReadPanel.Children.Clear();
        _cards.Clear();
        var dpi = ReadDpi;

        // Constats sur le programme
        var issues = label.Issues.OrderByDescending(i => i.Severity).ToList();
        if (issues.Count > 0)
        {
            ReadPanel.Children.Add(Section("Points à vérifier"));
            foreach (var i in issues)
            {
                ReadPanel.Children.Add(AlertBox(i));
            }
        }

        // Codes-barres
        var codes = label.Barcodes.ToList();
        ReadPanel.Children.Add(Section(codes.Count == 0 ? "Aucun code-barres" : codes.Count == 1 ? "Code-barres" : $"Codes-barres ({codes.Count})"));
        var n = 0;
        foreach (var c in codes)
        {
            n++;
            var card = new StackPanel();
            var head = new DockPanel();
            var again = new Button
            {
                Style = (Style)FindResource("BtnGhost"),
                Content = "Réimprimer ce code",
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Reprend ce code dans « Créer une étiquette » (même type, même contenu, même module) pour générer un programme propre",
            };
            DockPanel.SetDock(again, Dock.Right);
            again.Click += (_, _) => Recreate(c);
            head.Children.Add(again);
            var show = new Button
            {
                Style = (Style)FindResource("BtnGhost"),
                Content = $"Voir la ligne {c.Line}",
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = "Sélectionne la ligne de ce code dans le programme, pour la lire ou la corriger",
            };
            DockPanel.SetDock(show, Dock.Right);
            show.Click += (_, _) => Select(label.Elements.ToList().IndexOf(c), fromEditor: false);
            head.Children.Add(show);
            head.Children.Add(new TextBlock { Text = $"Code {n} · {c.Symbology}", FontWeight = FontWeights.SemiBold, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            card.Children.Add(head);
            card.Children.Add(new TextBlock { Text = BarcodeMeta(c, dpi), FontSize = 12, Foreground = Res("Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8) });

            card.Children.Add(FieldCaption("Données lues par une douchette"));
            card.Children.Add(Mono(ControlCharacters.Visualize(c.Content)));
            if (c.Readable != c.Content && c.Interpretation)
            {
                card.Children.Add(FieldCaption("Texte imprimé sous le code"));
                card.Children.Add(Mono(c.Readable));
            }

            if (c.Gs1 && Scanalyse.Core.Gs1.Gs1Parser.Parse(c.Content) is { Elements.Count: > 0 } g)
            {
                card.Children.Add(FieldCaption("Données GS1"));
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var row = 0;
                foreach (var el in g.Elements)
                {
                    grid.RowDefinitions.Add(new RowDefinition());
                    var k = new TextBlock { Text = $"({el.Ai}) {el.Name}", FontSize = 12.5, Foreground = Res("Muted"), Margin = new Thickness(0, 1, 14, 1), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 };
                    var v = new TextBlock { Text = el.Display + (el.IsValid ? string.Empty : "  ⚠ " + string.Join(" ; ", el.Issues)), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1) };
                    v.Foreground = Res(el.IsValid ? "Ink" : "BadFg");
                    Grid.SetRow(k, row);
                    Grid.SetRow(v, row);
                    Grid.SetColumn(v, 1);
                    grid.Children.Add(k);
                    grid.Children.Add(v);
                    row++;
                }

                card.Children.Add(grid);
            }

            if (c.Parameters.Count > 0)
            {
                card.Children.Add(new TextBlock
                {
                    Text = string.Join(" · ", c.Parameters.Select(x => $"{x.Label} : {x.Value}")),
                    FontSize = 12,
                    Foreground = Res("Muted"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 6),
                });
            }

            foreach (var note in c.Notes.OrderByDescending(x => x.Severity))
            {
                card.Children.Add(AlertBox(note));
            }

            var index = label.Elements.ToList().IndexOf(c);
            var cardBorder = new Border
            {
                Child = card,
                Tag = c,
                BorderBrush = Res("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12, 10, 12, 6),
                Margin = new Thickness(0, 0, 0, 10),
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            cardBorder.MouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is not TextBox)
                {
                    Select(index, fromEditor: false);
                }
            };
            _cards[index] = cardBorder;
            ReadPanel.Children.Add(cardBorder);
        }

        // Textes, cadres et traits : une ligne par champ, cliquable
        _rows.Clear();
        var indexed = label.Elements.Select((el, k) => (el, k)).ToList();
        AddElementSection("Textes", [.. indexed.Where(x => x.el is ZplText)], expanded: true);
        AddElementSection("Cadres et traits", [.. indexed.Where(x => x.el is ZplBox)], expanded: false);
        ShowLabelSize();
    }

    // ------------------------------------------------------------------ Sélection d'un élément (aperçu ⇄ programme ⇄ liste)

    private readonly Dictionary<int, Border> _rows = [];
    private readonly Dictionary<int, Border> _cards = [];
    private IReadOnlyList<int[]?> _boxes = [];
    private int _selected = -1;
    private bool _newProgram = true;
    private bool _selecting;
    private int _zplRotation;
    private double? _zplZoom;

    private void AddElementSection(string title, List<(ZplElement El, int Index)> items, bool expanded)
    {
        if (items.Count == 0)
        {
            return;
        }

        var toggle = new System.Windows.Controls.Primitives.ToggleButton
        {
            Style = (Style)FindResource("HexToggle"),
            Content = $"{title} ({items.Count})",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 4),
            IsChecked = expanded,
        };
        var list = new StackPanel { Visibility = expanded ? Visibility.Visible : Visibility.Collapsed };
        toggle.Checked += (_, _) => list.Visibility = Visibility.Visible;
        toggle.Unchecked += (_, _) => list.Visibility = Visibility.Collapsed;
        foreach (var (el, index) in items)
        {
            var text = el switch
            {
                ZplText t => $"« {t.Text.TrimEnd()} »",
                ZplBox b => b.Width <= b.Thickness * 2 || b.Height <= b.Thickness * 2
                    ? (b.Width > b.Height ? $"Trait horizontal {b.Width} points" : b.Width == b.Height ? $"Carré plein {b.Width} points" : $"Trait vertical {b.Height} points")
                    : $"Cadre {b.Width} × {b.Height}, trait {b.Thickness}",
                _ => string.Empty,
            };
            var detail = el switch
            {
                ZplText t => $"police {t.Font} · {t.Height}×{t.Width}" + (t.Orientation == ZplOrientation.N ? string.Empty : $" · {OrientationText(t.Orientation)}") + (t.Reverse ? " · inversé" : string.Empty),
                ZplBox b => b.Reverse ? "inversé" : b.White ? "blanc" : string.Empty,
                _ => string.Empty,
            };
            var row = new Border
            {
                Padding = new Thickness(8, 4, 8, 4),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.Transparent,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(3, 0, 0, 0),
                BorderBrush = Brushes.Transparent,
            };
            var dock = new DockPanel();
            var line = new TextBlock { Text = $"l.{el.Line}", Width = 44, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12, Foreground = Res("Muted"), VerticalAlignment = VerticalAlignment.Center };
            var info = new TextBlock { Text = detail, FontSize = 11.5, Foreground = Res("Muted"), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(line, Dock.Left);
            DockPanel.SetDock(info, Dock.Right);
            dock.Children.Add(line);
            dock.Children.Add(info);
            dock.Children.Add(new TextBlock { Text = text, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
            row.Child = dock;
            row.MouseEnter += (_, _) => { if (_selected != index) { row.Background = Res("Surface2"); } };
            row.MouseLeave += (_, _) => { if (_selected != index) { row.Background = Brushes.Transparent; } };
            row.MouseLeftButtonUp += (_, _) => Select(index, fromEditor: false);
            _rows[index] = row;
            list.Children.Add(row);
        }

        ReadPanel.Children.Add(toggle);
        ReadPanel.Children.Add(list);
    }

    private static string OrientationText(ZplOrientation o) => o switch
    {
        ZplOrientation.R => "tourné 90°",
        ZplOrientation.I => "tourné 180°",
        ZplOrientation.B => "tourné 270°",
        _ => "normal",
    };

    /// <summary>Met en avant un élément partout : cadre dans l'aperçu, ligne dans le programme, entrée dans la liste.</summary>
    private void Select(int index, bool fromEditor)
    {
        if (_label is null || index < 0 || index >= _label.Elements.Count)
        {
            return;
        }

        _selected = index;
        ShowSelection();
        if (!fromEditor)
        {
            _selecting = true;
            GoToLine(_label.Elements[index].Line);
            _selecting = false;
        }

        if (_rows.TryGetValue(index, out var row))
        {
            if (row.Parent is StackPanel { Visibility: Visibility.Collapsed } list)
            {
                list.Visibility = Visibility.Visible;
                if (ReadPanel.Children.IndexOf(list) is var k and > 0 && ReadPanel.Children[k - 1] is System.Windows.Controls.Primitives.ToggleButton t)
                {
                    t.IsChecked = true;
                }
            }

            row.BringIntoView();
        }
        else if (_cards.TryGetValue(index, out var card))
        {
            card.BringIntoView();
        }
    }

    private void ShowSelection()
    {
        foreach (var (k, row) in _rows)
        {
            row.Background = k == _selected ? Res("Surface2") : Brushes.Transparent;
            row.BorderBrush = k == _selected ? Res("Brand") : Brushes.Transparent;
        }

        foreach (var (k, card) in _cards)
        {
            card.BorderBrush = k == _selected ? Res("Brand") : Res("Line");
            card.BorderThickness = new Thickness(k == _selected ? 2 : 1);
        }

        ZplOverlay.Children.Clear();
        if (_selected < 0 || _selected >= _boxes.Count || _boxes[_selected] is not { } b || ZplImage.Source is null)
        {
            return;
        }

        var zoom = CurrentZoom();
        const double pad = 4;
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = Math.Max(4, b[2] - b[0] + pad * 2),
            Height = Math.Max(4, b[3] - b[1] + pad * 2),
            Stroke = new SolidColorBrush(Color.FromRgb(0xF0, 0x7F, 0x13)),
            StrokeThickness = Math.Max(2, 2.5 / zoom),
            Fill = new SolidColorBrush(Color.FromArgb(0x30, 0xF0, 0x7F, 0x13)),
            RadiusX = 3 / zoom,
            RadiusY = 3 / zoom,
        };
        Canvas.SetLeft(rect, b[0] - pad);
        Canvas.SetTop(rect, b[1] - pad);
        ZplOverlay.Children.Add(rect);
        Dispatcher.BeginInvoke(new Action(() => rect.BringIntoView()), DispatcherPriority.Loaded);
    }

    private void ZplFrame_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var p = e.GetPosition(ZplImage);
        var best = -1;
        var bestArea = double.MaxValue;
        for (var k = 0; k < _boxes.Count; k++)
        {
            if (_boxes[k] is { } b && p.X >= b[0] - 3 && p.X <= b[2] + 3 && p.Y >= b[1] - 3 && p.Y <= b[3] + 3)
            {
                var area = (double)(b[2] - b[0]) * (b[3] - b[1]);
                if (area < bestArea)
                {
                    best = k;
                    bestArea = area;
                }
            }
        }

        if (best >= 0)
        {
            Select(best, fromEditor: false);
        }
    }

    private void CodeBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_readMode || _selecting || _label is null || !CodeBox.IsKeyboardFocused)
        {
            return;
        }

        var line = CodeBox.GetLineIndexFromCharacterIndex(CodeBox.CaretIndex) + 1;
        var index = _label.Elements.ToList().FindIndex(el => el.Line == line);
        if (index >= 0 && index != _selected)
        {
            Select(index, fromEditor: true);
        }
    }

    // ------------------------------------------------------------------ Pivoter / zoomer l'aperçu

    private double CurrentZoom() => _zplZoom ?? FitZoom();

    private double FitZoom()
    {
        if (ZplImage.Source is not BitmapSource bmp || ZplScroll.ActualWidth < 10)
        {
            return 1;
        }

        double w = bmp.PixelWidth + 2, h = bmp.PixelHeight + 2;
        if (_zplRotation is 90 or 270)
        {
            (w, h) = (h, w);
        }

        return Math.Max(0.05, Math.Min((ZplScroll.ActualWidth - 6) / w, (ZplScroll.ActualHeight - 6) / h));
    }

    private void ApplyZplTransform()
    {
        var z = CurrentZoom();
        var t = new TransformGroup();
        t.Children.Add(new RotateTransform(_zplRotation));
        t.Children.Add(new ScaleTransform(z, z));
        ZplFrame.LayoutTransform = t;
        ZoomFitButton.Content = _zplZoom is null ? "Ajuster" : $"{Math.Round(z * 100)} %";
        ShowSelection();
    }

    private void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        _zplRotation = (_zplRotation + 270) % 360;
        ApplyZplTransform();
    }

    private void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        _zplRotation = (_zplRotation + 90) % 360;
        ApplyZplTransform();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomBy(1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomBy(0.8);

    private void ZoomFit_Click(object sender, RoutedEventArgs e)
    {
        _zplZoom = _zplZoom is null ? 1 : null; // « Ajuster » ⇄ taille réelle (1 point = 1 pixel)
        ApplyZplTransform();
    }

    private void ZoomBy(double factor)
    {
        _zplZoom = Math.Clamp(CurrentZoom() * factor, 0.05, 8);
        ApplyZplTransform();
    }

    private void ZplScroll_Wheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0)
        {
            ZoomBy(e.Delta > 0 ? 1.15 : 1 / 1.15);
            e.Handled = true;
        }
    }

    private void ZplScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_zplZoom is null)
        {
            ApplyZplTransform();
        }
    }

    /// <summary>Reprend un code lu dans le générateur, avec ses réglages, pour produire un programme propre.</summary>
    private void Recreate(ZplBarcode c)
    {
        var sym = c.Command switch
        {
            "^BC" => c.Gs1 ? LabelSymbology.Gs1_128 : LabelSymbology.Code128,
            "^BE" => LabelSymbology.Ean13,
            "^B8" => LabelSymbology.Ean8,
            "^BU" => LabelSymbology.UpcA,
            "^B3" => LabelSymbology.Code39,
            "^B2" => LabelSymbology.Itf,
            "^BQ" => LabelSymbology.QrCode,
            "^BX" => c.Gs1 ? LabelSymbology.Gs1DataMatrix : LabelSymbology.DataMatrix,
            "^B7" => LabelSymbology.Pdf417,
            "^B0" or "^BO" => LabelSymbology.Aztec,
            _ => LabelSymbology.Code128,
        };
        var dpi = ReadDpi;
        SetReadMode(false);
        _updating = true;
        Check(DpiChips, dpi.ToString(CultureInfo.InvariantCulture));
        ModuleBox.Text = c.ModuleWidth.ToString(CultureInfo.InvariantCulture);
        if (!Is2D(sym))
        {
            BarsBox.Text = (c.Height * 25.4 / dpi).ToString("0.#", CultureInfo.CurrentCulture);
            HriCheck.IsChecked = c.Interpretation;
        }

        Check(RotChips, c.Orientation switch { ZplOrientation.R => "90", ZplOrientation.I => "180", ZplOrientation.B => "270", _ => "0" });
        _updating = false;
        Load(c.Content, sym, $"programme ZPL lu, ligne {c.Line}");
    }

    private void GoTemplates_Click(object sender, RoutedEventArgs e) => SetPageMode(PageMode.Template);

    private void OpenProgram_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = Localization.T("Ouvrir le programme d'étiquette du client"),
            InitialDirectory = AppSettings.ExistingOrDocuments(AppSettings.Load().LabelsFolderOrDefault),
            Filter = Localization.T("Programmes d'étiquette (*.zpl;*.prn;*.txt;*.lbl;*.epl;*.tspl;*.dpl;*.nlbl)|*.zpl;*.prn;*.txt;*.lbl;*.epl;*.tspl;*.dpl;*.nlbl|Tous les fichiers (*.*)|*.*"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            OpenFile(dialog.FileName);
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void Root_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            OpenFile(files[0]);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Lit le fichier du client avec ses octets d'origine : UTF-8 s'il l'est, sinon octet par octet (Latin-1),
    /// pour que l'aperçu montre ce que l'imprimante imprime vraiment.
    /// </summary>
    private void OpenFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 4 * 1024 * 1024)
            {
                Log?.Invoke(LogKind.Warning, "Fichier trop gros pour un programme d'étiquette (plus de 4 Mo) : il contient sans doute des images.");
                return;
            }

            var bytes = File.ReadAllBytes(path);
            var (text, encoding) = ProgramFile.Decode(bytes);
            text = text.TrimStart('\uFEFF').Replace("\0", string.Empty, StringComparison.Ordinal);
            LoadProgram(text);
            Log?.Invoke(LogKind.Info, $"Programme ouvert : {Path.GetFileName(path)} ({bytes.Length} octets, {encoding}, {ProgramFile.Name(ProgramFile.Detect(text))}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke(LogKind.Error, "Lecture du fichier impossible : " + ex.Message);
        }
    }

    private void AnalyzeLabel_Click(object sender, RoutedEventArgs e)
    {
        if (_labelPng is null)
        {
            return;
        }

        AnalyzeRequested?.Invoke(_labelPng, "Programme ZPL" + (_label?.Barcodes.FirstOrDefault() is { } c ? " · " + c.Symbology : string.Empty));
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    private static TextBlock Section(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 13,
        Margin = new Thickness(0, 10, 0, 8),
    };

    private TextBlock FieldCaption(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = Res("Muted"),
        Margin = new Thickness(0, 0, 0, 3),
    };

    private TextBox Mono(string text) => new()
    {
        Text = text,
        IsReadOnly = true,
        BorderThickness = new Thickness(0),
        Background = Res("Surface2"),
        Padding = new Thickness(8, 6, 8, 6),
        Margin = new Thickness(0, 0, 0, 8),
        FontFamily = (FontFamily)FindResource("MonoFont"),
        FontSize = 12.5,
        Foreground = Res("Ink"),
        TextWrapping = TextWrapping.Wrap,
    };

    private Border AlertBox(Scanalyse.Core.Content.Alert a)
    {
        var (bg, fg) = a.Severity switch
        {
            Scanalyse.Core.Content.Severity.Ok => ("OkBg", "OkFg"),
            Scanalyse.Core.Content.Severity.Warning => ("WarnBg", "WarnFg"),
            Scanalyse.Core.Content.Severity.Error => ("BadBg", "BadFg"),
            _ => ("InfoBg", "InfoFg"),
        };
        return new Border
        {
            Background = Res(bg),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new TextBlock { Text = a.Message, Foreground = Res(fg), TextWrapping = TextWrapping.Wrap, FontSize = 12.5 },
        };
    }
}
