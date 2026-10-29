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
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded)
            {
                LoadTarget(); // l'imprimante a pu changer dans « Code imprimante »
            }
            else
            {
                SuggestPopup.IsOpen = false;
            }
        };
        AddressBox.TextChanged += (_, _) => AddressHint.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        HintAligner.Attach(AddressBox, AddressHint);
        HintAligner.Attach(QueueSearch, QueueSearchHint);
    }

    /// <summary>Message pour le journal.</summary>
    public event Action<LogKind, string>? Log;

    // ------------------------------------------------------------------ Imprimante

    private string _savedQueue = string.Empty;

    private void LoadTarget()
    {
        _loading = true;
        var p = AppSettings.Load().Print;
        AddressBox.Text = p.Address;
        AddressHint.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _savedQueue = p.Queue;
        (p.Mode == "queue" ? TargetQueue : TargetTcp).IsChecked = true;
        ShowTarget();
        _loading = false;
    }

    private void ShowTarget()
    {
        if (QueuePanel is null || AddressPanel is null)
        {
            return;
        }

        var queue = TargetQueue.IsChecked == true;
        QueuePanel.Visibility = queue ? Visibility.Visible : Visibility.Collapsed;
        AddressPanel.Visibility = queue ? Visibility.Collapsed : Visibility.Visible;
        if (queue)
        {
            LoadQueues();
        }
    }

    private void Target_Changed(object sender, RoutedEventArgs e)
    {
        ShowTarget();
        SaveTarget();
    }

    private void Address_LostFocus(object sender, RoutedEventArgs e) => SaveTarget();

    /// <summary>Imprimante installée choisie (ou celle mémorisée, tant que la liste n'est pas chargée).</summary>
    private string? SelectedQueue =>
        QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? (_savedQueue.Length == 0 ? null : _savedQueue);

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
                GroupName = "ConfQueue",
                Content = q,
                Tag = q,
                IsChecked = string.Equals(q, current, StringComparison.OrdinalIgnoreCase),
            };
            chip.Checked += (_, _) =>
            {
                _savedQueue = q;
                QueueSelected.Text = q;
                QueuePopup.IsOpen = false;
                SaveTarget();
            };
            QueueChips.Children.Add(chip);
        }

        var n = QueueChips.Children.Count;
        QueueCount.Text = Localization.T(n switch
        {
            0 => "aucune imprimante installée",
            1 => "1 imprimante",
            _ => $"{n} imprimantes",
        });
        QueueSelected.Text = QueueChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? Localization.T("Choisir une imprimante…");
        QueueScroll.ScrollToTop();
        if (n == 0)
        {
            QueueHint.Text = Localization.T("Aucune imprimante installée sur ce PC. Utilise l'envoi réseau, ou installe le pilote de l'imprimante d'étiquettes.");
        }
    }

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
        if (SelectedQueue is { } q)
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
            return SelectedQueue is { } q ? PrinterTransport.QueueAddress(q) : null;
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
                if (SelectedQueue is not { } queue)
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
            _filePath = path;
            _fileEncoding = encoding;
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

    private string? _filePath;
    private string _fileEncoding = "UTF-8";

    /// <summary>Enregistre le fichier (modifié ou non) sous un nouveau nom ; même encodage que l'original quand c'est possible.</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveAs();

    private void SaveAs()
    {
        if (CodeBox.Text.Trim().Length == 0)
        {
            Log?.Invoke(LogKind.Warning, "Rien à enregistrer : le fichier est vide.");
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = Localization.T("Enregistrer le fichier de configuration"),
            Filter = Localization.T("Fichier texte (*.txt)|*.txt|ZPL (*.zpl)|*.zpl|Fichier d'impression (*.prn)|*.prn|Tous les fichiers|*.*"),
            FileName = _filePath is null ? "configuration.txt" : Path.GetFileName(_filePath),
            InitialDirectory = _filePath is null ? string.Empty : Path.GetDirectoryName(_filePath) ?? string.Empty,
            AddExtension = true,
            OverwritePrompt = true,
        };
        var ext = _filePath is null ? ".txt" : Path.GetExtension(_filePath).ToLowerInvariant();
        dialog.FilterIndex = ext switch { ".zpl" => 2, ".prn" => 3, ".txt" => 1, _ => 4 };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var text = CodeBox.Text;
            byte[] bytes;
            if (_fileBytes is not null && text == _fileText)
            {
                bytes = _fileBytes;
            }
            else
            {
                Encoding encoding = _fileEncoding switch
                {
                    "UTF-16 (Unicode)" => new UnicodeEncoding(false, true),
                    "UTF-16 big-endian" => new UnicodeEncoding(true, true),
                    "Windows-1252 / Latin-1" when text.All(c => c <= 0xFF) => Encoding.Latin1,
                    _ => new UTF8Encoding(false),
                };
                bytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];
            }

            File.WriteAllBytes(dialog.FileName, bytes);
            _filePath = dialog.FileName;
            _fileBytes = bytes;
            _fileText = text;
            FileInfo.Text = $"{Path.GetFileName(dialog.FileName)} · {bytes.Length} octets · {_fileEncoding}";
            Log?.Invoke(LogKind.Ok, $"Configuration enregistrée : {dialog.FileName}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log?.Invoke(LogKind.Error, "Enregistrement impossible : " + ex.Message);
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
                _filePath = null;
                _fileEncoding = "UTF-8";
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
        _filePath = null;
        _fileEncoding = "UTF-8";
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
        // Saisie au clavier (un ou deux caractères ajoutés, ou effacés) : propositions ; collage ou ouverture : aucune.
        var typing = _typed || (e.Changes.Count == 1 && e.Changes.First() is { } c
            && ((c.AddedLength is > 0 and <= 2 && c.RemovedLength <= 2) || (c.AddedLength == 0 && c.RemovedLength is > 0 and <= 2 && SuggestPopup.IsOpen)));
        if (typing && CodeBox.IsKeyboardFocusWithin)
        {
            _typed = false;
            UpdateSuggestions();
        }
        else
        {
            SuggestPopup.IsOpen = false;
        }

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

    // ------------------------------------------------------------------ Propositions à la saisie

    private bool _typed;
    private ConfigCompletion? _completion;
    private int _suggestIndex;

    private void CodeBox_PreviewTextInput(object sender, TextCompositionEventArgs e) => _typed = true;

    private void CodeBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!SuggestPopup.IsMouseOver)
        {
            SuggestPopup.IsOpen = false;
        }
    }

    private void CodeBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SuggestPopup.IsOpen = false;
            SaveAs();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            UpdateSuggestions(force: true);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Back or Key.Delete)
        {
            _typed = SuggestPopup.IsOpen;
            return;
        }

        if (!SuggestPopup.IsOpen || _completion is null)
        {
            return;
        }

        var count = _completion.Items.Count;
        switch (e.Key)
        {
            case Key.Down:
                SelectSuggestion((_suggestIndex + 1) % count);
                e.Handled = true;
                break;
            case Key.Up:
                SelectSuggestion((_suggestIndex - 1 + count) % count);
                e.Handled = true;
                break;
            case Key.PageDown:
                SelectSuggestion(Math.Min(count - 1, _suggestIndex + 8));
                e.Handled = true;
                break;
            case Key.PageUp:
                SelectSuggestion(Math.Max(0, _suggestIndex - 8));
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab:
                AcceptSuggestion(_suggestIndex);
                e.Handled = true;
                break;
            case Key.Escape:
                SuggestPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Left or Key.Right or Key.Home or Key.End:
                SuggestPopup.IsOpen = false;
                break;
        }
    }

    /// <summary>Calcule les propositions pour la ligne en cours (texte jusqu'au curseur) et affiche la liste sous le curseur.</summary>
    private void UpdateSuggestions(bool force = false)
    {
        var caret = CodeBox.CaretIndex;
        var text = CodeBox.Text;
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var prefix = text[lineStart..caret];
        _completion = PrinterConfig.Suggest(prefix, force);
        if (_completion.Items.Count == 0)
        {
            SuggestPopup.IsOpen = false;
            return;
        }

        SuggestList.Children.Clear();
        for (var i = 0; i < _completion.Items.Count; i++)
        {
            SuggestList.Children.Add(SuggestionRow(_completion.Items[i], i));
        }

        var rect = CodeBox.GetRectFromCharacterIndex(caret);
        if (!rect.IsEmpty)
        {
            var start = CodeBox.GetRectFromCharacterIndex(Math.Max(lineStart, caret - _completion.ReplaceLength));
            SuggestPopup.HorizontalOffset = Math.Max(0, (start.IsEmpty ? rect : start).Left - 8);
            SuggestPopup.VerticalOffset = rect.Bottom + 4;
        }

        SuggestFooter.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            Localization.T("{0} proposition(s) · ↑ ↓ choisir · Entrée ou Tab insérer · Échap fermer"), _completion.Items.Count);
        SuggestPopup.IsOpen = true;
        SelectSuggestion(0);
    }

    private Border SuggestionRow(ConfigSnippet item, int index)
    {
        var badge = new Border
        {
            Background = (Brush)FindResource("Surface2"),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = item.Language, FontSize = 11, Foreground = (Brush)FindResource("Muted") },
        };
        DockPanel.SetDock(badge, Dock.Right);
        var label = new TextBlock
        {
            Text = item.Label,
            FontFamily = (FontFamily)FindResource("MonoFont"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var description = new TextBlock
        {
            Text = Localization.T(item.Description),
            FontSize = 12,
            Foreground = (Brush)FindResource("Muted"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 1, 0, 0),
        };
        var dock = new DockPanel();
        dock.Children.Add(badge);
        dock.Children.Add(new StackPanel { Children = { label, description } });
        var row = new Border
        {
            Child = dock,
            Padding = new Thickness(10, 6, 10, 6),
            CornerRadius = new CornerRadius(7),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => SelectSuggestion(index, scroll: false);
        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            AcceptSuggestion(index);
            e.Handled = true;
        };
        return row;
    }

    private void SelectSuggestion(int index, bool scroll = true)
    {
        if (index < 0 || index >= SuggestList.Children.Count)
        {
            return;
        }

        _suggestIndex = index;
        for (var i = 0; i < SuggestList.Children.Count; i++)
        {
            var row = (Border)SuggestList.Children[i];
            row.Background = i == index ? (Brush)FindResource("BrandSoft") : Brushes.Transparent;
        }

        if (scroll)
        {
            ((FrameworkElement)SuggestList.Children[index]).BringIntoView();
        }
    }

    /// <summary>Remplace ce qui est tapé par la proposition, place le curseur au bon endroit (entre les guillemets…) et relance les propositions (nom puis valeur SGD).</summary>
    private void AcceptSuggestion(int index)
    {
        if (_completion is null || index < 0 || index >= _completion.Items.Count)
        {
            return;
        }

        var item = _completion.Items[index];
        var caret = CodeBox.CaretIndex;
        var start = Math.Max(0, caret - _completion.ReplaceLength);
        SuggestPopup.IsOpen = false;
        CodeBox.Focus();
        CodeBox.Select(start, caret - start);
        CodeBox.SelectedText = item.Text;
        CodeBox.Select(start + item.CaretOffset, 0);
        Dispatcher.BeginInvoke(new Action(() => UpdateSuggestions()), DispatcherPriority.Input);
    }
}
