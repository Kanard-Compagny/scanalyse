using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Scanalyse.Core.Printing;
using Scanalyse.Desktop.Printing;

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
    private readonly Dictionary<string, int?> _queueDpi = new(StringComparer.OrdinalIgnoreCase);
    private ConversionResult? _convertResult;
    private ConversionResult? _createResult;

    // ------------------------------------------------------------------ Parcours

    private void Mode_Changed(object sender, RoutedEventArgs e)
    {
        // Appelé pendant le chargement de la page (carte cochée d'office) : les autres éléments n'existent pas encore.
        if (ModeConvert is null || ModeCreate is null || EditorCard is null || CreatePreviewCard is null)
        {
            return;
        }

        _mode = ModeConvert.IsChecked == true ? PageMode.Convert : ModeCreate.IsChecked == true ? PageMode.Create : PageMode.Analyze;

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

    /// <summary>Résolution : celle choisie, sinon celle lue sur l'imprimante (Zebra), sinon celle du nom du modèle ou du pilote, sinon 203 dpi.</summary>
    private int CurrentDpi()
    {
        if (_dpi is { } chosen)
        {
            return chosen;
        }

        if (_printer is not null && _printer.TryGetValue("head.resolution.in_dpi", out var v)
            && double.TryParse(PrinterConfig.Unquote(v.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) && dpi is > 100 and < 1300)
        {
            return (int)dpi;
        }

        // Toutes marques : la résolution citée dans le nom du modèle ou du pilote (« ZT230-200dpi », « PM45 300 dpi »).
        var queue = TargetQueue.IsChecked == true ? SelectedQueue : null;
        if (queue is not null && !_queueDpi.ContainsKey(queue))
        {
            _queueDpi[queue] = PrinterProbe.DpiFromName(queue + " " + PrinterTransport.QueueDriver(queue)); // le pilote n'est lu qu'une fois
        }

        return PrinterProbe.DpiFromName(_profile?.Model) ?? (queue is null ? null : _queueDpi[queue]) ?? 203;
    }

    /// <summary>
    /// Langage conseillé : celui de l'imprimante reconnue (pour une Zebra, l'autre de ZPL / SGD que celui du fichier).
    /// Sans imprimante reconnue, aucun : un langage par défaut serait un choix au hasard qui peut induire en erreur.
    /// </summary>
    private ConfigLanguage? SuggestedTarget(bool converting)
    {
        if (_profile is not { } p || p.Brand == PrinterBrand.Unknown)
        {
            return null;
        }

        var languages = _report?.Languages ?? [];
        var target = ConfigConverter.ForBrand(p.Brand);
        return converting && target == ConfigLanguage.Zpl && languages.Contains("ZPL") ? ConfigLanguage.Sgd : target;
    }

    /// <summary>Le langage coché a été choisi par Scanalyse (d'après l'imprimante), pas par l'utilisateur.</summary>
    private bool _convertAuto;
    private bool _createAuto;

    /// <summary>Phrase qui dit d'où vient le langage coché, ou qu'il faut le choisir.</summary>
    private string TargetNote(bool auto, ConfigLanguage? chosen) =>
        auto && chosen is not null && _profile is { } p
            ? string.Format(CultureInfo.InvariantCulture, Localization.T("Choisi d'après l'imprimante sélectionnée ({0}) : change-le si le fichier est pour une autre imprimante."), p.DisplayName)
            : chosen is null
                ? Localization.T("Choisis le langage de l'imprimante qui recevra le fichier.")
                : string.Empty;

    private static string Brands(ConfigLanguage language) => language switch
    {
        ConfigLanguage.Zpl => "Zebra : commandes ^ et ~",
        ConfigLanguage.Sgd => "Zebra : ! U1 setvar, réglages enregistrés d'office",
        ConfigLanguage.Tspl => "TSC (et imprimantes compatibles TSPL)",
        ConfigLanguage.Epl2 => "Zebra anciennes (LP / TLP 2844…)",
        ConfigLanguage.Dpl => "Honeywell / Datamax (classe I, M, E…)",
        ConfigLanguage.Fingerprint => "Honeywell / Intermec (PC, PD, PM, PX…)",
        ConfigLanguage.Tpcl => "Toshiba TEC (B-EX, B-SX, BA400…)",
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
        ConfigLanguage.Tpcl => "TPCL",
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
        if (_convertTarget is null && SuggestedTarget(converting: true) is { } suggested)
        {
            _convertTarget = suggested;
            _convertAuto = true;
        }

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
                _convertAuto = _convertAuto && language == _convertTarget;
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
            : string.Format(CultureInfo.InvariantCulture, Localization.T("Fichier de départ : {0}."), source ?? Localization.T("langage non reconnu"))
                + (TargetNote(_convertAuto, _convertTarget) is { Length: > 0 } note ? " " + note : string.Empty);
        if (_convertTarget is not { } target || CodeBox.Text.Trim().Length == 0)
        {
            _convertResult = null;
            ConvertResult.Visibility = Visibility.Collapsed;
            return;
        }

        var result = ConfigConverter.Convert(CodeBox.Text, target, CurrentDpi());
        _convertResult = result;
        ConvertResult.Visibility = Visibility.Visible;
        ShowCode(ConvertCode, result.Settings.IsEmpty ? string.Empty : result.Text, "Aucun réglage commun dans le fichier de départ : rien à convertir.");
        ConvertSummary.Text = Summary(result);
        Notes(ConvertNotes, result, showLines: true);
    }

    /// <summary>Fichier en lecture seule, coloré comme l'éditeur (commande inconnue en rouge) ; message si vide.</summary>
    private void ShowCode(TextBlock target, string text, string emptyMessage)
    {
        target.Inlines.Clear();
        if (text.Length == 0)
        {
            var empty = new Run(Localization.T(emptyMessage)) { FontFamily = (FontFamily)FindResource("UiFont") };
            empty.SetResourceReference(TextElement.ForegroundProperty, "Muted");
            target.Inlines.Add(empty);
            return;
        }

        foreach (var t in PrinterConfig.Tokenize(text))
        {
            var run = new Run(text.Substring(t.Start, t.Length));
            run.SetResourceReference(TextElement.ForegroundProperty, ColorKey(t.Kind));
            target.Inlines.Add(run);
        }
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

    private ConfigLanguage? CurrentResultLanguage => _mode == PageMode.Convert ? _convertTarget : _createTarget;

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
    private readonly WrapPanel _wifi = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _wifiPanel = new();
    private readonly TextBox _ssid = new();
    private readonly WrapPanel _wifiSecurity = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _passPanel = new();
    private readonly TextBox _pass = new();
    private readonly TextBlock _passHint = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel _country = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly WrapPanel _wifiNet = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly StackPanel _wifiStatic = new();
    private readonly TextBox _wifiIp = new();
    private readonly TextBox _wifiMask = new();
    private readonly TextBox _wifiGateway = new();
    private readonly CheckBox _restart = new();

    private void ShowCreate()
    {
        if (!_assistantBuilt)
        {
            BuildAssistant();
            _assistantBuilt = true;
        }

        if (_createTarget is null && SuggestedTarget(converting: false) is { } suggested)
        {
            _createTarget = suggested;
            _createAuto = true;
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
                IsChecked = language == _createTarget,
                Margin = new Thickness(0, 0, 6, 6),
            };
            chip.Checked += (_, _) =>
            {
                _createAuto = _createAuto && language == _createTarget;
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
        var p = Group("Impression");
        p.Children.Add(Field("Noirceur (0 à 100 %)", _darkness, "ex. 60"));
        _darknessHint.Margin = new Thickness(0, -4, 0, 8);
        p.Children.Add(_darknessHint);
        _darknessHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        p.Children.Add(Field("Vitesse (pouces par seconde ; 1 pouce/s ≈ 25 mm/s)", _speed, "ex. 4"));
        Chips<PrintMethod?>(_method, "AsMethod", [("Ne pas changer", null), ("Transfert thermique (avec ruban)", PrintMethod.ThermalTransfer), ("Thermique direct (sans ruban)", PrintMethod.DirectThermal)]);
        p.Children.Add(Choice("Méthode d'impression", _method));
        Chips<OutputMode?>(_modeChips, "AsMode",
        [
            ("Ne pas changer", null), ("Détachage", OutputMode.TearOff), ("Prédécollage", OutputMode.PeelOff), ("Enroulement", OutputMode.Rewind),
            ("Coupe", OutputMode.Cutter), ("Coupe différée", OutputMode.DelayedCut), ("Applicateur", OutputMode.Applicator),
        ]);
        p.Children.Add(Choice("Sortie des étiquettes", _modeChips));

        p = Group("Étiquettes");
        Chips<MediaKind?>(_media, "AsMedia", [("Ne pas changer", null), ("Étiquettes séparées par un espace", MediaKind.Gap), ("Marque noire au dos", MediaKind.Mark), ("Continu", MediaKind.Continuous)]);
        p.Children.Add(Choice("Type de support", _media));
        p.Children.Add(Field("Espace entre étiquettes, ou hauteur de la marque (mm)", _gap, "ex. 3"));
        p.Children.Add(Field("Largeur d'impression (mm)", _width, "ex. 100"));
        p.Children.Add(Field("Longueur d'étiquette (mm)", _length, "ex. 150"));

        p = Group("Réglages fins");
        p.Children.Add(Field("Décalage horizontal (mm)", _left, "ex. 0"));
        p.Children.Add(Field("Décalage vertical (mm)", _top, "ex. 0"));
        p.Children.Add(Field("Position de détachage (mm)", _tear, "ex. 0"));

        p = Group("Réseau filaire");
        Chips<bool?>(_network, "AsNet", [("Ne pas changer", null), ("Adresse automatique (DHCP)", true), ("Adresse fixe", false)]);
        p.Children.Add(Choice("Adresse IP", _network));
        _staticIp.Children.Add(Field("Adresse IP", _ip, "ex. 192.168.1.50"));
        _staticIp.Children.Add(Field("Masque", _mask, "ex. 255.255.255.0"));
        _staticIp.Children.Add(Field("Passerelle", _gateway, "ex. 192.168.1.1"));
        _staticIp.Visibility = Visibility.Collapsed;
        p.Children.Add(_staticIp);

        p = Group("Réseau Wi-Fi", "Pratique pour préparer en USB une imprimante qui passera ensuite en Wi-Fi : choisis « Installée sur ce PC » et envoie ce fichier.");
        Chips<bool?>(_wifi, "AsWifi", [("Ne pas changer", null), ("Configurer le Wi-Fi", true)]);
        p.Children.Add(Choice("Wi-Fi", _wifi));
        _wifiPanel.Children.Add(Field("Nom du réseau (SSID, 32 caractères au plus)", _ssid, "ex. Entrepot"));
        Chips<WifiSecurity?>(_wifiSecurity, "AsWifiSec", [("WPA / WPA2 personnel (mot de passe)", WifiSecurity.WpaPersonal), ("Réseau ouvert (sans mot de passe)", WifiSecurity.Open)]);
        Select(_wifiSecurity, WifiSecurity.WpaPersonal);
        _wifiPanel.Children.Add(Choice("Sécurité", _wifiSecurity));
        _passPanel.Children.Add(Field("Mot de passe du Wi-Fi", _pass, "8 à 63 caractères"));
        _passHint.Margin = new Thickness(0, -4, 0, 8);
        _passHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _passPanel.Children.Add(_passHint);
        _wifiPanel.Children.Add(_passPanel);
        Chips<string?>(_country, "AsCountry", [("Ne pas changer", null), .. ConfigConverter.WifiCountries.Select(c => (c.Label, (string?)c.Value))]);
        _wifiPanel.Children.Add(Choice("Pays (canaux Wi-Fi autorisés)", _country));
        Chips<bool?>(_wifiNet, "AsWifiNet", [("Ne pas changer", null), ("Adresse automatique (DHCP)", true), ("Adresse fixe", false)]);
        _wifiPanel.Children.Add(Choice("Adresse IP en Wi-Fi", _wifiNet));
        _wifiStatic.Children.Add(Field("Adresse IP", _wifiIp, "ex. 192.168.1.60"));
        _wifiStatic.Children.Add(Field("Masque", _wifiMask, "ex. 255.255.255.0"));
        _wifiStatic.Children.Add(Field("Passerelle", _wifiGateway, "ex. 192.168.1.1"));
        _wifiStatic.Visibility = Visibility.Collapsed;
        _wifiPanel.Children.Add(_wifiStatic);
        _wifiPanel.Visibility = Visibility.Collapsed;
        p.Children.Add(_wifiPanel);

        p = Group("À la fin du fichier");
        _restart.Style = (Style)FindResource("CheckLine");
        _restart.Content = Localization.T("Redémarrer l'imprimante à la fin (applique le réseau et le Wi-Fi)");
        _restart.Margin = new Thickness(0, 8, 0, 4);
        _restart.Checked += (_, _) => RefreshCreate();
        _restart.Unchecked += (_, _) => RefreshCreate();
        p.Children.Add(_restart);

        _save.Style = (Style)FindResource("CheckLine");
        _save.Content = Localization.T("Enregistrer les réglages dans l'imprimante (conservés à l'extinction)");
        _save.Margin = new Thickness(0, 8, 0, 4);
        _save.Checked += (_, _) => RefreshCreate();
        _save.Unchecked += (_, _) => RefreshCreate();
        p.Children.Add(_save);
    }

    /// <summary>
    /// Bloc du formulaire, présenté comme le plan de travail : titre en capitales, puis des lignes nettes (libellé à gauche,
    /// champ à droite ; ou libellé puis choix). Renvoie le conteneur des lignes.
    /// </summary>
    private StackPanel Group(string title, string? intro = null)
    {
        var rows = new StackPanel();
        var head = new TextBlock { Text = Localization.T(title).ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        var panel = new StackPanel();
        panel.Children.Add(head);
        if (intro is not null)
        {
            var t = new TextBlock { Text = Localization.T(intro), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(t);
        }

        panel.Children.Add(rows);
        var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 6), BorderThickness = new Thickness(1), Margin = new Thickness(0, AssistantPanel.Children.Count == 0 ? 4 : 12, 0, 0), Child = panel };
        card.SetResourceReference(Border.BackgroundProperty, "Surface2");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        AssistantPanel.Children.Add(card);
        return rows;
    }

    /// <summary>Libellé et précision (le texte entre parenthèses) pour la colonne de gauche d'une ligne.</summary>
    private static StackPanel RowLabel(string label)
    {
        var text = Localization.T(label);
        var open = text.IndexOf(" (", StringComparison.Ordinal);
        var main = open > 0 && text.EndsWith(')') ? text[..open] : text;
        var detail = open > 0 && text.EndsWith(')') ? text[(open + 2)..^1] : null;
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var m = new TextBlock { Text = main, FontSize = 13, TextWrapping = TextWrapping.Wrap };
        m.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        panel.Children.Add(m);
        if (detail is not null)
        {
            var d = new TextBlock { Text = detail, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            d.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            panel.Children.Add(d);
        }

        return panel;
    }

    private static Border RowBorder(UIElement child)
    {
        var b = new Border { Padding = new Thickness(0, 8, 0, 8), BorderThickness = new Thickness(0, 0, 0, 1), Child = child };
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        return b;
    }

    /// <summary>Une ligne « libellé — champ » : le champ, de hauteur fixe, est aligné à droite.</summary>
    private Border Field(string label, TextBox box, string hint)
    {
        box.Style = (Style)FindResource("FieldInput");
        box.Width = 200;
        box.Height = 34;
        box.Padding = new Thickness(8, 0, 8, 0);
        box.HorizontalAlignment = HorizontalAlignment.Right;
        box.VerticalAlignment = VerticalAlignment.Center;
        box.Margin = new Thickness(0);
        Placeholder.SetText(box, Localization.T(hint));
        box.TextChanged += (_, _) => RefreshCreate();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(RowLabel(label));
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return RowBorder(grid);
    }

    /// <summary>Une ligne « libellé, puis les choix » (puces).</summary>
    private static Border Choice(string label, WrapPanel chips)
    {
        var panel = new StackPanel();
        panel.Children.Add(RowLabel(label));
        chips.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(chips);
        return RowBorder(panel);
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
                switch (group)
                {
                    case "AsNet":
                        _staticIp.Visibility = Equals(value, false) ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "AsWifi":
                        _wifiPanel.Visibility = Equals(value, true) ? Visibility.Visible : Visibility.Collapsed;
                        break;
                    case "AsWifiSec":
                        _passPanel.Visibility = Equals(value, WifiSecurity.Open) ? Visibility.Collapsed : Visibility.Visible;
                        break;
                    case "AsWifiNet":
                        _wifiStatic.Visibility = Equals(value, false) ? Visibility.Visible : Visibility.Collapsed;
                        break;
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
            Select(_wifi, s.HasWifi ? true : null);
            _ssid.Text = s.WifiSsid ?? string.Empty;
            Select(_wifiSecurity, s.WifiSecurity ?? WifiSecurity.WpaPersonal);
            _pass.Text = s.WifiPassphrase ?? string.Empty;
            Select(_country, s.WifiCountry);
            Select(_wifiNet, s.WifiDhcp);
            _wifiIp.Text = s.WifiIpAddress ?? string.Empty;
            _wifiMask.Text = s.WifiNetmask ?? string.Empty;
            _wifiGateway.Text = s.WifiGateway ?? string.Empty;
            _restart.IsChecked = s.Restart;
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
    /// <summary>Réglages repris par « Partir des réglages de l'imprimante » (jamais le réseau : le fichier peut servir à une autre imprimante).</summary>
    private static readonly string[] FormKeys =
        ["print.tone", "media.speed", "media.type", "ezpl.media_type", "ezpl.print_method", "media.printmode", "ezpl.print_width", "print.width", "zpl.label_length", "zpl.left_position", "ezpl.tear_off"];

    private async void FromPrinter_Click(object sender, RoutedEventArgs e)
    {
        if (_printer is null)
        {
            return;
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in FormKeys)
        {
            if (_printer.TryGetValue(key, out var v) && PrinterConfig.Unquote(v.Value) is { Length: > 0 } value)
            {
                values[key] = value;
            }
        }

        // Ce que la lecture complète n'a pas donné, on le demande réglage par réglage (getvar), si l'imprimante est en réseau.
        var missing = FormKeys.Where(k => !values.ContainsKey(k)).ToList();
        if (missing.Count > 0 && NetworkTarget() is { } target)
        {
            FromPrinterButton.IsEnabled = false;
            try
            {
                foreach (var (key, value) in await PrinterProbe.GetVarsAsync(target, missing, CancellationToken.None))
                {
                    values[key] = value;
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or OperationCanceledException or System.Net.Sockets.SocketException)
            {
                Log?.Invoke(LogKind.Warning, "Lecture complémentaire de l'imprimante impossible : " + ex.Message);
            }
            finally
            {
                FromPrinterButton.IsEnabled = true;
            }
        }

        var text = new StringBuilder();
        foreach (var (key, value) in values)
        {
            text.Append(CultureInfo.InvariantCulture, $"! U1 setvar \"{key}\" \"{value}\"\r\n");
        }

        var (s, _, _) = ConfigConverter.Extract(text.ToString(), CurrentDpi());
        FillForm(s);
        var found = new List<string>();
        if (s.Darkness is not null) { found.Add(Localization.T("noirceur")); }
        if (s.SpeedIps is not null) { found.Add(Localization.T("vitesse")); }
        if (s.Method is not null) { found.Add(Localization.T("méthode")); }
        if (s.Mode is not null) { found.Add(Localization.T("sortie")); }
        if (s.Media is not null) { found.Add(Localization.T("support")); }
        if (s.WidthMm is not null) { found.Add(Localization.T("largeur")); }
        if (s.LengthMm is not null) { found.Add(Localization.T("longueur")); }
        Log?.Invoke(found.Count >= 5 ? LogKind.Info : LogKind.Warning, found.Count == 0
            ? "Aucun réglage du formulaire n'a pu être lu sur cette imprimante."
            : $"Réglages de l'imprimante repris dans le formulaire ({string.Join(", ", found)}) : change ce qui doit l'être. Non trouvés sur l'imprimante : {string.Join(", ", FormKeys.Where(k => !values.ContainsKey(k)).DefaultIfEmpty("aucun"))}.");
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
        var wifi = Equals(Selected(_wifi), true);
        var security = (WifiSecurity?)Selected(_wifiSecurity) ?? WifiSecurity.WpaPersonal;
        var wifiDhcp = wifi ? (bool?)Selected(_wifiNet) : null;
        var pass = _pass.Text.Trim();
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
            WifiSsid = wifi && _ssid.Text.Trim().Length > 0 ? _ssid.Text.Trim() : null,
            WifiSecurity = wifi ? security : null,
            WifiPassphrase = wifi && security == WifiSecurity.WpaPersonal && pass.Length > 0 ? pass : null,
            WifiCountry = wifi ? (string?)Selected(_country) : null,
            WifiDhcp = wifiDhcp,
            WifiIpAddress = wifiDhcp == false ? _wifiIp.Text.Trim() : null,
            WifiNetmask = wifiDhcp == false ? _wifiMask.Text.Trim() : null,
            WifiGateway = wifiDhcp == false ? _wifiGateway.Text.Trim() : null,
            Restart = _restart.IsChecked == true,
            Save = _save.IsChecked == true,
            Dpi = CurrentDpi(),
        };
        if (dhcp == false && !System.Net.IPAddress.TryParse(_ip.Text.Trim(), out _))
        {
            invalid.Add(Localization.T("Adresse IP"));
        }

        if (wifi)
        {
            var ssid = _ssid.Text.Trim();
            if (ssid.Length is 0 or > 32)
            {
                invalid.Add(Localization.T("Nom du réseau Wi-Fi (1 à 32 caractères)"));
            }

            if (pass.Length > 0 && security == WifiSecurity.WpaPersonal && !ConfigConverter.IsValidPassphrase(pass))
            {
                invalid.Add(Localization.T("Mot de passe Wi-Fi (8 à 63 caractères)"));
            }

            if (wifiDhcp == false && !System.Net.IPAddress.TryParse(_wifiIp.Text.Trim(), out _))
            {
                invalid.Add(Localization.T("Adresse IP du Wi-Fi"));
            }
        }

        CreateTargetNote.Text = TargetNote(_createAuto, _createTarget);
        CreateTargetNote.Visibility = CreateTargetNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_createTarget is not { } target)
        {
            _createResult = null;
            ShowCode(CreateCode, string.Empty, "Choisis d'abord, ci-dessus, le langage de l'imprimante qui recevra le fichier : il s'écrit ici.");
            CreateSummary.Text = string.Empty;
            CreateNotes.Children.Clear();
            return;
        }

        UpdateDarknessHint(settings.Darkness, target);
        _passHint.Text = Localization.T(pass.Length == 0
            ? "Vide : le mot de passe déjà enregistré dans l'imprimante est gardé."
            : target is ConfigLanguage.Zpl or ConfigLanguage.Sgd
                ? "Zebra : le mot de passe est transformé en clé calculée avec le nom du réseau ; il n'apparaît pas en clair dans le fichier."
                : "Le mot de passe est écrit en clair dans le fichier : ne le diffuse pas et supprime-le après usage.");
        if (settings.IsEmpty)
        {
            _createResult = null;
            ShowCode(CreateCode, string.Empty, "Renseigne au moins un réglage à gauche : le fichier s'écrit ici.");
            CreateSummary.Text = string.Empty;
            CreateNotes.Children.Clear();
            return;
        }

        var result = ConfigConverter.Write(settings, target);
        _createResult = invalid.Count > 0 ? null : result;
        ShowCode(CreateCode, result.Text, string.Empty);
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
            ConfigLanguage.Tpcl => $"{{AY;{Math.Round((d * 20) - 10):+00;-00;+00},…|}} (-10 à +10)",
            _ => $"<ESC>#E{Math.Round((d * 4) + 1)} (1 à 5)",
        };
        _darknessHint.Text = string.Format(CultureInfo.InvariantCulture, Localization.T("Sera écrit : {0}"), written);
    }
}
