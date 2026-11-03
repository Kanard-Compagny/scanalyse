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
    private readonly Action _placeTarget;

    public ConfigView()
    {
        InitializeComponent();
        // « Ne plus utiliser l'imprimante » prend de la place dans la barre : la zone d'adresse passe plus tôt sur sa ligne.
        _placeTarget = ResponsiveTarget.Attach(PrinterCard, WideTargetHost, NarrowTargetHost, TargetArea, 1000,
            () => ForgetButton.Visibility == Visibility.Visible ? Math.Max(ForgetButton.ActualWidth, 200) + 20 : 0);
        ScrollChaining.Enable(PageScroll);
        CodeBox.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => AlignOverlay()));
        CodeBox.SizeChanged += (_, _) => AlignOverlay();
        CodeBox.Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
        _colorTimer.Tick += (_, _) => Recolor();
        BuildLegend();
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Analyze();
        };
        Loaded += (_, _) =>
        {
            LoadTarget();
            ProbeIfChanged();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && IsLoaded)
            {
                LoadTarget(); // l'imprimante a pu changer dans « Étiquette imprimante »
                ProbeIfChanged();
            }
            else
            {
                SuggestPopup.IsOpen = false;
            }
        };
        AddressBox.TextChanged += (_, _) =>
        {
            AddressHint.Visibility = AddressBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (!_loading)
            {
                _probeTimer.Stop();
                _probeTimer.Start();
            }
        };
        _probeTimer.Tick += (_, _) =>
        {
            _probeTimer.Stop();
            ProbeIfChanged();
        };
        HintAligner.Attach(AddressBox, AddressHint);
        HintAligner.Attach(QueueSearch, QueueSearchHint);
        HintAligner.Attach(CodeBox, CodeHint);
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
        ProbeIfChanged();
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
                ProbeIfChanged();
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

    /// <summary>Même imprimante que « Étiquette imprimante » : le choix est partagé.</summary>
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

    private void Read_Click(object sender, RoutedEventArgs e)
    {
        if (NetworkTarget() is null)
        {
            Log?.Invoke(LogKind.Warning, TargetQueue.IsChecked == true
                ? "Cette imprimante installée n'a pas d'adresse réseau (USB, partage) : la lecture des réglages passe par le réseau. Choisis « Réseau » et saisis son adresse IP."
                : "Saisis l'adresse IP de l'imprimante pour lire ses réglages.");
        }

        _ = ProbeAsync(manual: true);
    }

    // ------------------------------------------------------------------ Reconnaissance de l'imprimante (toutes marques)

    private readonly DispatcherTimer _probeTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private CancellationTokenSource? _probeCts;
    private PrinterProfile? _profile;
    private string? _probedKey;

    /// <summary>Clé de l'imprimante choisie : une nouvelle recherche seulement quand elle change.</summary>
    private string TargetKey() => TargetQueue.IsChecked == true ? "q:" + SelectedQueue : "t:" + AddressBox.Text.Trim();

    /// <summary>Adresse qui ressemble à une IP ou à un nom réseau : la recherche part toute seule.</summary>
    private static bool LooksLikeAddress(string s) =>
        System.Text.RegularExpressions.Regex.IsMatch(s.Trim(), @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}|[A-Za-z0-9][A-Za-z0-9\-]*(\.[A-Za-z0-9\-]+)+)(:\d{1,5})?$");

    /// <summary>Recherche automatique : à l'affichage de l'onglet, au choix d'une imprimante ou après la saisie d'une adresse.</summary>
    private void ProbeIfChanged()
    {
        if (!IsLoaded || TargetKey() == _probedKey)
        {
            return;
        }

        // Rien n'est lu sans clic : choisir une imprimante dit seulement où envoyer. Ses réglages et sa marque ne servent de
        // référence qu'après « Lire l'imprimante » (règles de l'onglet : docs/CONF_IMPRIMANTE.md).
        _probeCts?.Cancel();
        _probedKey = TargetKey();
        SetProfile(null, null);
    }

    private void Forget_Click(object sender, RoutedEventArgs e)
    {
        _probeCts?.Cancel();
        SetProfile(null, null);
        Log?.Invoke(LogKind.Info, "Imprimante oubliée comme référence : le fichier est analysé seul. Elle reste la destination de l'envoi.");
    }

    private async Task ProbeAsync(bool manual)
    {
        _probeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _probeCts = cts;
        _probedKey = TargetKey();
        var target = NetworkTarget();
        var queue = TargetQueue.IsChecked == true ? SelectedQueue : null;
        if (target is null && queue is null)
        {
            SetProfile(null, null);
            return;
        }

        ShowProfileStatus("Muted", target is null
            ? Localization.T("Reconnaissance de l'imprimante…")
            : string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Recherche de l'imprimante {0}…"), target));
        ReadButton.IsEnabled = false;
        try
        {
            var result = await PrinterProbe.ProbeAsync(target, queue, manual, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            SetProfile(result, manual ? target : null);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.Net.Sockets.SocketException)
        {
            if (!cts.IsCancellationRequested)
            {
                SetProfile(new ProbeResult(null, false, target), null);
                if (manual)
                {
                    Log?.Invoke(LogKind.Error, ex.Message);
                }
            }
        }
        finally
        {
            if (_probeCts == cts)
            {
                ReadButton.IsEnabled = true;
            }
        }
    }

    /// <summary>Applique ce qui a été appris : référence de l'analyse, indicateur, journal (lecture demandée).</summary>
    private void SetProfile(ProbeResult? result, string? manualTarget)
    {
        _profile = result?.Profile;
        _printer = _profile is { Settings.Count: > 0 } ? _profile.Settings : null;
        _printerName = _profile?.DisplayName;
        ReadButton.Content = Localization.T(_printer is null ? "Lire l'imprimante" : "Relire l'imprimante");
        ViewPrinter.Visibility = _printer is null ? Visibility.Collapsed : Visibility.Visible;
        if (_printer is null && ViewPrinter.IsChecked == true)
        {
            ViewProblems.IsChecked = true;
        }
        else if (_printer is not null && manualTarget is not null)
        {
            ViewPrinter.IsChecked = true; // lecture demandée : on montre ce qui a été lu
        }

        ForgetButton.Visibility = _profile is null ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(_placeTarget, DispatcherPriority.Loaded); // une fois le bouton mesuré
        if (result is null)
        {
            var chosen = TargetQueue.IsChecked == true ? SelectedQueue : (AddressBox.Text.Trim() is { Length: > 0 } a ? a : null);
            ShowProfileStatus("Muted", chosen is null
                ? Localization.T("Choisis l'imprimante qui recevra le fichier. « Lire l'imprimante » l'utilise aussi comme référence (analyse, réglages).")
                : string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    Localization.T("Imprimante choisie pour l'envoi : {0}. Aucune imprimante lue : le fichier est analysé seul. « Lire l'imprimante » pour l'utiliser comme référence."), chosen));
        }
        else if (_profile is null)
        {
            ShowProfileStatus(result.Reachable ? "WarnFg" : "Muted", result.Reachable
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("{0} répond, mais sa marque n'a pas été reconnue : clique « Lire l'imprimante » pour lire ses réglages (Zebra). Analyse du fichier seule."), result.Target)
                : result.Target is null
                    ? Localization.T("Marque de l'imprimante non reconnue (USB ou partage) : analyse du fichier seule.")
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("{0} ne répond pas (éteinte, autre réseau ou adresse erronée) : analyse du fichier seule."), result.Target));
        }
        else
        {
            var p = _profile;
            var parts = new List<string> { p.DisplayName };
            if (p.Firmware is { } fw)
            {
                parts.Add(Localization.T("firmware") + " " + fw);
            }

            parts.Add(p.Languages.Count > 0
                ? Localization.T("langages") + " " + string.Join(", ", p.Languages)
                : Localization.T("langage d'origine") + " " + Localization.T(PrinterConfig.NativeLanguage(p.Brand)));
            string detail;
            if (_printer is not null)
            {
                detail = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("{0} réglages lus : ils font référence pour les noms et les valeurs."), _printer.Count);
            }
            else if (result.Reachable)
            {
                detail = Localization.T("compatibilité vérifiée selon la marque et les langages annoncés (les réglages de cette marque ne se lisent pas par le réseau).");
            }
            else
            {
                detail = result.Target is null
                    ? Localization.T("reconnue par son pilote Windows : compatibilité vérifiée selon la marque.")
                    : string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("ne répond pas sur {0} : compatibilité vérifiée selon la marque (pilote Windows)."), result.Target);
            }

            ShowProfileStatus(result.Reachable || result.Target is null ? "OkFg" : "WarnFg", string.Join(" · ", parts) + " — " + detail);
            if (manualTarget is not null)
            {
                Log?.Invoke(LogKind.Ok, _printer is not null
                    ? $"{_printer.Count} réglages lus sur {p.DisplayName} ({manualTarget})."
                    : $"Imprimante reconnue : {p.DisplayName} ({manualTarget}).");
            }
        }

        if (result is not null && _profile is null && manualTarget is not null)
        {
            Log?.Invoke(LogKind.Warning, $"Aucune information reçue de {manualTarget}.");
        }

        Analyze();

        // Convertir / Créer partent souvent d'un fichier du PC ou de zéro : l'imprimante lue ne change rien de ce qui est
        // déjà choisi (langage, résolution, formulaire). Seul le bouton « Partir des réglages de l'imprimante » la suit.
        FromPrinterButton.Visibility = _printer is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowProfileStatus(string brushKey, string text)
    {
        ProfileDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, brushKey);
        ProfileText.Text = text;
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        Analyze();
        var sendText = TextToSend();
        if (sendText is null)
        {
            Log?.Invoke(LogKind.Warning, _mode switch
            {
                PageMode.Convert => "Rien à envoyer : choisis d'abord le langage d'arrivée pour obtenir le fichier converti.",
                PageMode.Create => "Rien à envoyer : renseigne au moins un réglage valide dans le formulaire.",
                _ => "Rien à envoyer : colle ou ouvre d'abord un fichier de configuration.",
            });
            return;
        }

        // Fichier converti ou créé : contrôlé comme un fichier ouvert, avec l'imprimante choisie.
        var r = _mode == PageMode.Analyze && _report is not null ? _report : PrinterConfig.Analyze(sendText, _printer);
        var check = _mode == PageMode.Analyze ? _comparison : (_printer is null ? [] : PrinterConfig.Compare(r, _printer)).Concat(_profile is null ? [] : PrinterConfig.Compatibility(r, _profile)).ToList();
        var errors = r.Findings.Count(f => f.Severity == Severity.Error) + check.Count(f => f.Severity == Severity.Error);
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

        var bytes = Payload(sendText, out var fixedNewline);
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

            if (_printer is not null && !r.Risky)
            {
                // Zebra lue : relecture automatique, pour montrer les réglages réellement pris en compte.
                Log?.Invoke(LogKind.Info, "Relecture de l'imprimante dans quelques secondes, pour vérifier que les réglages ont été pris en compte.");
                await Task.Delay(2500);
                await ProbeAsync(manual: false);
            }
            else if (_printer is not null)
            {
                Log?.Invoke(LogKind.Info, "Relis l'imprimante quand elle aura redémarré, pour vérifier que les réglages ont été pris en compte.");
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            Log?.Invoke(LogKind.Error, ex.Message);
        }
    }

    /// <summary>Octets envoyés : ceux du fichier ouvert s'il n'a pas été retouché, sinon le texte en UTF-8 ; une ligne SGD finale reçoit son retour à la ligne.</summary>
    private byte[] Payload(string text, out bool fixedNewline)
    {
        var last = text.TrimEnd().Split('\n').LastOrDefault()?.Trim() ?? string.Empty;
        fixedNewline = (last.StartsWith("! U", StringComparison.Ordinal) || last.StartsWith("{}{", StringComparison.Ordinal)) && !text.EndsWith('\n');
        if (_mode == PageMode.Analyze && _fileBytes is not null && text == _fileText && !fixedNewline)
        {
            return _fileBytes;
        }

        // Notations <ESC>, <STX>, <SOH>, <ETX> (SBPL, DPL écrits à la main ou par la conversion) : vrais caractères de contrôle à l'envoi.
        text = System.Text.RegularExpressions.Regex.Replace(text, "<(ESC|STX|SOH|ETX)>", m => m.Groups[1].Value.ToUpperInvariant() switch
        {
            "ESC" => "\u001B",
            "STX" => "\u0002",
            "SOH" => "\u0001",
            _ => "\u0003",
        }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return new UTF8Encoding(false).GetBytes(fixedNewline ? text + "\r\n" : text);
    }

    // ------------------------------------------------------------------ Fichier

    /// <summary>Ouvre un fichier de configuration (bouton, glisser-déposer, « Ouvrir avec »).</summary>
    public void OpenFile(string path)
    {
        if (_mode == PageMode.Create)
        {
            ModeAnalyze.IsChecked = true; // le fichier ouvert s'affiche dans l'éditeur
        }

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
        _colorTimer.Stop();
        _colorTimer.Start();
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
            Render();
            UpdateMarks();
            RefreshConvert();
            BenchStateChanged?.Invoke();
            return;
        }

        var printer = _printer;
        var profile = _profile;
        var r = PrinterConfig.Analyze(text, printer);
        _report = r;
        var comparison = new List<ConfigFinding>();
        if (printer is not null)
        {
            comparison.AddRange(PrinterConfig.Compare(r, printer));
        }

        if (profile is not null)
        {
            comparison.AddRange(PrinterConfig.Compatibility(r, profile));
        }

        _comparison = comparison;
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
        UpdateMarks();
        RefreshConvert();
        BenchStateChanged?.Invoke();
    }

    private void ShowPrinterBanner()
    {
        if (_printer is null)
        {
            PrinterBanner.Visibility = Visibility.Collapsed;
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
        if (!IsLoaded)
        {
            return;
        }

        Render();
    }

    private void Render()
    {
        ResultPanel.Children.Clear();
        PrinterTools.Visibility = ViewPrinter.IsChecked == true && _printer is not null ? Visibility.Visible : Visibility.Collapsed;
        if (PrinterTools.Visibility == Visibility.Visible)
        {
            RenderPrinter();
            return;
        }

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

    // ------------------------------------------------------------------ Réglages lus sur l'imprimante

    private string _printerFamily = string.Empty;
    private string? _printerSelected;

    /// <summary>Réglages lus, filtrés (recherche ; lecture seule masquée sauf demande), triés par nom ; toutes familles.</summary>
    private List<KeyValuePair<string, PrinterValue>> PrinterMatching()
    {
        if (_printer is null)
        {
            return [];
        }

        var q = PrinterFilter.Text.Trim();
        var readOnly = PrinterReadOnly.IsChecked == true;
        return _printer
            .Where(kv => readOnly || PrinterConfig.Classify(kv.Key, kv.Value) != SettingUse.ReadOnly)
            .Where(kv => q.Length == 0
                || kv.Key.Contains(q, StringComparison.OrdinalIgnoreCase)
                || kv.Value.Value.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (PrinterConfig.SettingLabel(kv.Key) is { } label && Localization.T(label).Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Ce qui est affiché : la recherche, dans la famille choisie.</summary>
    private List<KeyValuePair<string, PrinterValue>> PrinterShown() =>
        PrinterMatching().Where(kv => _printerFamily.Length == 0 || PrinterConfig.FamilyOf(kv.Key) == _printerFamily).ToList();

    private void RenderPrinter()
    {
        var matching = PrinterMatching();
        RenderPrinterFamilies(matching);
        var shown = PrinterShown();
        var portable = shown.Count(kv => PrinterConfig.Classify(kv.Key, kv.Value) == SettingUse.Portable);
        PrinterAddAll.Content = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Tout ajouter au fichier ({0})"), portable);
        PrinterAddAll.IsEnabled = portable > 0;
        var inFile = FileSettings;
        if (shown.Count == 0)
        {
            ResultPanel.Children.Add(Muted(Localization.T("Aucun réglage ne correspond à la recherche.")));
            return;
        }

        foreach (var kv in shown)
        {
            ResultPanel.Children.Add(PrinterRow(kv, inFile.TryGetValue(kv.Key, out var fv) ? fv : null));
        }
    }

    private void RenderPrinterFamilies(IReadOnlyList<KeyValuePair<string, PrinterValue>> matching)
    {
        PrinterFamilies.Children.Clear();
        var counts = matching.GroupBy(kv => PrinterConfig.FamilyOf(kv.Key)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (_printerFamily.Length > 0 && !counts.ContainsKey(_printerFamily))
        {
            _printerFamily = string.Empty;
        }

        void Add(string tag, string label, int count)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"), GroupName = "PrinterFamily", IsChecked = tag == _printerFamily,
                Content = label + "  " + count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            chip.Checked += (_, _) =>
            {
                _printerFamily = tag;
                Render();
            };
            PrinterFamilies.Children.Add(chip);
        }

        Add(string.Empty, Localization.T("Toutes"), matching.Count);
        foreach (var f in PrinterConfig.SettingFamilies.Where(counts.ContainsKey))
        {
            Add(f, Localization.T(f), counts[f]);
        }
    }

    /// <summary>
    /// Une ligne : libellé et nom à gauche, valeur de l'imprimante à droite ; en bleu quand le fichier règle ce réglage.
    /// Un clic l'ouvre sur place pour la reprendre dans le fichier (même présentation que le plan de travail).
    /// </summary>
    private Border PrinterRow(KeyValuePair<string, PrinterValue> kv, string? inFile)
    {
        var (key, value) = kv;
        var use = PrinterConfig.Classify(key, value);
        var printerValue = PrinterConfig.Unquote(value.Value);
        var selected = key == _printerSelected;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (PrinterConfig.SettingLabel(key) is { } label)
        {
            var l = new TextBlock { Text = Localization.T(label), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            left.Children.Add(l);
        }

        var name = new TextBlock { Text = key, FontSize = left.Children.Count > 0 ? 11.5 : 12, FontFamily = (FontFamily)FindResource("MonoFont"), TextWrapping = TextWrapping.Wrap };
        name.SetResourceReference(TextBlock.ForegroundProperty, left.Children.Count > 0 ? "Muted" : "Ink");
        left.Children.Add(name);
        grid.Children.Add(left);

        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(right, 1);
        var differs = inFile is not null && !string.Equals(inFile, printerValue, StringComparison.Ordinal);
        var val = new TextBlock
        {
            Text = differs ? printerValue + "  →  " + inFile : printerValue, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap, FontWeight = differs ? FontWeights.SemiBold : FontWeights.Normal,
        };
        val.SetResourceReference(TextBlock.ForegroundProperty, inFile is not null ? "InfoFg" : "Ink");
        right.Children.Add(val);
        var note = inFile is not null
            ? Localization.T(differs ? "le fichier changera cette valeur" : "dans le fichier")
            : use switch
            {
                SettingUse.ReadOnly => Localization.T("lecture seule"),
                SettingUse.Specific => Localization.T("propre à cette imprimante"),
                SettingUse.Action => Localization.T("déclenche une action"),
                _ => string.Empty,
            };
        if (note.Length > 0)
        {
            var n = new TextBlock { Text = note, FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
            n.SetResourceReference(TextBlock.ForegroundProperty, inFile is not null ? "InfoFg" : "Muted");
            right.Children.Add(n);
        }

        grid.Children.Add(right);
        var content = new StackPanel();
        content.Children.Add(grid);
        if (selected)
        {
            content.Children.Add(PrinterRowEditor(key, value, use, printerValue, inFile));
        }

        var row = new Border
        {
            Padding = new Thickness(10, 7, 10, 7), BorderThickness = new Thickness(0, 0, 0, 1), Child = content, Background = Brushes.Transparent,
            Cursor = selected ? Cursors.Arrow : Cursors.Hand,
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (selected)
        {
            row.SetResourceReference(Border.BackgroundProperty, "Surface2");
        }
        else if (inFile is not null)
        {
            row.SetResourceReference(Border.BackgroundProperty, "InfoBg");
        }
        else
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Surface2");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }

        if (!selected)
        {
            row.ToolTip = Localization.T("Clique pour reprendre ce réglage dans le fichier");
        }

        grid.Cursor = Cursors.Hand;
        grid.MouseLeftButtonUp += (_, _) =>
        {
            _printerSelected = selected ? null : key;
            Render();
        };
        return row;
    }

    /// <summary>Le réglage ouvert : valeur à écrire dans le fichier (choix annoncés ou saisie contrôlée), et les boutons du fichier.</summary>
    private StackPanel PrinterRowEditor(string key, PrinterValue value, SettingUse use, string printerValue, string? inFile)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 4) };
        TextBlock Line(string text, string brush, double size = 12.5)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
            t.SetResourceReference(TextBlock.ForegroundProperty, brush);
            return t;
        }

        if (use is SettingUse.ReadOnly or SettingUse.Action)
        {
            panel.Children.Add(Line(Localization.T(use == SettingUse.ReadOnly
                ? "Information en lecture seule : l'imprimante la donne mais ne la laisse pas modifier."
                : "Ce réglage déclenche une action (remise à zéro, calibrage…) : il ne se met pas dans un fichier d'ici."), "Muted"));
            return panel;
        }

        if (use == SettingUse.Specific)
        {
            panel.Children.Add(Line(Localization.T("Réglage propre à cette imprimante (adresse, nom, identifiant, secret) : un fichier qui le contient ne convient qu'à elle."), "WarnFg"));
        }

        var caption = Line(Localization.T("Valeur à écrire dans le fichier"), "Muted", 11);
        caption.FontWeight = FontWeights.SemiBold;
        caption.Margin = new Thickness(0, 0, 0, 6);
        panel.Children.Add(caption);
        var choices = PrinterConfig.ChoicesOf(value);
        var chosen = inFile ?? printerValue;
        var error = Line(string.Empty, "BadFg", 12);
        error.Visibility = Visibility.Collapsed;
        Func<string> read;
        if (choices is { Options.Count: > 0 } list)
        {
            var wrap = new WrapPanel();
            foreach (var option in list.Options)
            {
                var c = new RadioButton { Style = (Style)FindResource("Chip"), GroupName = "PrinterValue", Content = option, IsChecked = string.Equals(option, chosen, StringComparison.Ordinal) };
                c.Checked += (_, _) => chosen = option;
                wrap.Children.Add(c);
            }

            panel.Children.Add(wrap);
            read = () => chosen;
        }
        else
        {
            var box = new TextBox { Style = (Style)FindResource("FieldInput"), Text = chosen, Width = 220, HorizontalAlignment = HorizontalAlignment.Left, FontFamily = (FontFamily)FindResource("MonoFont") };
            panel.Children.Add(box);
            if (choices is { Min: { } min, Max: { } max })
            {
                var range = Line(string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Accepte de {0} à {1}"), min, max), "Muted", 12);
                range.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(range);
            }

            read = () => box.Text.Trim();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    WriteFromRow(key, value, read(), error);
                }
            };
        }

        error.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(error);
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var write = new Button { Style = (Style)FindResource("BtnSecondary"), Content = Localization.T(inFile is null ? "Ajouter au fichier" : "Mettre à jour dans le fichier"), Margin = new Thickness(0, 0, 8, 4) };
        write.Click += (_, _) => WriteFromRow(key, value, read(), error);
        buttons.Children.Add(write);
        if (inFile is not null)
        {
            var remove = new Button { Style = (Style)FindResource("BtnGhost"), Content = Localization.T("Retirer du fichier"), Margin = new Thickness(0, 0, 8, 4) };
            remove.Click += (_, _) => RemoveFileSetting(key);
            buttons.Children.Add(remove);
        }

        var close = new Button { Style = (Style)FindResource("BtnGhost"), Content = Localization.T("Fermer"), Margin = new Thickness(0, 0, 0, 4) };
        close.Click += (_, _) =>
        {
            _printerSelected = null;
            Render();
        };
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        return panel;
    }

    private void WriteFromRow(string key, PrinterValue value, string next, TextBlock error)
    {
        if (next.Length == 0 || next.Contains('"', StringComparison.Ordinal))
        {
            error.Text = Localization.T("Saisis une valeur (sans guillemets).");
            error.Visibility = Visibility.Visible;
            return;
        }

        if (value.Choices is { Length: > 0 } c && !PrinterConfig.Accepts(c, next))
        {
            error.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Cette imprimante refuserait « {0} » (accepte : {1})."), next, c);
            error.Visibility = Visibility.Visible;
            return;
        }

        _printerSelected = null; // la ligne se referme : en bleu, elle montre ce que le fichier règle
        SetFileValue(key, next);
    }

    // ------------------------------------------------------------------ Plan de travail détaché (deuxième écran)

    private ProgramWindow? _benchWindow;

    /// <summary>Le fichier ou l'imprimante a changé : le plan de travail détaché se redessine.</summary>
    internal event Action? BenchStateChanged;

    internal IReadOnlyDictionary<string, PrinterValue>? PrinterSettings => _printer;

    internal string? PrinterName => _printerName;

    /// <summary>Adresse réseau de l'imprimante choisie dans la page (IP saisie, ou celle de l'imprimante installée).</summary>
    internal string? PrinterTarget => NetworkTarget();

    /// <summary>Réglages que le fichier donne (dernière valeur si un réglage apparaît plusieurs fois).</summary>
    internal IReadOnlyDictionary<string, string> FileSettings =>
        _report?.Settings.GroupBy(x => x.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    internal void Report(LogKind kind, string message) => Log?.Invoke(kind, message);

    internal void ReadPrinterAgain() => Read_Click(this, new RoutedEventArgs());

    private void Detach_Click(object sender, RoutedEventArgs e)
    {
        if (_benchWindow is { } open)
        {
            open.Activate();
            return;
        }

        var owner = Window.GetWindow(this);
        var w = new ProgramWindow
        {
            Heading = Localization.T("Plan de travail imprimante"),
            CloseTip = Localization.T("Fermer le plan de travail"),
            Owner = owner,
            Width = Math.Max(900, (owner?.ActualWidth ?? 1400) * 0.6),
            Height = Math.Max(640, (owner?.ActualHeight ?? 900) * 0.9),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        w.Host.Child = new PrinterBench(this);
        w.Closed += (_, _) =>
        {
            w.Host.Child = null;
            _benchWindow = null;
            owner?.Activate();
        };
        _benchWindow = w;
        w.Show();
    }

    /// <summary>Ajoute au fichier des réglages lus, avec la valeur de l'imprimante (« + »).</summary>
    internal void AddPrinterSettings(IReadOnlyList<KeyValuePair<string, PrinterValue>> settings) =>
        WriteSettings(settings.Where(kv => PrinterConfig.Classify(kv.Key, kv.Value) == SettingUse.Portable)
            .Select(kv => (kv.Key, PrinterConfig.Unquote(kv.Value.Value))).ToList());

    /// <summary>Donne une valeur à un réglage dans le fichier (choisie ou saisie dans le plan de travail).</summary>
    internal void SetFileValue(string key, string value) => WriteSettings([(key, value)]);

    /// <summary>
    /// Écrit des réglages dans le fichier de l'éditeur : une ligne « ! U1 setvar » par réglage, ou la ligne existante mise à
    /// jour si le fichier règle déjà ce nom. Le reste du fichier n'est jamais touché.
    /// </summary>
    private void WriteSettings(IReadOnlyList<(string Key, string Value)> settings)
    {
        if (settings.Count == 0)
        {
            return;
        }

        if (_mode != PageMode.Analyze)
        {
            ModeAnalyze.IsChecked = true;
        }

        var wasEmpty = CodeBox.Text.Trim().Length == 0;
        var newline = CodeBox.Text.Contains("\r\n", StringComparison.Ordinal) || wasEmpty ? "\r\n" : "\n";
        var lines = FileLines();
        int added = 0, updated = 0, last = 0;
        foreach (var (key, value) in settings)
        {
            var line = PrinterConfig.SetvarLine(key, new PrinterValue(value, null));
            var at = FindSetting(lines, key);
            if (at >= 0)
            {
                if (!string.Equals(lines[at].Trim(), line, StringComparison.Ordinal))
                {
                    lines[at] = line;
                    updated++;
                }

                last = at + 1;
            }
            else
            {
                lines.Add(line);
                added++;
                last = lines.Count;
            }
        }

        SetFileText(lines, newline);
        if (wasEmpty)
        {
            _filePath = null;
            _fileBytes = null;
            _fileText = null;
            FileInfo.Text = string.Format(System.Globalization.CultureInfo.InvariantCulture, Localization.T("Réglages lus sur {0}"), _printerName);
        }

        Analyze();
        if (_benchWindow is null)
        {
            GoToLine(last); // depuis le plan de travail détaché, on ne lui vole pas le clavier
        }

        Log?.Invoke(LogKind.Ok, settings.Count == 1 && added + updated == 1
            ? $"Fichier : {settings[0].Key} = « {settings[0].Value} »."
            : $"Fichier : {added} réglage(s) de l'imprimante ajouté(s), {updated} mis à jour.");
    }

    /// <summary>Retire un réglage du fichier (toutes ses lignes « setvar »).</summary>
    internal void RemoveFileSetting(string key)
    {
        var lines = FileLines();
        var removed = 0;
        for (var at = FindSetting(lines, key); at >= 0; at = FindSetting(lines, key))
        {
            lines.RemoveAt(at);
            removed++;
        }

        if (removed == 0)
        {
            return;
        }

        SetFileText(lines, CodeBox.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n");
        Analyze();
        Log?.Invoke(LogKind.Info, $"Fichier : {key} retiré.");
    }

    /// <summary>Met un fichier préparé (copie d'une imprimante vers une autre) dans l'éditeur, après accord s'il en contient déjà un.</summary>
    internal async Task LoadPrinterFile(string text, string info, int written, int refused)
    {
        if (CodeBox.Text.Trim().Length > 0 && !await Dialogs.ConfirmAsync(Localization.T("Remplacer le fichier ?"),
                Localization.T("L'éditeur contient déjà un fichier : il sera remplacé par les réglages à copier."), Localization.T("Remplacer"), null, danger: false))
        {
            return;
        }

        if (_mode != PageMode.Analyze)
        {
            ModeAnalyze.IsChecked = true;
        }

        _filePath = null;
        _fileBytes = null;
        _fileText = null;
        CodeBox.Text = text;
        FileInfo.Text = info;
        ViewProblems.IsChecked = true;
        Analyze();
        Log?.Invoke(LogKind.Ok, refused > 0
            ? $"{info} : {written} réglage(s) dans le fichier, {refused} non copié(s) (absents ou refusés sur l'autre imprimante). Vérifie puis « Envoyer à l'imprimante »."
            : $"{info} : {written} réglage(s) dans le fichier. Vérifie puis « Envoyer à l'imprimante ».");
    }

    private List<string> FileLines()
    {
        if (CodeBox.Text.Trim().Length == 0)
        {
            return [];
        }

        var lines = CodeBox.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        while (lines.Count > 0 && lines[^1].Trim().Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }

    private static int FindSetting(List<string> lines, string key)
    {
        var marker = "setvar \"" + key + "\"";
        return lines.FindIndex(l => l.TrimStart().StartsWith('!') && l.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private void SetFileText(List<string> lines, string newline) =>
        CodeBox.Text = lines.Count == 0 ? string.Empty : string.Join(newline, lines) + newline;

    private void PrinterFilter_Changed(object sender, TextChangedEventArgs e)
    {
        PrinterFilterHint.Visibility = PrinterFilter.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && ViewPrinter.IsChecked == true)
        {
            Render();
        }
    }

    private void PrinterReadOnly_Click(object sender, RoutedEventArgs e) => Render();

    private void PrinterAddAll_Click(object sender, RoutedEventArgs e) => AddPrinterSettings(PrinterShown());

    private void PrinterCopy_Click(object sender, RoutedEventArgs e)
    {
        var shown = PrinterShown();
        if (shown.Count == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, shown.Select(kv => kv.Key + "\t" + PrinterConfig.Unquote(kv.Value.Value) + (kv.Value.Choices is { Length: > 0 } c ? "\t" + c : string.Empty)));
        try
        {
            Clipboard.SetText(text);
            Log?.Invoke(LogKind.Ok, $"{shown.Count} réglage(s) de l'imprimante copiés.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            Log?.Invoke(LogKind.Warning, "Presse-papiers occupé : réessaie.");
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
        _completion = PrinterConfig.Suggest(prefix, force, _printer);
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

    // ------------------------------------------------------------------ Coloration et repères d'erreur dans l'éditeur

    private readonly DispatcherTimer _colorTimer = new() { Interval = TimeSpan.FromMilliseconds(90) };
    private Dictionary<int, Severity> _marks = new();
    private double _lineHeight;
    private int _tipLine;

    private static string ColorKey(ConfigTokenKind k) => k switch
    {
        ConfigTokenKind.Command => "SynStruct",
        ConfigTokenKind.Verb => "SynText",
        ConfigTokenKind.Name => "SynPos",
        ConfigTokenKind.Value => "SynCode",
        ConfigTokenKind.Parameter => "SynParam",
        ConfigTokenKind.Comment => "SynComment",
        ConfigTokenKind.Unknown => "BadFg",
        _ => "SynData",
    };

    /// <summary>Redessine le texte coloré sous la zone de saisie (couleurs du thème) ; mot inconnu en rouge souligné.</summary>
    private void Recolor()
    {
        _colorTimer.Stop();
        var text = CodeBox.Text;
        CodeColors.Inlines.Clear();
        if (text.Length > 300_000)
        {
            CodeBox.SetResourceReference(ForegroundProperty, "Ink");
            return;
        }

        CodeBox.Foreground = Brushes.Transparent;
        foreach (var t in PrinterConfig.Tokenize(text))
        {
            var run = new Run(text.Substring(t.Start, t.Length));
            run.SetResourceReference(TextElement.ForegroundProperty, ColorKey(t.Kind));
            if (t.Kind == ConfigTokenKind.Unknown)
            {
                var underline = new TextDecoration { Location = TextDecorationLocation.Underline, PenOffset = 1 };
                underline.Pen = new Pen((Brush)FindResource("BadFg"), 1.2);
                run.TextDecorations = new TextDecorationCollection { underline };
            }

            CodeColors.Inlines.Add(run);
        }

        UpdateLineNumbers();
        Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
    }

    /// <summary>Lignes en erreur ou à vérifier (analyse et comparaison avec l'imprimante) : bandeau sous la ligne et numéro coloré.</summary>
    private void UpdateMarks()
    {
        _marks = new();
        if (_report is { } r)
        {
            foreach (var f in r.Findings.Concat(_comparison).Where(f => f.Line > 0 && f.Severity is Severity.Error or Severity.Warning))
            {
                if (!_marks.TryGetValue(f.Line, out var current) || (current == Severity.Warning && f.Severity == Severity.Error))
                {
                    _marks[f.Line] = f.Severity;
                }
            }
        }

        _tipLine = 0;
        UpdateLineNumbers();
        Dispatcher.BeginInvoke(new Action(AlignOverlay), DispatcherPriority.Loaded);
    }

    private void UpdateLineNumbers()
    {
        var count = Math.Max(1, CodeBox.LineCount > 0 ? CodeBox.LineCount : CodeBox.Text.Count(c => c == '\n') + 1);
        LineNumbers.Inlines.Clear();
        for (var i = 1; i <= count; i++)
        {
            var run = new Run(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (_marks.TryGetValue(i, out var severity))
            {
                run.SetResourceReference(TextElement.ForegroundProperty, severity == Severity.Error ? "BadFg" : "WarnFg");
                run.FontWeight = FontWeights.Bold;
            }

            LineNumbers.Inlines.Add(run);
            if (i < count)
            {
                LineNumbers.Inlines.Add(new LineBreak());
            }
        }
    }

    /// <summary>
    /// Cale le texte coloré, les bandeaux et les numéros de ligne sur la position réelle du texte dans la zone de saisie
    /// (marges internes, défilement et hauteur de ligne compris).
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
            var bottom = host.ComputedHorizontalScrollBarVisibility == Visibility.Visible ? origin.Y + host.ViewportHeight : CodeBox.ActualHeight - 1;
            CodeOverlay.Clip = new RectangleGeometry(new Rect(origin.X, 1, Math.Max(0, host.ViewportWidth), Math.Max(0, bottom - 1)));
            var first = CodeBox.GetRectFromCharacterIndex(0);
            if (first.IsEmpty || double.IsInfinity(first.Top))
            {
                return;
            }

            _lineHeight = first.Height;
            if (CodeBox.LineCount > 1)
            {
                var second = CodeBox.GetRectFromCharacterIndex(CodeBox.GetCharacterIndexFromLineIndex(1));
                var lineHeight = second.Top - first.Top;
                if (lineHeight > 4 && lineHeight < 80)
                {
                    _lineHeight = lineHeight;
                    CodeColors.LineStackingStrategy = LineNumbers.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                    CodeColors.LineHeight = LineNumbers.LineHeight = lineHeight;
                }
            }

            Canvas.SetLeft(CodeColors, first.Left);
            Canvas.SetTop(CodeColors, first.Top);
            Canvas.SetTop(LineNumbers, first.Top - 1);

            MarkLayer.Children.Clear();
            foreach (var (line, severity) in _marks)
            {
                var top = first.Top + ((line - 1) * _lineHeight);
                if (top + _lineHeight < 0 || top > CodeBox.ActualHeight)
                {
                    continue;
                }

                var band = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, host.ViewportWidth), Height = _lineHeight };
                band.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, severity == Severity.Error ? "BadBg" : "WarnBg");
                Canvas.SetLeft(band, origin.X);
                Canvas.SetTop(band, top);
                MarkLayer.Children.Add(band);
                var edge = new System.Windows.Shapes.Rectangle { Width = 3, Height = _lineHeight };
                edge.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, severity == Severity.Error ? "BadFg" : "WarnFg");
                Canvas.SetLeft(edge, origin.X);
                Canvas.SetTop(edge, top);
                MarkLayer.Children.Add(edge);
            }
        }
        catch (InvalidOperationException)
        {
            // Mise en page pas encore prête : on recalera au prochain passage.
        }
    }

    /// <summary>Survol d'une ligne : ses erreurs et points à vérifier en infobulle.</summary>
    private void CodeBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (_report is not { } r || CodeBox.Text.Length == 0)
        {
            return;
        }

        var index = CodeBox.GetCharacterIndexFromPoint(e.GetPosition(CodeBox), snapToText: true);
        var line = index < 0 ? 0 : CodeBox.GetLineIndexFromCharacterIndex(index) + 1;
        if (line == _tipLine)
        {
            return;
        }

        _tipLine = line;
        var messages = r.Findings.Concat(_comparison)
            .Where(f => f.Line == line && f.Severity is not Severity.Ok)
            .OrderBy(f => f.Severity switch { Severity.Error => 0, Severity.Warning => 1, _ => 2 })
            .Select(f => (f.Severity switch { Severity.Error => "⛔ ", Severity.Warning => "⚠ ", _ => "ℹ " }) + Localization.T(f.Message))
            .ToList();
        if (messages.Count == 0)
        {
            CodeBox.ToolTip = null;
            return;
        }

        CodeBox.ToolTip = new System.Windows.Controls.ToolTip
        {
            Content = new TextBlock { Text = string.Join(Environment.NewLine + Environment.NewLine, messages), TextWrapping = TextWrapping.Wrap, MaxWidth = 480 },
        };
    }

    private void BuildLegend()
    {
        (string Key, string Label, bool Band)[] items =
        [
            ("SynStruct", "Commande", false),
            ("SynText", "Action (setvar, do, SET…)", false),
            ("SynPos", "Nom du réglage", false),
            ("SynCode", "Valeur, données", false),
            ("SynParam", "Paramètres", false),
            ("SynComment", "Commentaire", false),
            ("BadFg", "Inconnu de l'imprimante", false),
            ("BadBg", "Ligne en erreur", true),
            ("WarnBg", "Ligne à vérifier", true),
        ];
        foreach (var (key, label, band) in items)
        {
            FrameworkElement swatch = band
                ? new Border { Width = 16, Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center }
                : new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            if (swatch is Border b)
            {
                b.SetResourceReference(Border.BackgroundProperty, key);
                b.SetResourceReference(Border.BorderBrushProperty, key == "BadBg" ? "BadFg" : "WarnFg");
                b.BorderThickness = new Thickness(3, 0, 0, 0);
            }
            else
            {
                swatch.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
            }

            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
            sp.Children.Add(swatch);
            var text = new TextBlock { Text = Localization.T(label), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };
            text.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            sp.Children.Add(text);
            Legend.Children.Add(sp);
        }

        Legend.ToolTip = Localization.T("Survole une ligne pour lire ses erreurs et points à vérifier.");
    }
}
