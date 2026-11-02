using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Scanalyse.Core.Printing;
using Scanalyse.Desktop.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Plan de travail de l'imprimante, dans une fenêtre à part (deuxième écran). Une règle simple, pour ne jamais avoir peur
/// de faire une bêtise : on regarde librement (la liste ne modifie rien), on modifie à un seul endroit (le panneau
/// « Modifier »), les changements s'accumulent dans « Modifications prévues » (ancienne → nouvelle valeur), et rien ne part
/// sans le bouton qui nomme l'imprimante et son adresse. La fenêtre a sa propre imprimante, affichée en permanence :
/// ce qui se passe dans la page ne la change pas.
/// </summary>
internal sealed class PrinterBench : UserControl
{
    private sealed record Change(string Key, string? Old, string New);

    private sealed record Printer(string Target, string Name, IReadOnlyDictionary<string, PrinterValue> Settings, string When, bool FromFile);

    private readonly ConfigView _host;
    private readonly Grid _overlayHost = new();
    private readonly RadioButton _modeAdjust;
    private readonly RadioButton _modeCopy;
    private readonly ContentControl _page = new();
    private bool _busy;

    // Régler : l'imprimante de la fenêtre, et ce qu'on prévoit de lui envoyer
    private Printer? _printer;
    private readonly List<Change> _changes = [];
    private string? _selected;
    private string _family = string.Empty;
    private string _query = string.Empty;
    private bool _showReadOnly;
    private string _status = string.Empty;
    private double _listOffset;

    // Copier : source (lue ou état enregistré) et destination, et les réglages décochés
    private Printer? _source;
    private Printer? _destination;
    private readonly HashSet<string> _unchecked = new(StringComparer.Ordinal);
    private string _sourceTarget = string.Empty;
    private string _destinationTarget = string.Empty;

    public PrinterBench(ConfigView host)
    {
        _host = host;
        var root = new DockPanel();
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        _modeAdjust = Chip("BenchMode", T("Régler une imprimante"), "Voir les réglages d'une imprimante et préparer des modifications", true);
        _modeCopy = Chip("BenchMode", T("Copier d'une imprimante à une autre"), "Lire une imprimante source et une destination, voir les écarts, et copier", false);
        _modeAdjust.Checked += (_, _) => Render();
        _modeCopy.Checked += (_, _) => Render();
        modes.Children.Add(_modeAdjust);
        modes.Children.Add(_modeCopy);
        DockPanel.SetDock(modes, Dock.Top);
        root.Children.Add(modes);
        root.Children.Add(_page);
        _overlayHost.Children.Add(root);
        Content = _overlayHost;

        // Au départ : l'imprimante déjà lue dans la page, s'il y en a une (la fenêtre la garde ensuite pour elle).
        if (host.PrinterSettings is { } settings && host.PrinterTarget is { } target)
        {
            _printer = new Printer(target, host.PrinterName ?? target, settings, Now(), false);
        }

        _sourceTarget = _destinationTarget = host.PrinterTarget ?? string.Empty;
        Render();
    }

    // ------------------------------------------------------------------ Petits outils

    private static string T(string text) => Localization.T(text);

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(format), args);

    private static string Now() => DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    private Button Btn(string style, string text, string? tip, Action click)
    {
        var b = new Button { Style = (Style)FindResource(style), Content = T(text), VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_busy };
        if (tip is not null)
        {
            b.ToolTip = T(tip);
        }

        b.Click += (_, _) => click();
        return b;
    }

    private RadioButton Chip(string group, string text, string? tip, bool isChecked)
    {
        var c = new RadioButton { Style = (Style)FindResource("Chip"), GroupName = group, Content = text, IsChecked = isChecked };
        if (tip is not null)
        {
            c.ToolTip = T(tip);
        }

        return c;
    }

    private TextBlock Text(string text, double size = 13, string brush = "Ink", bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private TextBlock Mono(string text, string brush = "Ink", double size = 12.5)
    {
        var t = Text(text, size, brush);
        t.FontFamily = (FontFamily)FindResource("MonoFont");
        return t;
    }

    private TextBlock Caps(string text)
    {
        var t = Text(text.ToUpperInvariant(), 11, "Muted", true);
        t.Margin = new Thickness(0, 0, 0, 4);
        return t;
    }

    private Border Card(UIElement child, string background = "Surface2", string border = "Line")
    {
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12, 14, 12), BorderThickness = new Thickness(1), Child = child };
        b.SetResourceReference(Border.BackgroundProperty, background);
        b.SetResourceReference(Border.BorderBrushProperty, border);
        return b;
    }

    private TextBox Field(string text, string hint, double width)
    {
        var box = new TextBox { Style = (Style)FindResource("FieldInput"), Text = text, Width = width, Height = 34, Padding = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, IsEnabled = !_busy };
        Placeholder.SetText(box, T(hint));
        return box;
    }

    private static string LabelOf(string key) => PrinterConfig.SettingLabel(key) is { } l ? T(l) : key;

    private void Render()
    {
        if (_modeCopy.IsChecked == true)
        {
            RenderCopy();
        }
        else
        {
            RenderAdjust();
        }
    }

    // ------------------------------------------------------------------ Lecture d'une imprimante

    /// <summary>Lit tous les réglages d'une imprimante réseau (Zebra) ; null et un message au journal en cas d'échec.</summary>
    private async Task<Printer?> ReadAsync(string target)
    {
        target = target.Trim();
        if (target.Length == 0)
        {
            _host.Report(LogKind.Warning, "Saisis l'adresse IP de l'imprimante.");
            return null;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var result = await PrinterProbe.ProbeAsync(target, null, true, cts.Token);
            if (result.Profile is { Settings.Count: > 0 } p)
            {
                _host.Report(LogKind.Ok, $"{p.Settings.Count} réglages lus sur {p.DisplayName} ({target}).");
                return new Printer(target, p.DisplayName, p.Settings, Now(), false);
            }

            _host.Report(LogKind.Warning, result.Reachable
                ? $"{target} répond, mais n'a pas donné ses réglages : la lecture complète fonctionne avec les imprimantes Zebra en réseau."
                : $"{target} ne répond pas (éteinte, autre réseau ou adresse erronée).");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or OperationCanceledException or System.Net.Sockets.SocketException)
        {
            _host.Report(LogKind.Error, $"Lecture de {target} impossible : {ex.Message}");
        }

        return null;
    }

    private async Task RunBusy(string status, Func<Task> work)
    {
        _busy = true;
        _status = status;
        Render();
        try
        {
            await work();
        }
        finally
        {
            _busy = false;
            _status = string.Empty;
            Render();
        }
    }

    // ------------------------------------------------------------------ Régler une imprimante

    private void RenderAdjust()
    {
        var page = new DockPanel();

        // Bandeau : l'imprimante concernée, toujours visible
        page.Children.Add(Docked(PrinterBanner(), Dock.Top));

        if (_printer is null)
        {
            page.Children.Add(Text(T("Saisis l'adresse IP de l'imprimante ci-dessus et clique « Lire » : ses réglages s'affichent ici. Rien n'est modifié tant que tu n'envoies pas tes modifications."), 13.5, "Muted"));
            _page.Content = page;
            return;
        }

        // Modifications prévues, en bas
        page.Children.Add(Docked(ChangesPanel(), Dock.Bottom));

        // Liste (lecture seule) à gauche, panneau « Modifier » à droite
        var body = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 300 });
        body.Children.Add(SettingsList());
        var editor = EditPanel();
        Grid.SetColumn(editor, 2);
        body.Children.Add(editor);
        page.Children.Add(body);
        _page.Content = page;
    }

    private static UIElement Docked(UIElement e, Dock dock)
    {
        DockPanel.SetDock(e, dock);
        return e;
    }

    private Border PrinterBanner()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel();
        left.Children.Add(Caps(T("Imprimante de cette fenêtre")));
        if (_printer is { } p)
        {
            left.Children.Add(Text(p.Name, 17, "Ink", true));
            left.Children.Add(Text(F("Adresse {0} · réglages lus à {1} · {2} réglages", p.Target, p.When, p.Settings.Count), 12.5, "Muted"));
        }
        else
        {
            left.Children.Add(Text(T("Aucune imprimante lue"), 17, "Ink", true));
        }

        if (_status.Length > 0)
        {
            var s = Text(_status, 12.5, "InfoFg");
            s.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(s);
        }

        grid.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var ip = Field(_printer?.Target ?? _host.PrinterTarget ?? string.Empty, "Adresse IP", 150);
        ip.ToolTip = T("Adresse de l'imprimante à lire (port 9100)");
        right.Children.Add(ip);
        var read = Btn("BtnSmall", _printer is null ? "Lire" : "Lire / relire", "Lit tous les réglages de l'imprimante à cette adresse", () => _ = ReadPrinter(ip.Text));
        read.Margin = new Thickness(6, 0, 0, 0);
        right.Children.Add(read);
        if (_printer is not null)
        {
            var save = Btn("BtnSmall", "Enregistrer l'état…", "Enregistre tous les réglages de cette imprimante dans un fichier : pour garder une trace (avant / après une intervention) ou les copier plus tard", SaveState);
            save.Margin = new Thickness(6, 0, 0, 0);
            right.Children.Add(save);
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        ip.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                _ = ReadPrinter(ip.Text);
            }
        };
        var banner = Card(grid, "BrandSoft", "Brand");
        banner.Margin = new Thickness(0, 0, 0, 4);
        return banner;
    }

    private async Task ReadPrinter(string target)
    {
        target = target.Trim();
        var other = _printer is not null && !string.Equals(_printer.Target, target, StringComparison.OrdinalIgnoreCase);
        if (other && _changes.Count > 0 && !await AskAsync(T("Changer d'imprimante ?"),
                F("{0} modification(s) sont prévues pour {1} ({2}). Elles seront abandonnées si tu lis une autre imprimante.", _changes.Count, _printer!.Name, _printer.Target),
                T("Abandonner et lire l'autre")))
        {
            return;
        }

        await RunBusy(F("Lecture de {0}…", target), async () =>
        {
            var read = await ReadAsync(target);
            if (read is null)
            {
                return;
            }

            if (other || _printer is null)
            {
                _changes.Clear();
                _selected = null;
            }

            _printer = read;
        });
    }

    private UIElement SettingsList()
    {
        var p = _printer!;
        var panel = new DockPanel();
        var tools = new StackPanel();

        // Recherche
        var searchGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var search = new TextBox { Style = (Style)FindResource("SearchInput"), Text = _query };
        searchGrid.Children.Add(search);
        var icon = new Viewbox { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        var path = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("IconSearch"), Width = 24, Height = 24, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Muted");
        icon.Child = path;
        searchGrid.Children.Add(icon);
        var hint = Text(T("Rechercher un réglage : nom, libellé ou valeur"), 13.5, "Muted");
        hint.Margin = new Thickness(37, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.IsHitTestVisible = false;
        hint.Visibility = _query.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        searchGrid.Children.Add(hint);
        tools.Children.Add(searchGrid);

        // Familles
        var visible = p.Settings
            .Where(kv => _showReadOnly || PrinterConfig.Classify(kv.Key, kv.Value) != SettingUse.ReadOnly)
            .Where(kv => _query.Length == 0
                || kv.Key.Contains(_query, StringComparison.OrdinalIgnoreCase)
                || kv.Value.Value.Contains(_query, StringComparison.OrdinalIgnoreCase)
                || LabelOf(kv.Key).Contains(_query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        var counts = visible.GroupBy(kv => PrinterConfig.FamilyOf(kv.Key)).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (_family.Length > 0 && !counts.ContainsKey(_family))
        {
            _family = string.Empty;
        }

        var families = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        void Family(string tag, string label, int count)
        {
            var c = Chip("BenchFamily", label + "  " + count.ToString(CultureInfo.InvariantCulture), null, tag == _family);
            c.Checked += (_, _) =>
            {
                _family = tag;
                _listOffset = 0;
                Render();
            };
            families.Children.Add(c);
        }

        Family(string.Empty, T("Toutes"), visible.Count);
        foreach (var f in PrinterConfig.SettingFamilies.Where(counts.ContainsKey))
        {
            Family(f, T(f), counts[f]);
        }

        tools.Children.Add(families);
        var readOnly = new CheckBox { Style = (Style)FindResource("CheckLine"), Content = T("Afficher aussi les informations en lecture seule (modèle, compteurs, état…)"), IsChecked = _showReadOnly, Margin = new Thickness(0, 0, 0, 8) };
        readOnly.Click += (_, _) =>
        {
            _showReadOnly = readOnly.IsChecked == true;
            Render();
        };
        tools.Children.Add(readOnly);
        panel.Children.Add(Docked(tools, Dock.Top));

        // Liste : on clique une ligne pour la modifier dans le panneau de droite
        var list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var shown = visible.Where(kv => _family.Length == 0 || PrinterConfig.FamilyOf(kv.Key) == _family).ToList();
        if (shown.Count == 0)
        {
            list.Children.Add(Text(T("Aucun réglage ne correspond à la recherche."), 13, "Muted"));
        }

        foreach (var kv in shown)
        {
            list.Children.Add(Row(kv.Key, kv.Value));
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = list };
        scroll.ScrollChanged += (_, _) => _listOffset = scroll.VerticalOffset;
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(_listOffset);
        panel.Children.Add(scroll);

        search.TextChanged += (_, _) =>
        {
            _query = search.Text.Trim();
            _listOffset = 0;
            Render();
            _ = Dispatcher.InvokeAsync(() =>
            {
                if (FindSearch() is { } s)
                {
                    s.Focus();
                    s.CaretIndex = s.Text.Length;
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
        };
        search.Tag = "bench-search";
        return panel;
    }

    private TextBox? FindSearch()
    {
        static TextBox? Walk(DependencyObject d)
        {
            if (d is TextBox { Tag: "bench-search" } t)
            {
                return t;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                if (Walk(VisualTreeHelper.GetChild(d, i)) is { } found)
                {
                    return found;
                }
            }

            return null;
        }

        UpdateLayout();
        return Walk(this);
    }

    private Border Row(string key, PrinterValue value)
    {
        var change = _changes.FirstOrDefault(c => c.Key == key);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (PrinterConfig.SettingLabel(key) is { } label)
        {
            left.Children.Add(Text(T(label)));
            left.Children.Add(Mono(key, "Muted", 11.5));
        }
        else
        {
            left.Children.Add(Mono(key, "Ink", 12));
        }

        grid.Children.Add(left);
        var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (change is not null)
        {
            var t = Mono(PrinterConfig.Unquote(value.Value) + "  →  " + change.New, "InfoFg");
            t.FontWeight = FontWeights.SemiBold;
            right.Children.Add(t);
            right.Children.Add(Text(T("modification prévue"), 11.5, "InfoFg"));
        }
        else
        {
            right.Children.Add(Mono(PrinterConfig.Unquote(value.Value)));
            if (PrinterConfig.Classify(key, value) == SettingUse.ReadOnly)
            {
                right.Children.Add(Text(T("lecture seule"), 11.5, "Muted"));
            }
        }

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var row = new Border { Padding = new Thickness(10, 7, 10, 7), BorderThickness = new Thickness(0, 0, 0, 1), Child = grid, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (key == _selected)
        {
            row.SetResourceReference(Border.BackgroundProperty, "BrandSoft");
        }
        else if (change is not null)
        {
            row.SetResourceReference(Border.BackgroundProperty, "InfoBg");
        }
        else
        {
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Surface2");
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        }

        row.ToolTip = T("Clique pour voir ce réglage et préparer une modification");
        row.MouseLeftButtonUp += (_, _) =>
        {
            _selected = key;
            Render();
        };
        return row;
    }

    /// <summary>Le seul endroit où l'on choisit une nouvelle valeur ; elle va dans « Modifications prévues », pas plus loin.</summary>
    private Border EditPanel()
    {
        var panel = new StackPanel();
        panel.Children.Add(Caps(T("Modifier un réglage")));
        if (_selected is null || _printer is null || !_printer.Settings.TryGetValue(_selected, out var value))
        {
            panel.Children.Add(Text(T("Clique un réglage dans la liste pour le voir ici. Tu pourras choisir une nouvelle valeur : elle s'ajoute aux « Modifications prévues », et rien n'est envoyé tant que tu ne cliques pas sur le bouton d'envoi."), 13, "Muted"));
            return Card(panel);
        }

        var key = _selected;
        var current = PrinterConfig.Unquote(value.Value);
        var change = _changes.FirstOrDefault(c => c.Key == key);
        var use = PrinterConfig.Classify(key, value);
        panel.Children.Add(Text(LabelOf(key), 16, "Ink", true));
        var k = Mono(key, "Muted", 11.5);
        k.Margin = new Thickness(0, 2, 0, 12);
        panel.Children.Add(k);
        panel.Children.Add(Caps(T("Sur l'imprimante")));
        var cur = Mono(current, "Ink", 14);
        cur.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(cur);

        if (use is SettingUse.ReadOnly or SettingUse.Action)
        {
            panel.Children.Add(Text(T(use == SettingUse.ReadOnly
                ? "Information en lecture seule : l'imprimante la donne mais ne la laisse pas modifier."
                : "Ce réglage déclenche une action (remise à zéro, calibrage…) : il ne se modifie pas d'ici."), 13, "Muted"));
            return Card(panel);
        }

        if (use == SettingUse.Specific)
        {
            var warn = Card(Text(T("Réglage propre à cette imprimante (adresse, nom, identifiant, secret). Le changer peut la rendre injoignable sur le réseau : vérifie bien la valeur."), 12.5, "WarnFg"), "WarnBg", "WarnBg");
            warn.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(warn);
        }

        panel.Children.Add(Caps(T("Nouvelle valeur")));
        var choices = PrinterConfig.ChoicesOf(value);
        var chosen = change?.New ?? current;
        var error = Text(string.Empty, 12, "BadFg");
        error.Visibility = Visibility.Collapsed;
        Func<string> read;
        if (choices is { Options.Count: > 0 } list)
        {
            var wrap = new WrapPanel();
            foreach (var option in list.Options)
            {
                var c = Chip("BenchValue", option, null, string.Equals(option, chosen, StringComparison.Ordinal));
                c.Checked += (_, _) => chosen = option;
                wrap.Children.Add(c);
            }

            panel.Children.Add(wrap);
            read = () => chosen;
        }
        else
        {
            var box = Field(chosen, "valeur", 240);
            box.HorizontalAlignment = HorizontalAlignment.Left;
            box.FontFamily = (FontFamily)FindResource("MonoFont");
            panel.Children.Add(box);
            if (choices is { Min: { } min, Max: { } max })
            {
                var range = Text(F("Accepte de {0} à {1}", min.ToString(CultureInfo.InvariantCulture), max.ToString(CultureInfo.InvariantCulture)), 12, "Muted");
                range.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(range);
            }
            else if (value.Choices is { Length: > 0 } c)
            {
                var accepted = Text(F("Accepte : {0}", c), 12, "Muted");
                accepted.Margin = new Thickness(0, 4, 0, 0);
                panel.Children.Add(accepted);
            }

            read = () => box.Text.Trim();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Plan(key, current, read(), value, error);
                }
            };
        }

        error.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(error);
        var buttons = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var add = Btn("BtnSecondary", change is null ? "Ajouter aux modifications prévues" : "Mettre à jour la modification prévue",
            "Note ce changement dans « Modifications prévues » : il n'est pas encore envoyé", () => Plan(key, current, read(), value, error));
        add.Margin = new Thickness(0, 0, 8, 6);
        buttons.Children.Add(add);
        if (change is not null)
        {
            var undo = Btn("BtnGhost", "Annuler cette modification", null, () =>
            {
                _changes.RemoveAll(c => c.Key == key);
                Render();
            });
            undo.Margin = new Thickness(0, 0, 0, 6);
            buttons.Children.Add(undo);
        }

        panel.Children.Add(buttons);
        return Card(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel });
    }

    private void Plan(string key, string current, string next, PrinterValue value, TextBlock error)
    {
        if (next.Length == 0 || next.Contains('"', StringComparison.Ordinal))
        {
            error.Text = T("Saisis une valeur (sans guillemets).");
            error.Visibility = Visibility.Visible;
            return;
        }

        if (value.Choices is { Length: > 0 } c && !PrinterConfig.Accepts(c, next))
        {
            error.Text = F("Cette imprimante refuserait « {0} » (accepte : {1}).", next, c);
            error.Visibility = Visibility.Visible;
            return;
        }

        _changes.RemoveAll(x => x.Key == key);
        if (!string.Equals(next, current, StringComparison.Ordinal))
        {
            _changes.Add(new Change(key, current, next));
        }

        Render();
    }

    private Border ChangesPanel()
    {
        var p = _printer!;
        var panel = new StackPanel();
        panel.Children.Add(Caps(F("Modifications prévues ({0})", _changes.Count)));
        if (_changes.Count == 0)
        {
            panel.Children.Add(Text(T("Aucune pour l'instant. Rien n'est envoyé à l'imprimante tant que tu n'as pas cliqué sur le bouton d'envoi, qui nomme l'imprimante et son adresse."), 12.5, "Muted"));
        }
        else
        {
            var rows = new StackPanel();
            foreach (var c in _changes)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var name = new StackPanel();
                name.Children.Add(Text(LabelOf(c.Key), 13));
                if (PrinterConfig.SettingLabel(c.Key) is not null)
                {
                    name.Children.Add(Mono(c.Key, "Muted", 11));
                }

                g.Children.Add(name);
                var values = Mono((c.Old ?? "—") + "  →  " + c.New, "Ink", 13);
                values.FontWeight = FontWeights.SemiBold;
                values.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(values, 1);
                g.Children.Add(values);
                var key = c.Key;
                var remove = Btn("BtnGhost", "Retirer", "Retire ce changement de la liste", () =>
                {
                    _changes.RemoveAll(x => x.Key == key);
                    Render();
                });
                Grid.SetColumn(remove, 2);
                g.Children.Add(remove);
                rows.Children.Add(g);
            }

            panel.Children.Add(new ScrollViewer { Content = rows, MaxHeight = 170, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        var actions = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
        var send = Btn("BtnAction", _changes.Count == 0
                ? F("Envoyer à {0} ({1})", p.Name, p.Target)
                : F("Envoyer {0} modification(s) à {1} ({2})", _changes.Count, p.Name, p.Target),
            null, () => _ = SendAsync());
        send.IsEnabled = !_busy && _changes.Count > 0;
        DockPanel.SetDock(send, Dock.Right);
        actions.Children.Add(send);
        if (_changes.Count > 0)
        {
            var clear = Btn("BtnGhost", "Tout annuler", "Vide la liste des modifications prévues (rien n'a été envoyé)", () =>
            {
                _changes.Clear();
                Render();
            });
            clear.Margin = new Thickness(0, 0, 8, 0);
            actions.Children.Add(clear);
            var file = Btn("BtnGhost", "Voir le fichier", "Met ces modifications dans l'éditeur de la page, sous forme de fichier : pour l'enregistrer ou le relire (rien n'est envoyé)", () => _ = _host.LoadPrinterFile(ChangesFile(), F("Modifications prévues pour {0} ({1})", p.Name, p.Target), _changes.Count, 0));
            actions.Children.Add(file);
        }

        panel.Children.Add(actions);
        var card = Card(panel, _changes.Count > 0 ? "InfoBg" : "Surface2");
        card.Margin = new Thickness(0, 12, 0, 0);
        return card;
    }

    private string ChangesFile()
    {
        var sb = new StringBuilder();
        foreach (var c in _changes)
        {
            sb.Append(PrinterConfig.SetvarLine(c.Key, new PrinterValue(c.New, null))).Append("\r\n");
        }

        return sb.ToString();
    }

    /// <summary>Envoie les modifications prévues, après confirmation qui nomme l'imprimante, puis relit pour vérifier.</summary>
    private async Task SendAsync()
    {
        if (_printer is not { } p || _changes.Count == 0)
        {
            return;
        }

        var list = string.Join(Environment.NewLine, _changes.Select(c => $"• {LabelOf(c.Key)} : {c.Old ?? "—"} → {c.New}"));
        var network = _changes.Any(c => PrinterConfig.Classify(c.Key, p.Settings.TryGetValue(c.Key, out var v) ? v : new PrinterValue(c.New, null)) == SettingUse.Specific
            || c.Key.StartsWith("ip.", StringComparison.Ordinal) || c.Key.StartsWith("wlan.", StringComparison.Ordinal) || c.Key.StartsWith("internal_wired.", StringComparison.Ordinal));
        var message = F("Imprimante : {0}", p.Name) + "\n" + F("Adresse : {0}", p.Target) + "\n\n" + list
            + (network ? "\n\n" + T("Attention : des réglages réseau changent. L'imprimante peut devenir injoignable à cette adresse, ou redémarrer.") : string.Empty);
        if (!await AskAsync(F("Envoyer {0} modification(s) ?", _changes.Count), message, T("Envoyer")))
        {
            return;
        }

        var sent = _changes.ToList();
        await RunBusy(F("Envoi à {0}…", p.Target), async () =>
        {
            try
            {
                await PrinterTransport.SendTcpAsync(p.Target, Encoding.UTF8.GetBytes(ChangesFile()));
                _host.Report(LogKind.Ok, $"{sent.Count} modification(s) envoyée(s) à {p.Name} ({p.Target}).");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or System.Net.Sockets.SocketException)
            {
                _host.Report(LogKind.Error, $"Envoi à {p.Target} impossible : {ex.Message}");
                return;
            }

            if (network)
            {
                _changes.Clear();
                _host.Report(LogKind.Info, "Réglages réseau envoyés : relis l'imprimante quand elle aura redémarré, à sa nouvelle adresse si elle a changé.");
                return;
            }

            // Vérification : on relit et on compare chaque valeur.
            _status = T("Vérification : relecture de l'imprimante…");
            Render();
            await Task.Delay(2500);
            var after = await ReadAsync(p.Target);
            if (after is null)
            {
                return;
            }

            _printer = after;
            var failed = sent.Where(c => !after.Settings.TryGetValue(c.Key, out var v) || !string.Equals(PrinterConfig.Unquote(v.Value), c.New, StringComparison.Ordinal)
                && !(double.TryParse(PrinterConfig.Unquote(v.Value), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                    && double.TryParse(c.New, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && Math.Abs(a - b) < 0.0001)).ToList();
            _changes.Clear();
            _changes.AddRange(failed.Select(c => c with { Old = after.Settings.TryGetValue(c.Key, out var v) ? PrinterConfig.Unquote(v.Value) : null }));
            _host.Report(failed.Count == 0 ? LogKind.Ok : LogKind.Warning, failed.Count == 0
                ? $"Vérifié sur {after.Name} : les {sent.Count} réglage(s) ont bien été pris en compte."
                : $"Vérifié sur {after.Name} : {sent.Count - failed.Count} pris en compte, {failed.Count} non appliqué(s) (restent dans « Modifications prévues ») : {string.Join(", ", failed.Select(c => c.Key))}.");
        });
    }

    // ------------------------------------------------------------------ Copier d'une imprimante à une autre

    private void RenderCopy()
    {
        var page = new DockPanel();
        var steps = new Grid();
        steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        steps.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        steps.Children.Add(Step(1, T("Source : copier depuis"), _source, _sourceTarget, t => _sourceTarget = t, async t =>
        {
            if (await ReadAsync(t) is { } r)
            {
                _source = r;
                _unchecked.Clear();
            }
        }, allowState: true));
        var dest = Step(2, T("Destination : copier vers"), _destination, _destinationTarget, t => _destinationTarget = t, async t =>
        {
            if (await ReadAsync(t) is { } r)
            {
                _destination = r;
                _unchecked.Clear();
            }
        }, allowState: false);
        Grid.SetColumn(dest, 2);
        steps.Children.Add(dest);
        page.Children.Add(Docked(steps, Dock.Top));
        if (_status.Length > 0)
        {
            var s = Text(_status, 12.5, "InfoFg");
            s.Margin = new Thickness(0, 8, 0, 0);
            page.Children.Add(Docked(s, Dock.Top));
        }

        if (_source is null || _destination is null)
        {
            var help = Text(T("1. Lis l'imprimante source (ou ouvre un état enregistré). 2. Lis l'imprimante de destination. 3. Les écarts s'affichent : coche ce que tu veux copier, puis prépare l'envoi vers la destination."), 13.5, "Muted");
            help.Margin = new Thickness(0, 14, 0, 0);
            page.Children.Add(help);
            _page.Content = page;
            return;
        }

        if (!_source.FromFile && string.Equals(_source.Target, _destination.Target, StringComparison.OrdinalIgnoreCase))
        {
            var same = Text(T("La source et la destination sont la même imprimante : lis une autre imprimante en destination."), 13.5, "WarnFg");
            same.Margin = new Thickness(0, 14, 0, 0);
            page.Children.Add(same);
            _page.Content = page;
            return;
        }

        // Écarts : seulement ce qui diffère ; cochés d'office quand la copie est possible
        var diff = PrinterConfig.Diff(_source.Settings, _destination.Settings).Where(d => !d.Same).ToList();
        var copyable = diff.Where(d => CanCopy(d)).ToList();
        var selected = copyable.Where(d => !_unchecked.Contains(d.Key)).ToList();

        var bottom = new DockPanel { Margin = new Thickness(0, 12, 0, 0), LastChildFill = false };
        var go = Btn("BtnAction", F("Préparer {0} réglage(s) pour {1} ({2})", selected.Count, _destination.Name, _destination.Target),
            "Ajoute les réglages cochés aux « Modifications prévues » de la destination : tu les reverras avant l'envoi", () => PrepareCopy(selected));
        go.IsEnabled = !_busy && selected.Count > 0;
        DockPanel.SetDock(go, Dock.Right);
        bottom.Children.Add(go);
        var summary = Text(F("{0} écart(s) · {1} copiable(s) · {2} coché(s)", diff.Count, copyable.Count, selected.Count), 12.5, "Muted");
        summary.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(summary);
        page.Children.Add(Docked(bottom, Dock.Bottom));

        var list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        var head = new Grid { Margin = new Thickness(0, 14, 0, 6) };
        DiffColumns(head);
        var h0 = Caps(T("Réglage"));
        h0.Margin = new Thickness(30, 0, 0, 0);
        head.Children.Add(h0);
        var h1 = Caps(T("Source"));
        Grid.SetColumn(h1, 1);
        head.Children.Add(h1);
        var h2 = Caps(T("Destination"));
        Grid.SetColumn(h2, 2);
        head.Children.Add(h2);
        page.Children.Add(Docked(head, Dock.Top));
        if (diff.Count == 0)
        {
            list.Children.Add(Text(T("Aucun écart : les deux imprimantes sont réglées pareil."), 13, "Muted"));
        }

        foreach (var d in diff.OrderBy(d => CanCopy(d) ? 0 : 1).ThenBy(d => d.Key, StringComparer.Ordinal))
        {
            list.Children.Add(DiffRow(d));
        }

        page.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list });
        _page.Content = page;
    }

    private bool CanCopy(SettingDiff d) =>
        d.Use == SettingUse.Portable && d.Reference is not null && d.Target is not null
        && !(_destination!.Settings.TryGetValue(d.Key, out var v) && v.Choices is { } c && !PrinterConfig.Accepts(c, d.Reference));

    private static void DiffColumns(Grid g)
    {
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
    }

    private Border Step(int n, string title, Printer? printer, string target, Action<string> keep, Func<string, Task> read, bool allowState)
    {
        var panel = new StackPanel();
        panel.Children.Add(Caps(n.ToString(CultureInfo.InvariantCulture) + ". " + title));
        if (printer is not null)
        {
            panel.Children.Add(Text(printer.Name, 15, "Ink", true));
            panel.Children.Add(Text(printer.FromFile ? F("État enregistré · {0} réglages", printer.Settings.Count) : F("Adresse {0} · lue à {1}", printer.Target, printer.When), 12, "Muted"));
        }
        else
        {
            panel.Children.Add(Text(T("Pas encore lue"), 15, "Muted", true));
        }

        var line = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        var ip = Field(target, "Adresse IP", 150);
        ip.TextChanged += (_, _) => keep(ip.Text);
        line.Children.Add(ip);
        var go = Btn("BtnSmall", printer is null ? "Lire" : "Relire", null, () => _ = RunBusy(F("Lecture de {0}…", ip.Text.Trim()), () => read(ip.Text)));
        go.Margin = new Thickness(6, 0, 0, 0);
        line.Children.Add(go);
        if (allowState)
        {
            var open = Btn("BtnSmall", "Ouvrir un état…", "Utilise comme source un état enregistré (fichier) au lieu d'une imprimante lue", OpenSourceState);
            open.Margin = new Thickness(6, 0, 0, 0);
            line.Children.Add(open);
        }

        panel.Children.Add(line);
        return Card(panel, printer is null ? "Surface2" : "BrandSoft", printer is null ? "Line" : "Brand");
    }

    private Border DiffRow(SettingDiff d)
    {
        var can = CanCopy(d);
        var g = new Grid();
        DiffColumns(g);
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        if (can)
        {
            var box = new CheckBox { Style = (Style)FindResource("CheckLine"), IsChecked = !_unchecked.Contains(d.Key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            box.Click += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _unchecked.Remove(d.Key);
                }
                else
                {
                    _unchecked.Add(d.Key);
                }

                Render();
            };
            left.Children.Add(box);
        }
        else
        {
            left.Children.Add(new Border { Width = 27 });
        }

        var name = new StackPanel();
        name.Children.Add(Text(LabelOf(d.Key), 13, can ? "Ink" : "Muted"));
        if (PrinterConfig.SettingLabel(d.Key) is not null)
        {
            name.Children.Add(Mono(d.Key, "Muted", 11));
        }

        left.Children.Add(name);
        g.Children.Add(left);
        var a = Mono(d.Reference ?? "—", can ? "Ink" : "Muted");
        a.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(a, 1);
        g.Children.Add(a);
        var b = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        b.Children.Add(Mono(d.Target ?? "—", can ? "Ink" : "Muted"));
        if (!can)
        {
            var why = d switch
            {
                { Target: null } => T("n'existe pas sur la destination"),
                { Reference: null } => T("n'existe pas sur la source"),
                { Use: SettingUse.Specific } => T("propre à chaque imprimante : jamais copié"),
                { Use: SettingUse.Action } => T("action : jamais copiée"),
                _ => T("refusé par la destination"),
            };
            b.Children.Add(Text(why, 11.5, "Muted"));
        }

        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        var row = new Border { Padding = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        return row;
    }

    private async void PrepareCopy(IReadOnlyList<SettingDiff> selected)
    {
        var dest = _destination!;
        if (_printer is not null && _changes.Count > 0 && !string.Equals(_printer.Target, dest.Target, StringComparison.OrdinalIgnoreCase)
            && !await AskAsync(T("Remplacer les modifications prévues ?"), F("{0} modification(s) étaient prévues pour {1} ({2}) : elles seront abandonnées.", _changes.Count, _printer.Name, _printer.Target), T("Continuer")))
        {
            return;
        }

        if (_printer is null || !string.Equals(_printer.Target, dest.Target, StringComparison.OrdinalIgnoreCase))
        {
            _changes.Clear();
        }

        _printer = dest;
        foreach (var d in selected)
        {
            _changes.RemoveAll(c => c.Key == d.Key);
            _changes.Add(new Change(d.Key, d.Target, d.Reference!));
        }

        _selected = null;
        _modeAdjust.IsChecked = true; // même envoi, même confirmation : on voit tout avant d'envoyer
        _host.Report(LogKind.Info, $"{selected.Count} réglage(s) de {_source!.Name} préparés pour {dest.Name} ({dest.Target}) : vérifie « Modifications prévues » puis envoie.");
        Render();
    }

    // ------------------------------------------------------------------ États enregistrés

    private void SaveState()
    {
        if (_printer is not { } p)
        {
            return;
        }

        var safe = string.Concat(p.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch)).Trim('_');
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = T("Enregistrer l'état de l'imprimante"),
            Filter = T("État d'imprimante (*.txt)|*.txt|Tous les fichiers|*.*"),
            FileName = $"etat_{safe}_{DateTime.Now:yyyy-MM-dd_HHmm}.txt",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, PrinterConfig.SaveState(p.Settings, p.Name + " (" + p.Target + ")", DateTimeOffset.Now));
            _host.Report(LogKind.Ok, $"État de {p.Name} enregistré : {Path.GetFileName(dialog.FileName)} ({p.Settings.Count} réglages).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Report(LogKind.Error, "Enregistrement impossible : " + ex.Message);
        }
    }

    private void OpenSourceState()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = T("Ouvrir un état d'imprimante"),
            Filter = T("État d'imprimante (*.txt)|*.txt|Tous les fichiers|*.*"),
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            var settings = PrinterConfig.ParseAllcv(text);
            if (settings.Count == 0)
            {
                _host.Report(LogKind.Warning, "Ce fichier ne contient pas d'état d'imprimante (réglages « nom : valeur » d'une lecture allcv).");
                return;
            }

            var name = PrinterConfig.StateName(text) ?? Path.GetFileNameWithoutExtension(dialog.FileName);
            _source = new Printer(Path.GetFileName(dialog.FileName), name, settings, string.Empty, true);
            _unchecked.Clear();
            _host.Report(LogKind.Info, $"État ouvert : {name} ({settings.Count} réglages).");
            Render();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Report(LogKind.Error, "Lecture du fichier impossible : " + ex.Message);
        }
    }

    // ------------------------------------------------------------------ Confirmation dans la fenêtre

    /// <summary>Confirmation affichée dans la fenêtre du plan de travail (pas dans la page, qui peut être sur un autre écran).</summary>
    private Task<bool> AskAsync(string title, string message, string confirm)
    {
        var done = new TaskCompletionSource<bool>();
        var veil = new Grid { Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)) };
        var panel = new StackPanel { MaxWidth = 520 };
        panel.Children.Add(Text(title, 17, "Ink", true));
        var msg = Text(message, 13.5);
        msg.Margin = new Thickness(0, 10, 0, 16);
        panel.Children.Add(new ScrollViewer { Content = msg, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Close(bool answer)
        {
            _overlayHost.Children.Remove(veil);
            done.TrySetResult(answer);
        }

        var cancel = new Button { Style = (Style)FindResource("BtnSecondary"), Content = T("Annuler"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => Close(false);
        var ok = new Button { Style = (Style)FindResource("BtnAction"), Content = confirm };
        ok.Click += (_, _) => Close(true);
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        var card = Card(panel, "Surface", "Line");
        card.Padding = new Thickness(22, 18, 22, 18);
        card.HorizontalAlignment = HorizontalAlignment.Center;
        card.VerticalAlignment = VerticalAlignment.Center;
        veil.Children.Add(card);
        _overlayHost.Children.Add(veil);
        cancel.Focus();
        return done.Task;
    }
}
