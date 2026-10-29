using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Scanalyse.Core.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Conf imprimante : conversion du fichier vers le langage d'une autre marque, et assistant « Configurer » qui écrit le
/// fichier à partir d'un formulaire (pré-rempli avec les réglages du fichier ouvert). Les deux passent par les réglages
/// communs (<see cref="PrinterSettings"/>) et affichent le compte-rendu dans la vue « Conversion ».
/// </summary>
public partial class ConfigView
{
    private static readonly int[] DpiChoices = [203, 300, 600];

    private ConversionResult? _conversion;
    private string? _beforeConvert;
    private string _conversionTitle = string.Empty;
    private ConfigLanguage? _target;
    private int? _dpi;

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

    /// <summary>Langage d'arrivée : celui choisi, sinon celui de l'imprimante reconnue (Zebra : SGD pour un fichier ZPL, et inversement).</summary>
    private ConfigLanguage CurrentTarget()
    {
        if (_target is { } chosen)
        {
            return chosen;
        }

        var languages = _report?.Languages ?? [];
        if (_profile is { } p && p.Brand != PrinterBrand.Unknown)
        {
            var target = ConfigConverter.ForBrand(p.Brand);
            return target == ConfigLanguage.Zpl && languages.Contains("ZPL") ? ConfigLanguage.Sgd : target;
        }

        return languages.Contains("ZPL") ? ConfigLanguage.Sgd : ConfigLanguage.Zpl;
    }

    private void FillChoices(WrapPanel targets, WrapPanel dpis, string group)
    {
        targets.Children.Clear();
        foreach (var language in Enum.GetValues<ConfigLanguage>())
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = group + "Target",
                Content = Localization.T(ConfigConverter.Name(language)),
                IsChecked = language == CurrentTarget(),
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Checked += (_, _) =>
            {
                _target = language;
                UpdateDarknessHint();
            };
            targets.Children.Add(chip);
        }

        dpis.Children.Clear();
        foreach (var dpi in DpiChoices)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = group + "Dpi",
                Content = $"{dpi} dpi",
                IsChecked = dpi == CurrentDpi(),
                Margin = new Thickness(0, 0, 6, 0),
            };
            chip.Checked += (_, _) => _dpi = dpi;
            dpis.Children.Add(chip);
        }
    }

    // ------------------------------------------------------------------ Convertir vers…

    private void ConvertButton_Click(object sender, RoutedEventArgs e)
    {
        FillChoices(ConvertTargets, ConvertDpi, "Convert");
        ConvertPopup.IsOpen = true;
    }

    private void Convert_Click(object sender, RoutedEventArgs e)
    {
        ConvertPopup.IsOpen = false;
        if (CodeBox.Text.Trim().Length == 0)
        {
            Log?.Invoke(LogKind.Warning, "Rien à convertir : colle ou ouvre d'abord un fichier de configuration.");
            return;
        }

        var target = CurrentTarget();
        var result = ConfigConverter.Convert(CodeBox.Text, target, CurrentDpi());
        if (result.Settings.IsEmpty)
        {
            _conversion = result;
            _conversionTitle = Localization.T("Rien à convertir");
            ShowConversion();
            Log?.Invoke(LogKind.Warning, "Aucun réglage commun trouvé dans ce fichier : il n'a pas été modifié.");
            return;
        }

        ApplyConversion(result, string.Format(CultureInfo.InvariantCulture, Localization.T("Converti en {0}"), Localization.T(ConfigConverter.Name(target))));
    }

    /// <summary>Remplace le fichier par le résultat (le texte d'avant reste disponible) et montre le compte-rendu.</summary>
    private void ApplyConversion(ConversionResult result, string title)
    {
        _beforeConvert = CodeBox.Text;
        _conversion = result;
        _conversionTitle = title;
        _fileBytes = null;
        CodeBox.Text = result.Text;
        FileInfo.Text = title + " · " + Localization.T("non enregistré (Enregistrer sous…)");
        Analyze();
        ShowConversion();
        var notes = result.Notes;
        Log?.Invoke(LogKind.Ok, string.Format(CultureInfo.InvariantCulture, Localization.T("{0} : {1} réglage(s) repris, {2} approché(s), {3} non converti(s)."),
            title,
            notes.Count(n => n.Status == ConversionStatus.Converted),
            notes.Count(n => n.Status == ConversionStatus.Approximated),
            notes.Count(n => n.Status == ConversionStatus.NotConverted)));
    }

    /// <summary>Nouveau fichier : l'ancien compte-rendu de conversion n'a plus lieu d'être.</summary>
    private void ResetConversion()
    {
        _conversion = null;
        _beforeConvert = null;
        if (ViewConversion.IsChecked == true)
        {
            ViewProblems.IsChecked = true;
        }

        ViewConversion.Visibility = Visibility.Collapsed;
    }

    private void ShowConversion()
    {
        ViewConversion.Visibility = Visibility.Visible;
        if (ViewConversion.IsChecked == true)
        {
            Render();
        }
        else
        {
            ViewConversion.IsChecked = true;
        }
    }

    private void RenderConversion()
    {
        if (_conversion is not { } c)
        {
            return;
        }

        var head = new TextBlock { Text = _conversionTitle, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        ResultPanel.Children.Add(head);
        ResultPanel.Children.Add(Muted(string.Format(CultureInfo.InvariantCulture,
            Localization.T("{0} repris · {1} approché(s), à vérifier après un essai · {2} non converti(s), à régler autrement sur l'imprimante."),
            c.Notes.Count(n => n.Status == ConversionStatus.Converted),
            c.Notes.Count(n => n.Status == ConversionStatus.Approximated),
            c.Notes.Count(n => n.Status == ConversionStatus.NotConverted))));
        if (_beforeConvert is not null)
        {
            var back = new Button { Style = (Style)FindResource("BtnSecondary"), Content = Localization.T("Revenir au fichier d'avant"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 10) };
            back.Click += (_, _) =>
            {
                CodeBox.Text = _beforeConvert ?? string.Empty;
                _beforeConvert = null;
                FileInfo.Text = Localization.T("Fichier d'avant la conversion");
                Analyze();
                Render();
            };
            ResultPanel.Children.Add(back);
        }

        foreach (var note in c.Notes.OrderBy(n => n.Status switch { ConversionStatus.NotConverted => 0, ConversionStatus.Approximated => 1, _ => 2 }))
        {
            var (bg, fg, label) = note.Status switch
            {
                ConversionStatus.NotConverted => ("BadBg", "BadFg", "Non converti"),
                ConversionStatus.Approximated => ("WarnBg", "WarnFg", "À vérifier"),
                _ => ("OkBg", "OkFg", "Repris"),
            };
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            text.SetResourceReference(TextBlock.ForegroundProperty, fg);
            text.Inlines.Add(new Run(Localization.T(label)
                + (note.Line > 0 ? " · " + string.Format(CultureInfo.InvariantCulture, Localization.T("ligne {0} du fichier d'origine"), note.Line) : string.Empty) + " : ")
            { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run(Localization.T(note.Message)));
            var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = text };
            border.SetResourceReference(Border.BackgroundProperty, bg);
            ResultPanel.Children.Add(border);
        }
    }

    // ------------------------------------------------------------------ Assistant « Configurer »

    private bool _assistantBuilt;
    private readonly WrapPanel _asTargets = new() { Margin = new Thickness(0, 4, 0, 10) };
    private readonly WrapPanel _asDpi = new() { Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBox _darkness = new();
    private readonly TextBlock _darknessHint = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBox _speed = new();
    private readonly WrapPanel _media = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _gap = new();
    private readonly WrapPanel _method = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _mode = new() { Margin = new Thickness(0, 4, 0, 0) };
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

    /// <summary>Ouverture de l'assistant : construit une fois, pré-rempli avec les réglages du fichier à la première ouverture.</summary>
    private void ShowAssistant()
    {
        if (!_assistantBuilt)
        {
            BuildAssistant();
            _assistantBuilt = true;
            LoadFromFile(quiet: true);
        }

        FillChoices(_asTargets, _asDpi, "Assistant");
        UpdateDarknessHint();
    }

    private void BuildAssistant()
    {
        var p = AssistantPanel;
        p.Children.Add(Muted(Localization.T("Règle les valeurs voulues (laisse vide ce qui ne doit pas changer) : Scanalyse écrit le fichier dans le langage choisi, prêt à vérifier et à envoyer.")));
        p.Children.Add(AsLabel("Langage du fichier"));
        p.Children.Add(_asTargets);
        p.Children.Add(AsLabel("Résolution de la tête"));
        p.Children.Add(_asDpi);

        p.Children.Add(Section("Impression"));
        p.Children.Add(Field("Noirceur (0 à 100 %)", _darkness, "ex. 60"));
        p.Children.Add(_darknessHint);
        _darknessHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _darkness.TextChanged += (_, _) => UpdateDarknessHint();
        p.Children.Add(Field("Vitesse (pouces par seconde)", _speed, "ex. 4  (1 pouce/s ≈ 25 mm/s)"));
        p.Children.Add(AsLabel("Méthode d'impression"));
        Chips<PrintMethod?>(_method, "AsMethod", [("—", null), ("Transfert thermique (ruban)", PrintMethod.ThermalTransfer), ("Thermique direct", PrintMethod.DirectThermal)]);
        p.Children.Add(_method);
        p.Children.Add(AsLabel("Sortie des étiquettes"));
        Chips<OutputMode?>(_mode, "AsMode",
        [
            ("—", null), ("Détachage", OutputMode.TearOff), ("Prédécollage", OutputMode.PeelOff), ("Enroulement", OutputMode.Rewind),
            ("Coupe", OutputMode.Cutter), ("Coupe différée", OutputMode.DelayedCut), ("Applicateur", OutputMode.Applicator),
        ]);
        p.Children.Add(_mode);

        p.Children.Add(Section("Support"));
        Chips<MediaKind?>(_media, "AsMedia", [("—", null), ("Étiquettes séparées (espace)", MediaKind.Gap), ("Marque noire", MediaKind.Mark), ("Continu", MediaKind.Continuous)]);
        p.Children.Add(_media);
        p.Children.Add(Field("Espace entre étiquettes ou hauteur de la marque (mm)", _gap, "ex. 3"));
        p.Children.Add(Field("Largeur d'impression (mm)", _width, "ex. 101.6"));
        p.Children.Add(Field("Longueur d'étiquette (mm)", _length, "ex. 152.4"));
        p.Children.Add(Field("Décalage horizontal (mm)", _left, "ex. 0"));
        p.Children.Add(Field("Décalage vertical (mm)", _top, "ex. 0"));
        p.Children.Add(Field("Position de détachage (mm)", _tear, "ex. 0"));

        p.Children.Add(Section("Réseau filaire"));
        Chips<bool?>(_network, "AsNet", [("—", null), ("Adresse automatique (DHCP)", true), ("Adresse fixe", false)]);
        p.Children.Add(_network);
        _staticIp.Children.Add(Field("Adresse IP", _ip, "ex. 192.168.1.50"));
        _staticIp.Children.Add(Field("Masque", _mask, "ex. 255.255.255.0"));
        _staticIp.Children.Add(Field("Passerelle", _gateway, "ex. 192.168.1.1"));
        _staticIp.Visibility = Visibility.Collapsed;
        p.Children.Add(_staticIp);

        _save.Style = (Style)FindResource("CheckLine");
        _save.Content = Localization.T("Enregistrer les réglages dans l'imprimante (conservés à l'extinction)");
        _save.Margin = new Thickness(0, 12, 0, 0);
        p.Children.Add(_save);

        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 8) };
        var load = new Button { Style = (Style)FindResource("BtnSecondary"), Content = Localization.T("Reprendre les réglages du fichier"), Margin = new Thickness(0, 0, 8, 0) };
        load.Click += (_, _) => LoadFromFile(quiet: false);
        var write = new Button { Style = (Style)FindResource("BtnAction"), Content = Localization.T("Écrire le fichier") };
        write.Click += (_, _) => WriteFromAssistant();
        buttons.Children.Add(load);
        buttons.Children.Add(write);
        p.Children.Add(buttons);
    }

    private static TextBlock Section(string title)
    {
        return new TextBlock { Text = Localization.T(title), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 2) };
    }

    private TextBlock AsLabel(string text)
    {
        var t = new TextBlock { Text = Localization.T(text), Margin = new Thickness(0, 8, 0, 0) };
        t.Style = (Style)FindResource("FieldLabel");
        return t;
    }

    private StackPanel Field(string label, TextBox box, string hint)
    {
        box.Style = (Style)FindResource("FieldInput");
        box.MaxWidth = 280;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.MinWidth = 200;
        box.ToolTip = Localization.T(hint);
        var panel = new StackPanel();
        panel.Children.Add(AsLabel(label));
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        grid.Children.Add(box);
        var hintText = new TextBlock { Text = Localization.T(hint), Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, FontSize = 13 };
        hintText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        hintText.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => hintText.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        grid.Children.Add(hintText);
        panel.Children.Add(grid);
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
            if (group == "AsNet")
            {
                chip.Checked += (_, _) => _staticIp.Visibility = Equals(value, false) ? Visibility.Visible : Visibility.Collapsed;
            }

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

    /// <summary>Pré-remplit le formulaire avec les réglages communs du fichier ouvert.</summary>
    private void LoadFromFile(bool quiet)
    {
        if (CodeBox.Text.Trim().Length == 0)
        {
            if (!quiet)
            {
                Log?.Invoke(LogKind.Warning, "Le fichier est vide : rien à reprendre.");
            }

            return;
        }

        var (s, _, _) = ConfigConverter.Extract(CodeBox.Text, CurrentDpi());
        _darkness.Text = s.Darkness is { } d ? N(Math.Round(d * 100)) : string.Empty;
        _speed.Text = N(s.SpeedIps);
        Select(_media, s.Media);
        _gap.Text = N(s.GapMm);
        Select(_method, s.Method);
        Select(_mode, s.Mode);
        _width.Text = N(s.WidthMm);
        _length.Text = N(s.LengthMm);
        _left.Text = N(s.LeftShiftMm);
        _top.Text = N(s.TopShiftMm);
        _tear.Text = N(s.TearOffMm);
        Select(_network, s.Dhcp);
        _ip.Text = s.IpAddress ?? string.Empty;
        _mask.Text = s.Netmask ?? string.Empty;
        _gateway.Text = s.Gateway ?? string.Empty;
        if (!quiet)
        {
            Log?.Invoke(LogKind.Info, s.IsEmpty ? "Aucun réglage commun dans ce fichier : formulaire laissé vide." : "Réglages du fichier repris dans l'assistant.");
        }
    }

    /// <summary>Nombre saisi (virgule ou point) ; vide : non réglé ; illisible : message et arrêt.</summary>
    private bool TryRead(TextBox box, string what, out double? value)
    {
        value = null;
        var t = box.Text.Trim().Replace(',', '.');
        if (t.Length == 0)
        {
            return true;
        }

        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
        {
            value = d;
            return true;
        }

        Log?.Invoke(LogKind.Warning, string.Format(CultureInfo.InvariantCulture, Localization.T("{0} : « {1} » n'est pas un nombre."), Localization.T(what), box.Text));
        box.Focus();
        return false;
    }

    private void WriteFromAssistant()
    {
        if (!TryRead(_darkness, "Noirceur", out var dark) || !TryRead(_speed, "Vitesse", out var speed) || !TryRead(_gap, "Espace", out var gap)
            || !TryRead(_width, "Largeur", out var width) || !TryRead(_length, "Longueur", out var length) || !TryRead(_left, "Décalage horizontal", out var left)
            || !TryRead(_top, "Décalage vertical", out var top) || !TryRead(_tear, "Position de détachage", out var tear))
        {
            return;
        }

        var dhcp = (bool?)Selected(_network);
        if (dhcp == false && !System.Net.IPAddress.TryParse(_ip.Text.Trim(), out _))
        {
            Log?.Invoke(LogKind.Warning, "Adresse fixe : saisis une adresse IP valide (ex. 192.168.1.50).");
            _ip.Focus();
            return;
        }

        var settings = new PrinterSettings
        {
            Darkness = dark is { } d ? Math.Clamp(d / 100, 0, 1) : null,
            SpeedIps = speed,
            Media = (MediaKind?)Selected(_media),
            GapMm = gap,
            Method = (PrintMethod?)Selected(_method),
            Mode = (OutputMode?)Selected(_mode),
            WidthMm = width,
            LengthMm = length,
            LeftShiftMm = left,
            TopShiftMm = top,
            TearOffMm = tear,
            Dhcp = dhcp,
            IpAddress = dhcp == false ? _ip.Text.Trim() : null,
            Netmask = dhcp == false ? _mask.Text.Trim() : null,
            Gateway = dhcp == false ? _gateway.Text.Trim() : null,
            Save = _save.IsChecked == true,
            Dpi = CurrentDpi(),
        };
        if (settings.IsEmpty)
        {
            Log?.Invoke(LogKind.Warning, "Aucune valeur saisie dans l'assistant : rien à écrire.");
            return;
        }

        var target = CurrentTarget();
        ApplyConversion(ConfigConverter.Write(settings, target), string.Format(CultureInfo.InvariantCulture, Localization.T("Écrit par l'assistant en {0}"), Localization.T(ConfigConverter.Name(target))));
    }

    /// <summary>Équivalent de la noirceur saisie dans l'échelle du langage choisi.</summary>
    private void UpdateDarknessHint()
    {
        if (!_assistantBuilt)
        {
            return;
        }

        if (!double.TryParse(_darkness.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
        {
            _darknessHint.Text = Localization.T("Vide : la noirceur n'est pas changée.");
            return;
        }

        var d = Math.Clamp(pct / 100, 0, 1);
        var written = CurrentTarget() switch
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
