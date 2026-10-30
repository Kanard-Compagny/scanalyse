using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Scanalyse.Core.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Conf imprimante, trois parcours : « Analyser un fichier », « Convertir » (fichier de départ à gauche, langage d'arrivée
/// et résultat à droite, sans toucher à l'original) et « Créer un fichier » (formulaire à gauche, fichier écrit en direct à
/// droite). Conversion et création passent par les réglages communs (<see cref="PrinterSettings"/>).
/// </summary>
public partial class ConfigView
{
    private enum PageMode
    {
        Analyze,
        Convert,
        Create,
    }

    private static readonly int[] DpiChoices = [203, 300, 600];

    private PageMode _mode = PageMode.Analyze;
    private ConfigLanguage? _convertTarget;
    private ConfigLanguage? _createTarget;
    private int? _dpi;
    private ConversionResult? _convertResult;
    private ConversionResult? _createResult;

    // ------------------------------------------------------------------ Parcours

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        _mode = ModeConvert.IsChecked == true ? PageMode.Convert : ModeCreate.IsChecked == true ? PageMode.Create : PageMode.Analyze;
        if (EditorCard is null)
        {
            return;
        }

        SuggestPopup.IsOpen = false;
        EditorCard.Visibility = _mode == PageMode.Create ? Visibility.Collapsed : Visibility.Visible;
        CreateFormCard.Visibility = _mode == PageMode.Create ? Visibility.Visible : Visibility.Collapsed;
        AnalysisCard.Visibility = _mode == PageMode.Analyze ? Visibility.Visible : Visibility.Collapsed;
        ConvertCard.Visibility = _mode == PageMode.Convert ? Visibility.Visible : Visibility.Collapsed;
        CreatePreviewCard.Visibility = _mode == PageMode.Create ? Visibility.Visible : Visibility.Collapsed;
        EditorTitle.Text = Localization.T(_mode == PageMode.Convert ? "1. Le fichier à convertir" : "Fichier de configuration");
        SendButton.ToolTip = Localization.T(_mode switch
        {
            PageMode.Convert => "Envoie le fichier converti à l'imprimante",
            PageMode.Create => "Envoie le fichier créé à l'imprimante",
            _ => "Envoie le fichier à l'imprimante",
        });
        switch (_mode)
        {
            case PageMode.Convert:
                ShowConvert();
                break;
            case PageMode.Create:
                ShowCreate();
                break;
        }
    }

    /// <summary>Texte envoyé à l'imprimante : celui du parcours affiché.</summary>
    private string? TextToSend() => _mode switch
    {
        PageMode.Convert => _convertResult is { Settings.IsEmpty: false } c ? c.Text : null,
        PageMode.Create => _createResult is { Settings.IsEmpty: false } c ? c.Text : null,
        _ => CodeBox.Text.Trim().Length > 0 ? CodeBox.Text : null,
    };

    /// <summary>Résolution : celle choisie, sinon celle lue sur l'imprimante (Zebra), sinon 203 dpi.</summary>
    private int CurrentDpi()
    {
        if (_dpi is { } chosen)
        {
            return chosen;
        }

        if (_printer is not null && _printer.TryGetValue("head.resolution.in_dpi", out var v)
            && double.TryParse(v.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) && dpi is > 100 and < 1300)
        {
            return (int)dpi;
        }

        return 203;
    }

    /// <summary>Langage conseillé : celui de l'imprimante reconnue ; pour une Zebra, l'autre de ZPL / SGD que celui du fichier.</summary>
    private ConfigLanguage SuggestedTarget(bool converting)
    {
        var languages = _report?.Languages ?? [];
        if (_profile is { } p && p.Brand != PrinterBrand.Unknown)
        {
            var target = ConfigConverter.ForBrand(p.Brand);
            return converting && target == ConfigLanguage.Zpl && languages.Contains("ZPL") ? ConfigLanguage.Sgd : target;
        }

        return converting && languages.Contains("ZPL") ? ConfigLanguage.Tspl : ConfigLanguage.Zpl;
    }

    private static string Brands(ConfigLanguage language) => language switch
    {
        ConfigLanguage.Zpl => "Zebra : commandes ^ et ~",
        ConfigLanguage.Sgd => "Zebra : ! U1 setvar, réglages enregistrés d'office",
        ConfigLanguage.Tspl => "TSC (et imprimantes compatibles TSPL)",
        ConfigLanguage.Epl2 => "Zebra anciennes (LP / TLP 2844…)",
        ConfigLanguage.Dpl => "Honeywell / Datamax (classe I, M, E…)",
        ConfigLanguage.Fingerprint => "Honeywell / Intermec (PC, PD, PM, PX…)",
        _ => "SATO (CL4NX, CT4-LX…)",
    };

    private static string Short(ConfigLanguage language) => language switch
    {
        ConfigLanguage.Zpl => "ZPL",
        ConfigLanguage.Sgd => "SGD",
        ConfigLanguage.Tspl => "TSPL",
        ConfigLanguage.Epl2 => "EPL2",
        ConfigLanguage.Dpl => "DPL",
        ConfigLanguage.Fingerprint => "Fingerprint",
        _ => "SBPL",
    };

    /// <summary>Choix de la résolution, commun aux deux parcours.</summary>
    private void FillDpi(WrapPanel panel, string group, Action changed)
    {
        panel.Children.Clear();
        foreach (var dpi in DpiChoices)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = group,
                Content = $"{dpi} dpi",
                IsChecked = dpi == CurrentDpi(),
                Margin = new Thickness(0, 0, 6, 0),
            };
            chip.Checked += (_, _) =>
            {
                _dpi = dpi;
                changed();
            };
            panel.Children.Add(chip);
        }
    }

    // ------------------------------------------------------------------ Convertir

    private void ShowConvert()
    {
        ConvertTargets.Children.Clear();
        foreach (var language in Enum.GetValues<ConfigLanguage>())
        {
            var content = new StackPanel();
            content.Children.Add(new TextBlock { Text = Localization.T(ConfigConverter.Name(language)), FontWeight = FontWeights.SemiBold });
            var sub = new TextBlock { Text = Localization.T(Brands(language)), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            content.Children.Add(sub);
            var card = new RadioButton
            {
                Style = (Style)FindResource("ChoiceCard"),
                GroupName = "ConvertTarget",
                Content = content,
                Width = 236,
                Margin = new Thickness(0, 0, 8, 8),
                IsChecked = language == _convertTarget,
            };
            card.Checked += (_, _) =>
            {
                _convertTarget = language;
                RefreshConvert();
            };
            ConvertTargets.Children.Add(card);
        }

        FillDpi(ConvertDpi, "ConvertDpi", RefreshConvert);
        RefreshConvert();
    }

    /// <summary>Conversion en direct : à chaque choix de langage ou de résolution, et à chaque modification du fichier de départ.</summary>
    private void RefreshConvert()
    {
        if (_mode != PageMode.Convert)
        {
            return;
        }

        var source = _report is { Languages.Count: > 0 } r ? string.Join(", ", r.Languages) : null;
        ConvertSource.Text = CodeBox.Text.Trim().Length == 0
            ? Localization.T("Colle ou ouvre d'abord, à gauche, le fichier à convertir.")
            : string.Format(CultureInfo.InvariantCulture, Localization.T("Fichier de départ : {0}. Choisis le langage de l'imprimante qui recevra le fichier ({1} conseillé)."),
                source ?? Localization.T("langage non reconnu"), Short(SuggestedTarget(converting: true)));
        if (_convertTarget is not { } target || CodeBox.Text.Trim().Length == 0)
        {
            _convertResult = null;
            ConvertResult.Visibility = Visibility.Collapsed;
            return;
        }

        var result = ConfigConverter.Convert(CodeBox.Text, target, CurrentDpi());
        _convertResult = result;
        ConvertResult.Visibility = Visibility.Visible;
        ConvertOutput.Text = result.Settings.IsEmpty ? string.Empty : result.Text;
        Placeholder.SetText(ConvertOutput, Localization.T("Aucun réglage commun dans le fichier de départ : rien à convertir."));
        ConvertSummary.Text = Summary(result);
        Notes(ConvertNotes, result, showLines: true);
    }

    private static string Summary(ConversionResult result) => string.Format(CultureInfo.InvariantCulture,
        Localization.T("{0} repris · {1} à vérifier après un essai (valeur approchée) · {2} non repris (à régler autrement sur l'imprimante)."),
        result.Notes.Count(n => n.Status == ConversionStatus.Converted),
        result.Notes.Count(n => n.Status == ConversionStatus.Approximated),
        result.Notes.Count(n => n.Status == ConversionStatus.NotConverted));

    private void Notes(StackPanel panel, ConversionResult result, bool showLines)
    {
        panel.Children.Clear();
        foreach (var note in result.Notes.OrderBy(n => n.Status switch { ConversionStatus.NotConverted => 0, ConversionStatus.Approximated => 1, _ => 2 }))
        {
            var (bg, fg, label) = note.Status switch
            {
                ConversionStatus.NotConverted => ("BadBg", "BadFg", "Non repris"),
                ConversionStatus.Approximated => ("WarnBg", "WarnFg", "À vérifier"),
                _ => ("OkBg", "OkFg", "Repris"),
            };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5 };
            text.SetResourceReference(TextBlock.ForegroundProperty, fg);
            text.Inlines.Add(new Run(Localization.T(label)
                + (showLines && note.Line > 0 ? " · " + string.Format(CultureInfo.InvariantCulture, Localization.T("ligne {0}"), note.Line) : string.Empty) + " : ")
            { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run(Localization.T(note.Message)));
            var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(0, 0, 0, 5), Child = text };
            border.SetResourceReference(Border.BackgroundProperty, bg);
            if (showLines && note.Line > 0)
            {
                border.Cursor = System.Windows.Input.Cursors.Hand;
                border.ToolTip = Localization.T("Clique pour voir la ligne dans le fichier de départ");
                border.MouseLeftButtonUp += (_, _) => GoToLine(note.Line);
            }

            panel.Children.Add(border);
        }
    }

    // ------------------------------------------------------------------ Résultat (Convertir, Créer)

    private ConversionResult? CurrentResult => _mode == PageMode.Convert ? _convertResult : _createResult;

    private ConfigLanguage? CurrentResultLanguage => _mode == PageMode.Convert ? _convertTarget : _createTarget ?? SuggestedTarget(converting: false);

    private void SaveResult_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentResult is not { Settings.IsEmpty: false } result)
        {
            Log?.Invoke(LogKind.Warning, "Rien à enregistrer pour le moment.");
            return;
        }

        var language = CurrentResultLanguage ?? ConfigLanguage.Zpl;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("Enregistrer le fichier de configuration"),
            Filter = Localization.T("Fichier texte (*.txt)|*.txt|ZPL (*.zpl)|*.zpl|Fichier d'impression (*.prn)|*.prn|Tous les fichiers|*.*"),
            FilterIndex = language == ConfigLanguage.Zpl ? 2 : 1,
            FileName = "configuration-" + Short(language).ToLowerInvariant() + (language == ConfigLanguage.Zpl ? ".zpl" : ".txt"),
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (_filePath is not null)
        {
            dialog.InitialDirectory = Path.GetDirectoryName(_filePath) ?? string.Empty;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, new UTF8Encoding(false).GetBytes(result.Text));
            Log?.Invoke(LogKind.Ok, $"Configuration enregistrée : {dialog.FileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke(LogKind.Error, "Enregistrement impossible : " + ex.Message);
        }
    }

    private void CopyResult_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentResult is not { Settings.IsEmpty: false } result)
        {
            return;
        }

        try
        {
            Clipboard.SetText(result.Text);
            Log?.Invoke(LogKind.Info, "Fichier copié dans le presse-papiers.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    /// <summary>Ouvre le résultat dans « Analyser un fichier » (vérification ligne par ligne, retouches, envoi).</summary>
    private void OpenResult_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentResult is not { Settings.IsEmpty: false } result)
        {
            return;
        }

        var title = _mode == PageMode.Convert
            ? string.Format(CultureInfo.InvariantCulture, Localization.T("Converti en {0}"), Short(_convertTarget ?? ConfigLanguage.Zpl))
            : string.Format(CultureInfo.InvariantCulture, Localization.T("Créé en {0}"), Short(CurrentResultLanguage ?? ConfigLanguage.Zpl));
        _fileBytes = null;
        _filePath = null;
        _fileEncoding = "UTF-8";
        CodeBox.Text = result.Text;
        FileInfo.Text = title + " · " + Localization.T("non enregistré (Enregistrer sous…)");
        ModeAnalyze.IsChecked = true;
        Analyze();
    }

    // ------------------------------------------------------------------ Créer

    private bool _assistantBuilt;
    private bool _filling;
    private readonly TextBox _darkness = new();
    private readonly TextBlock _darknessHint = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _speed = new();
    private readonly WrapPanel _media = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _gap = new();
    private readonly WrapPanel _method = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _modeChips = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _width = new();
    private readonly TextBox _length = new();
    private readonly TextBox _left = new();
    private readonly TextBox _top = new();
    private readonly TextBox _tear = new();
    private readonly WrapPanel _network = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _ip = new();
    private readonly TextBox _mask = new();
    private readonly TextBox _gateway = new();
    private readonly CheckBox _save = new() { IsChecked = true };
    private readonly StackPanel _staticIp = new();

    private void ShowCreate()
    {
        if (!_assistantBuilt)
        {
            BuildAssistant();
            _assistantBuilt = true;
        }

        CreateTargets.Children.Clear();
        foreach (var language in Enum.GetValues<ConfigLanguage>())
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "CreateTarget",
                Content = Short(language),
                ToolTip = Localization.T(ConfigConverter.Name(language)) + " — " + Localization.T(Brands(language)),
                IsChecked = language == (_createTarget ?? SuggestedTarget(converting: false)),
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Checked += (_, _) =>
            {
                _createTarget = language;
                RefreshCreate();
            };
            CreateTargets.Children.Add(chip);
        }

        FillDpi(CreateDpi, "CreateDpi", RefreshCreate);
        FromFileButton.Visibility = CodeBox.Text.Trim().Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FromPrinterButton.Visibility = _printer is not null ? Visibility.Visible : Visibility.Collapsed;
        RefreshCreate();
    }

    private void BuildAssistant()
    {
        var p = AssistantPanel;
        p.Children.Add(Section("Impression"));
        p.Children.Add(Field("Noirceur (0 à 100 %)", _darkness, "ex. 60"));
        p.Children.Add(_darknessHint);
        _darknessHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        p.Children.Add(Field("Vitesse (pouces par seconde ; 1 pouce/s ≈ 25 mm/s)", _speed, "ex. 4"));
        p.Children.Add(AsLabel("Méthode d'impression"));
        Chips<PrintMethod?>(_method, "AsMethod", [("Ne pas changer", null), ("Transfert thermique (avec ruban)", PrintMethod.ThermalTransfer), ("Thermique direct (sans ruban)", PrintMethod.DirectThermal)]);
        p.Children.Add(_method);
        p.Children.Add(AsLabel("Sortie des étiquettes"));
        Chips<OutputMode?>(_modeChips, "AsMode",
        [
            ("Ne pas changer", null), ("Détachage", OutputMode.TearOff), ("Prédécollage", OutputMode.PeelOff), ("Enroulement", OutputMode.Rewind),
            ("Coupe", OutputMode.Cutter), ("Coupe différée", OutputMode.DelayedCut), ("Applicateur", OutputMode.Applicator),
        ]);
        p.Children.Add(_modeChips);

        p.Children.Add(Section("Étiquettes"));
        p.Children.Add(AsLabel("Type de support"));
        Chips<MediaKind?>(_media, "AsMedia", [("Ne pas changer", null), ("Étiquettes séparées par un espace", MediaKind.Gap), ("Marque noire au dos", MediaKind.Mark), ("Continu", MediaKind.Continuous)]);
        p.Children.Add(_media);
        p.Children.Add(Field("Espace entre étiquettes, ou hauteur de la marque (mm)", _gap, "ex. 3"));
        p.Children.Add(Field("Largeur d'impression (mm)", _width, "ex. 100"));
        p.Children.Add(Field("Longueur d'étiquette (mm)", _length, "ex. 150"));

        p.Children.Add(Section("Réglages fins"));
        p.Children.Add(Field("Décalage horizontal (mm)", _left, "ex. 0"));
        p.Children.Add(Field("Décalage vertical (mm)", _top, "ex. 0"));
        p.Children.Add(Field("Position de détachage (mm)", _tear, "ex. 0"));

        p.Children.Add(Section("Réseau filaire"));
        Chips<bool?>(_network, "AsNet", [("Ne pas changer", null), ("Adresse automatique (DHCP)", true), ("Adresse fixe", false)]);
        p.Children.Add(_network);
        _staticIp.Children.Add(Field("Adresse IP", _ip, "ex. 192.168.1.50"));
        _staticIp.Children.Add(Field("Masque", _mask, "ex. 255.255.255.0"));
        _staticIp.Children.Add(Field("Passerelle", _gateway, "ex. 192.168.1.1"));
        _staticIp.Visibility = Visibility.Collapsed;
        p.Children.Add(_staticIp);

        _save.Style = (Style)FindResource("CheckLine");
        _save.Content = Localization.T("Enregistrer les réglages dans l'imprimante (conservés à l'extinction)");
        _save.Margin = new Thickness(0, 14, 0, 8);
        _save.Checked += (_, _) => RefreshCreate();
        _save.Unchecked += (_, _) => RefreshCreate();
        p.Children.Add(_save);
    }

    private static TextBlock Section(string title) =>
        new() { Text = Localization.T(title), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };

    private TextBlock AsLabel(string text) =>
        new() { Text = Localization.T(text), Margin = new Thickness(0, 10, 0, 0), Style = (Style)FindResource("FieldLabel"), TextWrapping = TextWrapping.Wrap };

    private StackPanel Field(string label, TextBox box, string hint)
    {
        box.Style = (Style)FindResource("FieldInput");
        box.Width = 220;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.Margin = new Thickness(0, 4, 0, 0);
        Placeholder.SetText(box, Localization.T(hint));
        box.TextChanged += (_, _) => RefreshCreate();
        var panel = new StackPanel();
        panel.Children.Add(AsLabel(label));
        panel.Children.Add(box);
        return panel;
    }

    private void Chips<T>(WrapPanel panel, string group, (string Label, T Value)[] items)
    {
        panel.Children.Clear();
        foreach (var (label, value) in items)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = group,
                Content = Localization.T(label),
                Tag = value,
                IsChecked = value is null,
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Checked += (_, _) =>
            {
                if (group == "AsNet")
                {
                    _staticIp.Visibility = Equals(value, false) ? Visibility.Visible : Visibility.Collapsed;
                }

                RefreshCreate();
            };
            panel.Children.Add(chip);
        }
    }

    private static object? Selected(WrapPanel panel) => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag;

    private static void Select(WrapPanel panel, object? value)
    {
        foreach (var chip in panel.Children.OfType<RadioButton>())
        {
            chip.IsChecked = Equals(chip.Tag, value);
        }
    }

    private static string N(double? v) => v is { } d ? d.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;

    private void FillForm(PrinterSettings s)
    {
        _filling = true;
        try
        {
            _darkness.Text = s.Darkness is { } d ? N(Math.Round(d * 100)) : string.Empty;
            _speed.Text = N(s.SpeedIps);
            Select(_media, s.Media);
            _gap.Text = N(s.GapMm);
            Select(_method, s.Method);
            Select(_modeChips, s.Mode);
            _width.Text = N(s.WidthMm);
            _length.Text = N(s.LengthMm);
            _left.Text = N(s.LeftShiftMm);
            _top.Text = N(s.TopShiftMm);
            _tear.Text = N(s.TearOffMm);
            Select(_network, s.Dhcp);
            _ip.Text = s.IpAddress ?? string.Empty;
            _mask.Text = s.Netmask ?? string.Empty;
            _gateway.Text = s.Gateway ?? string.Empty;
        }
        finally
        {
            _filling = false;
        }

        RefreshCreate();
    }

    private void FromFile_Click(object sender, RoutedEventArgs e)
    {
        var (s, _, _) = ConfigConverter.Extract(CodeBox.Text, CurrentDpi());
        FillForm(s);
        Log?.Invoke(LogKind.Info, s.IsEmpty ? "Aucun réglage commun dans le fichier ouvert." : "Réglages du fichier ouvert repris dans le formulaire.");
    }

    /// <summary>Réglages lus sur la Zebra, relus comme un fichier SGD pour remplir le formulaire.</summary>
    private void FromPrinter_Click(object sender, RoutedEventArgs e)
    {
        if (_printer is null)
        {
            return;
        }

        string[] keys = ["print.tone", "media.speed", "ezpl.media_type", "ezpl.print_method", "media.printmode", "ezpl.print_width", "zpl.label_length", "zpl.left_position", "ezpl.tear_off"];
        var text = new StringBuilder();
        foreach (var key in keys)
        {
            if (_printer.TryGetValue(key, out var v) && v.Value.Length > 0)
            {
                text.Append(CultureInfo.InvariantCulture, $"! U1 setvar \"{key}\" \"{v.Value}\"\r\n");
            }
        }

        var (s, _, _) = ConfigConverter.Extract(text.ToString(), CurrentDpi());
        FillForm(s);
        Log?.Invoke(LogKind.Info, "Réglages de l'imprimante repris dans le formulaire : change ce qui doit l'être.");
    }

    private void ClearForm_Click(object sender, RoutedEventArgs e) => FillForm(new PrinterSettings());

    private static double? Read(TextBox box, out bool invalid)
    {
        invalid = false;
        var t = box.Text.Trim().Replace(',', '.');
        if (t.Length == 0)
        {
            return null;
        }

        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }

        invalid = true;
        return null;
    }

    /// <summary>Écrit le fichier en direct à partir du formulaire ; une valeur illisible est signalée sans bloquer le reste.</summary>
    private void RefreshCreate()
    {
        if (_filling || !_assistantBuilt || _mode != PageMode.Create)
        {
            return;
        }

        var invalid = new List<string>();
        double? Value(TextBox box, string what)
        {
            var v = Read(box, out var bad);
            if (bad)
            {
                invalid.Add(Localization.T(what));
            }

            return v;
        }

        var dhcp = (bool?)Selected(_network);
        var settings = new PrinterSettings
        {
            Darkness = Value(_darkness, "Noirceur") is { } d ? Math.Clamp(d / 100, 0, 1) : null,
            SpeedIps = Value(_speed, "Vitesse"),
            Media = (MediaKind?)Selected(_media),
            GapMm = Value(_gap, "Espace"),
            Method = (PrintMethod?)Selected(_method),
            Mode = (OutputMode?)Selected(_modeChips),
            WidthMm = Value(_width, "Largeur"),
            LengthMm = Value(_length, "Longueur"),
            LeftShiftMm = Value(_left, "Décalage horizontal"),
            TopShiftMm = Value(_top, "Décalage vertical"),
            TearOffMm = Value(_tear, "Position de détachage"),
            Dhcp = dhcp,
            IpAddress = dhcp == false ? _ip.Text.Trim() : null,
            Netmask = dhcp == false ? _mask.Text.Trim() : null,
            Gateway = dhcp == false ? _gateway.Text.Trim() : null,
            Save = _save.IsChecked == true,
            Dpi = CurrentDpi(),
        };
        if (dhcp == false && !System.Net.IPAddress.TryParse(_ip.Text.Trim(), out _))
        {
            invalid.Add(Localization.T("Adresse IP"));
        }

        var target = _createTarget ?? SuggestedTarget(converting: false);
        UpdateDarknessHint(settings.Darkness, target);
        if (settings.IsEmpty)
        {
            _createResult = null;
            CreateOutput.Text = string.Empty;
            Placeholder.SetText(CreateOutput, Localization.T("Renseigne au moins un réglage à gauche : le fichier s'écrit ici."));
            CreateSummary.Text = string.Empty;
            CreateNotes.Children.Clear();
            return;
        }

        var result = ConfigConverter.Write(settings, target);
        _createResult = invalid.Count > 0 ? null : result;
        CreateOutput.Text = result.Text;
        CreateSummary.Text = invalid.Count > 0
            ? string.Format(CultureInfo.InvariantCulture, Localization.T("À corriger avant d'enregistrer ou d'envoyer : {0}."), string.Join(", ", invalid))
            : string.Format(CultureInfo.InvariantCulture, Localization.T("{0} écrit pour {1}. Les lignes « à vérifier » ou « non repris » sont détaillées ci-dessous."),
                Short(target), Localization.T(Brands(target)));
        CreateSummary.SetResourceReference(TextBlock.ForegroundProperty, invalid.Count > 0 ? "BadFg" : "Muted");
        Notes(CreateNotes, new ConversionResult(result.Text, [.. result.Notes.Where(n => n.Status != ConversionStatus.Converted)], result.Settings), showLines: false);
    }

    /// <summary>Équivalent de la noirceur saisie dans l'échelle du langage choisi.</summary>
    private void UpdateDarknessHint(double? darkness, ConfigLanguage target)
    {
        if (darkness is not { } d)
        {
            _darknessHint.Text = Localization.T("Vide : la noirceur n'est pas changée.");
            return;
        }

        var written = target switch
        {
            ConfigLanguage.Zpl => $"~SD{Math.Round(d * 30):00} (0 à 30)",
            ConfigLanguage.Sgd => $"print.tone {Math.Round(d * 30)} (0 à 30)",
            ConfigLanguage.Tspl => $"DENSITY {Math.Round(d * 15)} (0 à 15)",
            ConfigLanguage.Epl2 => $"D{Math.Round(d * 15)} (0 à 15)",
            ConfigLanguage.Dpl => $"H{Math.Round(d * 30):00} (0 à 30, dans chaque format)",
            ConfigLanguage.Fingerprint => $"CONTRAST {Math.Round((d * 20) - 10):+0;-0;0}% (-10 à +10)",
            _ => $"<ESC>#E{Math.Round((d * 4) + 1)} (1 à 5)",
        };
        _darknessHint.Text = string.Format(CultureInfo.InvariantCulture, Localization.T("Sera écrit : {0}"), written);
    }
}
