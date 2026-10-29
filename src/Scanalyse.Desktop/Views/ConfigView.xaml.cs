using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Scanalyse.Core.Content;
using Scanalyse.Core.Printing;
using Scanalyse.Desktop.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Conf imprimante : fichier de configuration (ZPL, SGD, TSPL, DPL, Fingerprint, SBPL, EPL2) expliqué ligne par ligne et
/// contrôlé ; comparaison avec les réglages lus sur l'imprimante (Zebra, « ! U1 getvar "allcv" ») ; envoi après
/// confirmation quand le fichier contient des erreurs ou des commandes risquées (redémarrage, usine, réseau).
/// </summary>
public partial class ConfigView : UserControl
{
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private ConfigReport? _report;
    private IReadOnlyDictionary<string, PrinterValue>? _printer;
    private IReadOnlyList<ConfigFinding> _comparison = [];
    private string? _printerName;
    private byte[]? _fileBytes;
    private string? _fileText;
    private bool _loading;

    public ConfigView()
    {
        InitializeComponent();
        ScrollChaining.Enable(PageScroll);
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Analyze();
        };
        Loaded += (_, _) => LoadTarget();
    }

    /// <summary>Message pour le journal.</summary>
    public event Action<LogKind, string>? Log;

    // ------------------------------------------------------------------ Imprimante

    private void LoadTarget()
    {
        _loading = true;
        var p = AppSettings.Load().Print;
        AddressBox.Text = p.Address;
        QueueBox.ItemsSource = PrinterTransport.Queues();
        QueueBox.SelectedItem = QueueBox.Items.Cast<string>().FirstOrDefault(q => q == p.Queue);
        (p.Mode == "queue" ? TargetQueue : TargetTcp).IsChecked = true;
        ShowTarget();
        _loading = false;
    }

    private void ShowTarget()
    {
        var queue = TargetQueue.IsChecked == true;
        AddressBox.Visibility = queue ? Visibility.Collapsed : Visibility.Visible;
        QueueBox.Visibility = queue ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Target_Changed(object sender, RoutedEventArgs e)
    {
        ShowTarget();
        SaveTarget();
    }

    private void Address_LostFocus(object sender, RoutedEventArgs e) => SaveTarget();

    private void Queue_Changed(object sender, SelectionChangedEventArgs e) => SaveTarget();

    /// <summary>Même imprimante que « Code imprimante » : le choix est partagé.</summary>
    private void SaveTarget()
    {
        if (_loading || !IsLoaded)
        {
            return;
        }

        var s = AppSettings.Load();
        s.Print.Mode = TargetQueue.IsChecked == true ? "queue" : "tcp";
        s.Print.Address = AddressBox.Text.Trim();
        if (QueueBox.SelectedItem is string q)
        {
            s.Print.Queue = q;
        }

        s.Save();
    }

    /// <summary>Adresse réseau à interroger : l'IP saisie, ou celle derrière l'imprimante installée (port TCP/IP standard).</summary>
    private string? NetworkTarget()
    {
        if (TargetQueue.IsChecked == true)
        {
            return QueueBox.SelectedItem is string q ? PrinterTransport.QueueAddress(q) : null;
        }

        var a = AddressBox.Text.Trim();
        return a.Length > 0 ? a : null;
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        var target = NetworkTarget();
        if (target is null)
        {
            Log?.Invoke(LogKind.Warning, TargetQueue.IsChecked == true
                ? "Cette imprimante installée n'a pas d'adresse réseau (USB, partage) : la lecture des réglages passe par le réseau. Choisis « Réseau » et saisis son adresse IP."
                : "Saisis l'adresse IP de l'imprimante pour lire ses réglages.");
            return;
        }

        ReadButton.IsEnabled = false;
        ReadButton.Content = Localization.T("Lecture…");
        try
        {
            var reply = await PrinterTransport.QueryTcpAsync(target, PrinterConfig.AllcvRequest, maxBytes: 2 * 1024 * 1024, firstWaitMs: 6000, quietMs: 1500);
            var values = PrinterConfig.ParseAllcv(Encoding.UTF8.GetString(reply));
            if (values.Count == 0)
            {
                _printer = null;
                PrinterBanner.Visibility = Visibility.Visible;
                PrinterText.Text = Localization.T("L'imprimante n'a pas renvoyé ses réglages SGD : ce n'est pas une Zebra Link-OS, ou la commande getvar est désactivée. L'analyse du fichier reste valable ; la comparaison avec l'imprimante n'est possible que pour les Zebra.");
                Log?.Invoke(LogKind.Warning, $"Aucun réglage SGD reçu de {target}.");
                return;
            }

            _printer = values;
            _printerName = values.TryGetValue("device.product_name", out var product) ? product.Value
                : values.TryGetValue("appl.name", out var fw) ? "firmware " + fw.Value : target;
            Log?.Invoke(LogKind.Ok, $"{values.Count} réglages lus sur {_printerName} ({target}).");
            Analyze();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            Log?.Invoke(LogKind.Error, ex.Message);
        }
        finally
        {
            ReadButton.IsEnabled = true;
            ReadButton.Content = Localization.T("Lire l'imprimante");
        }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        Analyze();
        if (_report is not { } r || CodeBox.Text.Trim().Length == 0)
        {
            Log?.Invoke(LogKind.Warning, "Rien à envoyer : colle ou ouvre d'abord un fichier de configuration.");
            return;
        }

        var errors = r.Findings.Count(f => f.Severity == Severity.Error) + _comparison.Count(f => f.Severity == Severity.Error);
        var notes = new List<string>();
        if (errors > 0)
        {
            notes.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Le fichier contient {0} erreur(s) : l'imprimante ignorera ou refusera ces lignes."), errors));
        }

        if (r.Risky)
        {
            notes.Add(Localization.T("Il redémarre l'imprimante, la remet en usine ou change ses réglages réseau : elle peut devenir injoignable quelques secondes, ou à une autre adresse."));
        }

        if (notes.Count > 0 && !await Dialogs.ConfirmAsync(Localization.T("Envoyer quand même ?"), string.Join(Environment.NewLine + Environment.NewLine, notes), Localization.T("Envoyer"), null, danger: errors > 0))
        {
            return;
        }

        var bytes = Payload(out var fixedNewline);
        try
        {
            if (TargetQueue.IsChecked == true)
            {
                if (QueueBox.SelectedItem is not string queue)
                {
                    Log?.Invoke(LogKind.Warning, "Choisis l'imprimante installée sur ce PC.");
                    return;
                }

                PrinterTransport.SendToQueue(queue, bytes, "Scanalyse - configuration");
                Log?.Invoke(LogKind.Ok, $"Configuration envoyée à {queue} ({bytes.Length} octets).");
            }
            else
            {
                var target = NetworkTarget();
                if (target is null)
                {
                    Log?.Invoke(LogKind.Warning, "Saisis l'adresse IP de l'imprimante.");
                    return;
                }

                await PrinterTransport.SendTcpAsync(target, bytes);
                Log?.Invoke(LogKind.Ok, $"Configuration envoyée à {target} ({bytes.Length} octets).");
            }

            if (fixedNewline)
            {
                Log?.Invoke(LogKind.Info, "Retour à la ligne ajouté après la dernière commande SGD, pour qu'elle soit exécutée.");
            }

            if (_printer is not null)
            {
                Log?.Invoke(LogKind.Info, "Relis l'imprimante pour vérifier que les réglages ont bien été pris en compte.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            Log?.Invoke(LogKind.Error, ex.Message);
        }
    }

    /// <summary>Octets envoyés : ceux du fichier ouvert s'il n'a pas été retouché, sinon le texte en UTF-8 ; une ligne SGD finale reçoit son retour à la ligne.</summary>
    private byte[] Payload(out bool fixedNewline)
    {
        var text = CodeBox.Text;
        var last = text.TrimEnd().Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
        fixedNewline = (last.StartsWith("! U", StringComparison.Ordinal) || last.StartsWith("{}{", StringComparison.Ordinal)) && !text.EndsWith('\n');
        if (_fileBytes is not null && text == _fileText && !fixedNewline)
        {
            return _fileBytes;
        }

        return new UTF8Encoding(false).GetBytes(fixedNewline ? text + "\r\n" : text);
    }

    // ------------------------------------------------------------------ Fichier

    /// <summary>Ouvre un fichier de configuration (bouton, glisser-déposer, « Ouvrir avec »).</summary>
    public void OpenFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length > 2 * 1024 * 1024)
            {
                Log?.Invoke(LogKind.Warning, "Fichier trop gros pour une configuration (plus de 2 Mo).");
                return;
            }

            var (text, encoding) = ProgramFile.Decode(bytes);
            text = text.TrimStart('﻿').Replace("\0", string.Empty, StringComparison.Ordinal);
            _fileBytes = bytes;
            _fileText = text;
            CodeBox.Text = text;
            FileInfo.Text = $"{Path.GetFileName(path)} · {bytes.Length} octets · {encoding}";
            Analyze();
            Log?.Invoke(LogKind.Info, $"Configuration ouverte : {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke(LogKind.Error, "Lecture du fichier impossible : " + ex.Message);
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Localization.T("Ouvrir un fichier de configuration d'imprimante"),
            Filter = Localization.T("Fichiers de configuration (*.txt, *.zpl, *.prn, *.nrd, *.cfg, *.json…)|*.txt;*.zpl;*.prn;*.nrd;*.cfg;*.json;*.tspl;*.dpl;*.epl;*.sbpl|Tous les fichiers|*.*"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            OpenFile(dialog.FileName);
        }
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (Clipboard.ContainsText())
            {
                _fileBytes = null;
                CodeBox.Text = Clipboard.GetText();
                FileInfo.Text = Localization.T("Collé depuis le presse-papiers");
                Analyze();
            }
        }
        catch (System.Runtime.InteropServices.COMException)
        {
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _fileBytes = null;
        CodeBox.Clear();
        FileInfo.Text = string.Empty;
        Analyze();
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

    private void CodeBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        CodeHint.Visibility = CodeBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>Sélectionne une ligne du fichier (clic sur « ligne n » dans l'analyse).</summary>
    private void GoToLine(int line)
    {
        if (line <= 0 || line > CodeBox.LineCount)
        {
            return;
        }

        var start = CodeBox.GetCharacterIndexFromLineIndex(line - 1);
        var length = CodeBox.GetLineLength(line - 1);
        CodeBox.Focus();
        CodeBox.Select(start, Math.Max(0, length - (CodeBox.GetLineText(line - 1).EndsWith('\n') ? 1 : 0)));
        CodeBox.ScrollToLine(line - 1);
    }

    // ------------------------------------------------------------------ Analyse

    private void Analyze()
    {
        var text = CodeBox.Text;
        if (text.Trim().Length == 0)
        {
            _report = null;
            _comparison = [];
            ResultTitle.Text = Localization.T("Analyse");
            ResultSummary.Text = Localization.T("Le fichier est analysé dès qu'il est collé ou ouvert.");
            RiskBanner.Visibility = Visibility.Collapsed;
            ShowPrinterBanner();
            ResultPanel.Children.Clear();
            return;
        }

        var r = PrinterConfig.Analyze(text);
        _report = r;
        _comparison = _printer is null ? [] : PrinterConfig.Compare(r, _printer);
        var errors = r.Findings.Count(f => f.Severity == Severity.Error) + _comparison.Count(f => f.Severity == Severity.Error);
        var warnings = r.Findings.Count(f => f.Severity == Severity.Warning) + _comparison.Count(f => f.Severity == Severity.Warning);
        ResultTitle.Text = Localization.T(errors > 0 ? "Erreurs à corriger" : warnings > 0 ? "À vérifier" : "Aucune erreur trouvée");
        ResultTitle.SetResourceReference(TextBlock.ForegroundProperty, errors > 0 ? "BadFg" : warnings > 0 ? "WarnFg" : "OkFg");
        var languages = r.Languages.Count == 0 ? Localization.T("langage non reconnu") : string.Join(", ", r.Languages);
        ResultSummary.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("{0} · {1} commande(s) · {2} erreur(s), {3} point(s) à vérifier"),
            languages, r.Items.Count, errors, warnings);
        RiskBanner.Visibility = r.Risky ? Visibility.Visible : Visibility.Collapsed;
        RiskText.Text = Localization.T("Ce fichier redémarre l'imprimante, la remet en usine ou change ses réglages réseau : une confirmation sera demandée avant l'envoi.");
        ShowPrinterBanner();
        Render();
    }

    private void ShowPrinterBanner()
    {
        if (_printer is null)
        {
            return;
        }

        PrinterBanner.Visibility = Visibility.Visible;
        var changes = _comparison.Count(f => f.Severity == Severity.Info);
        var same = _comparison.Count(f => f.Severity == Severity.Ok);
        PrinterText.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Localization.T("Comparé à {0} ({1} réglages lus) : {2} réglage(s) changeront, {3} déjà en place. Les valeurs sont contrôlées avec les choix annoncés par cette imprimante."),
            _printerName, _printer.Count, changes, same);
    }

    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            Render();
        }
    }

    private void Render()
    {
        ResultPanel.Children.Clear();
        if (_report is not { } r)
        {
            return;
        }

        if (ViewLines.IsChecked == true)
        {
            foreach (var group in r.Items.GroupBy(i => i.Line))
            {
                var problems = r.Findings.Where(f => f.Line == group.Key).Concat(_comparison.Where(f => f.Line == group.Key)).ToList();
                ResultPanel.Children.Add(LineCard(group.Key, group.ToList(), problems));
            }

            return;
        }

        if (ViewSettings.IsChecked == true)
        {
            if (r.Settings.Count == 0)
            {
                ResultPanel.Children.Add(Muted(Localization.T("Ce fichier ne règle aucune valeur.")));
            }

            foreach (var s in r.Settings)
            {
                var onPrinter = _printer is not null && _printer.TryGetValue(s.Key, out var pv) ? pv.Value : null;
                ResultPanel.Children.Add(SettingRow(s, onPrinter));
            }

            return;
        }

        var all = r.Findings.Concat(_comparison).Where(f => f.Severity != Severity.Ok)
            .OrderByDescending(f => f.Severity).ThenBy(f => f.Line).ToList();
        if (all.Count == 0)
        {
            ResultPanel.Children.Add(Finding(new ConfigFinding(0, Severity.Ok, Localization.T("Aucune erreur trouvée. Consulte « Ligne par ligne » pour voir ce que fait chaque commande."))));
        }

        foreach (var f in all)
        {
            ResultPanel.Children.Add(Finding(f));
        }
    }

    private Border Finding(ConfigFinding f)
    {
        var (bg, fg, label) = f.Severity switch
        {
            Severity.Error => ("BadBg", "BadFg", "Erreur"),
            Severity.Warning => ("WarnBg", "WarnFg", "À vérifier"),
            Severity.Ok => ("OkBg", "OkFg", "OK"),
            _ => ("InfoBg", "InfoFg", "Info"),
        };
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        text.SetResourceReference(TextBlock.ForegroundProperty, fg);
        text.Inlines.Add(new Run(Localization.T(label) + (f.Line > 0 ? " · " + string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("ligne {0}"), f.Line) : string.Empty) + " : ") { FontWeight = FontWeights.SemiBold });
        text.Inlines.Add(new Run(Localization.T(f.Message)));
        var border = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 6), Child = text };
        border.SetResourceReference(Border.BackgroundProperty, bg);
        if (f.Line > 0)
        {
            border.Cursor = Cursors.Hand;
            border.ToolTip = Localization.T("Clique pour voir la ligne dans le fichier");
            border.MouseLeftButtonUp += (_, _) => GoToLine(f.Line);
        }

        return border;
    }

    private Border LineCard(int line, IReadOnlyList<ConfigItem> items, IReadOnlyList<ConfigFinding> problems)
    {
        var panel = new StackPanel();
        var head = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 4) };
        head.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        head.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Ligne {0}"), line) + " · " + string.Join(", ", items.Select(i => i.Language).Distinct());
        panel.Children.Add(head);
        foreach (var i in items)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
            var category = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            category.SetResourceReference(Border.BackgroundProperty, "Surface2");
            var catText = new TextBlock { Text = Localization.T(i.Category), FontSize = 11 };
            catText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            category.Child = catText;
            DockPanel.SetDock(category, Dock.Right);
            row.Children.Add(category);
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap };
            t.Inlines.Add(new Run(i.Command.Replace("\u0002", "<STX>", StringComparison.Ordinal).Replace("\u0001", "<SOH>", StringComparison.Ordinal)) { FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5 });
            t.Inlines.Add(new LineBreak());
            t.Inlines.Add(new Run(Localization.T(i.Meaning)) { FontSize = 13 });
            t.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            row.Children.Add(t);
            panel.Children.Add(row);
        }

        foreach (var f in problems)
        {
            panel.Children.Add(Finding(f));
        }

        var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8), BorderThickness = new Thickness(1), Child = panel, Cursor = Cursors.Hand };
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        card.MouseLeftButtonUp += (_, _) => GoToLine(line);
        return card;
    }

    private Border SettingRow(ConfigSetting s, string? onPrinter)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = Localization.T(s.Label), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var key = new TextBlock { Text = s.Key + " · " + string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("ligne {0}"), s.Line), FontSize = 11.5, FontFamily = (FontFamily)FindResource("MonoFont") };
        key.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        left.Children.Add(key);
        grid.Children.Add(left);
        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = s.Value, FontFamily = (FontFamily)FindResource("MonoFont"), HorizontalAlignment = HorizontalAlignment.Right });
        if (onPrinter is not null)
        {
            var now = new TextBlock { Text = Localization.T("imprimante : ") + onPrinter, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Right };
            now.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            right.Children.Add(now);
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var row = new Border { Padding = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Cursor = Cursors.Hand };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        row.MouseLeftButtonUp += (_, _) => GoToLine(s.Line);
        return row;
    }

    private static TextBlock Muted(string text)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }
}
