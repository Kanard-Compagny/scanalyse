using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Scanalyse.Core.Printing;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Plan de travail détaché de « Conf imprimante » (deuxième écran) : les réglages lus sur l'imprimante par famille, modifiables
/// directement dans le fichier de la page ; et la comparaison de deux imprimantes (ou d'une imprimante et d'un état enregistré),
/// avec la copie des réglages de l'une vers l'autre. Le fichier reste dans la page : c'est lui qu'on vérifie et qu'on envoie.
/// </summary>
internal sealed class PrinterBench : UserControl
{
    private readonly ConfigView _host;
    private readonly TextBlock _title = new() { FontSize = 18, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = new() { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly RadioButton _modeSettings;
    private readonly RadioButton _modeCompare;
    private readonly TextBox _search;
    private readonly TextBlock _searchHint;
    private readonly CheckBox _readOnly;
    private readonly StackPanel _families = new();
    private readonly WrapPanel _chips = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _list = new() { Margin = new Thickness(0, 0, 8, 0) };
    private readonly ScrollViewer _scroll;
    private readonly StackPanel _compareHead = new() { Margin = new Thickness(0, 0, 0, 10) };
    private readonly DockPanel _footer = new() { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };
    private string _family = string.Empty;
    private string _filter = "all";
    private bool _refreshing;

    // Comparaison : la référence (imprimante gardée ou état enregistré) et, si on l'a ouvert, un état à la place de l'imprimante lue.
    private IReadOnlyDictionary<string, PrinterValue>? _reference;
    private string? _referenceName;
    private IReadOnlyDictionary<string, PrinterValue>? _otherState;
    private string? _otherName;

    public PrinterBench(ConfigView host)
    {
        _host = host;
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        _subtitle.SetResourceReference(TextBlock.ForegroundProperty, "Muted");

        var root = new DockPanel();

        // En-tête : l'imprimante, et ce qu'on fait de ses réglages (relire, enregistrer, ouvrir un état)
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        var headButtons = new WrapPanel { VerticalAlignment = VerticalAlignment.Top };
        headButtons.Children.Add(Btn("BtnSmall", "Relire", "Relit les réglages de l'imprimante choisie dans la page", (_, _) => _host.ReadPrinterAgain(), new Thickness(0, 0, 6, 0)));
        headButtons.Children.Add(Btn("BtnSmall", "Enregistrer l'état…", "Enregistre tous les réglages lus dans un fichier : pour garder une trace, comparer plus tard ou copier vers une autre imprimante", (_, _) => SaveState(), new Thickness(0, 0, 6, 0)));
        headButtons.Children.Add(Btn("BtnSmall", "Ouvrir un état…", "Ouvre un état enregistré et le prend comme référence de comparaison", (_, _) => OpenState(asReference: true), default));
        DockPanel.SetDock(headButtons, Dock.Right);
        head.Children.Add(headButtons);
        var titles = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        titles.Children.Add(_title);
        titles.Children.Add(_subtitle);
        head.Children.Add(titles);
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // Deux usages : régler (imprimante ↔ fichier) ou comparer deux imprimantes
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        _modeSettings = Chip("BenchMode", "Réglages", "Les réglages de l'imprimante, à reprendre ou modifier dans le fichier", true);
        _modeCompare = Chip("BenchMode", "Comparer deux imprimantes", "Les écarts entre une imprimante de référence et une autre, et la copie de l'une vers l'autre", false);
        _modeSettings.Checked += (_, _) => Refresh();
        _modeCompare.Checked += (_, _) => Refresh();
        modes.Children.Add(_modeSettings);
        modes.Children.Add(_modeCompare);
        DockPanel.SetDock(modes, Dock.Top);
        root.Children.Add(modes);

        DockPanel.SetDock(_compareHead, Dock.Top);
        root.Children.Add(_compareHead);

        // Recherche et filtres
        var tools = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var searchGrid = new Grid { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Stretch };
        _search = new TextBox { Style = (Style)FindResource("SearchInput") };
        AutomationProps(_search, "Rechercher un réglage");
        _search.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Refresh();
        };
        searchGrid.Children.Add(_search);
        var icon = new Viewbox { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0), IsHitTestVisible = false };
        var path = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("IconSearch"), Width = 24, Height = 24, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        path.SetResourceReference(Shape.StrokeProperty, "Muted");
        icon.Child = path;
        searchGrid.Children.Add(icon);
        _searchHint = new TextBlock { Text = T("Rechercher : nom, libellé ou valeur (noirceur, media, wlan…)"), Margin = new Thickness(37, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false, TextTrimming = TextTrimming.CharacterEllipsis };
        _searchHint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        searchGrid.Children.Add(_searchHint);
        tools.Children.Add(searchGrid);
        Grid.SetColumn(_chips, 1);
        _chips.Margin = new Thickness(12, 0, 0, 0);
        tools.Children.Add(_chips);
        _readOnly = new CheckBox { Style = (Style)FindResource("CheckLine"), Content = T("Lecture seule"), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = T("Afficher aussi les informations en lecture seule (modèle, firmware, compteurs, état…)") };
        _readOnly.Click += (_, _) => Refresh();
        Grid.SetColumn(_readOnly, 2);
        tools.Children.Add(_readOnly);
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        DockPanel.SetDock(_footer, Dock.Bottom);
        root.Children.Add(_footer);

        // Familles à gauche, liste à droite
        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var famScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _families, Margin = new Thickness(0, 0, 14, 0) };
        body.Children.Add(famScroll);
        _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _list };
        Grid.SetColumn(_scroll, 1);
        body.Children.Add(_scroll);
        root.Children.Add(body);

        Content = root;
        _host.BenchStateChanged += Refresh;
        Unloaded += (_, _) => _host.BenchStateChanged -= Refresh;
        Refresh();
    }

    private static string T(string text) => Localization.T(text);

    private static string F(string format, params object?[] args) => string.Format(CultureInfo.InvariantCulture, T(format), args);

    private static void AutomationProps(UIElement e, string name) => System.Windows.Automation.AutomationProperties.SetName(e, T(name));

    private Button Btn(string style, string text, string? tip, RoutedEventHandler click, Thickness margin)
    {
        var b = new Button { Style = (Style)FindResource(style), Content = T(text), Margin = margin, VerticalAlignment = VerticalAlignment.Center };
        if (tip is not null)
        {
            b.ToolTip = T(tip);
        }

        b.Click += click;
        return b;
    }

    private RadioButton Chip(string group, string text, string? tip, bool isChecked)
    {
        var c = new RadioButton { Style = (Style)FindResource("Chip"), GroupName = group, Content = T(text), IsChecked = isChecked };
        if (tip is not null)
        {
            c.ToolTip = T(tip);
        }

        return c;
    }

    private TextBlock Muted(string text, double size = 12)
    {
        var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        return t;
    }

    private TextBlock Mono(string text, string brush = "Ink", double size = 12.5)
    {
        var t = new TextBlock { Text = text, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = size, TextWrapping = TextWrapping.Wrap };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    private bool Matches(string key, string? v1, string? v2)
    {
        var q = _search.Text.Trim();
        return q.Length == 0
            || key.Contains(q, StringComparison.OrdinalIgnoreCase)
            || (v1 is not null && v1.Contains(q, StringComparison.OrdinalIgnoreCase))
            || (v2 is not null && v2.Contains(q, StringComparison.OrdinalIgnoreCase))
            || (PrinterConfig.SettingLabel(key) is { } label && T(label).Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Redessine tout : appelé quand l'imprimante est relue, quand le fichier change, ou quand un filtre bouge.</summary>
    public void Refresh()
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            RefreshCore();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void RefreshCore()
    {
        var offset = _scroll.VerticalOffset;
        var printer = _host.PrinterSettings;
        _title.Text = _host.PrinterName ?? T("Aucune imprimante lue");
        _subtitle.Text = printer is null
            ? T("Choisis l'imprimante dans la page « Conf imprimante » puis clique « Lire l'imprimante » : ses réglages s'affichent ici.")
            : F("{0} réglage(s) lus. Le fichier de configuration reste dans la page : ce que tu ajoutes ou modifies ici y est écrit, prêt à vérifier et à envoyer.", printer.Count);
        _list.Children.Clear();
        _footer.Children.Clear();
        if (_modeCompare.IsChecked == true)
        {
            RenderCompare();
        }
        else
        {
            RenderSettings();
        }

        _scroll.ScrollToVerticalOffset(offset);
    }

    // ------------------------------------------------------------------ Réglages : imprimante ↔ fichier

    private void SetChips(params (string Tag, string Text, string Tip)[] chips)
    {
        if (_chips.Tag as string == string.Join("|", chips.Select(c => c.Tag)))
        {
            return; // mêmes puces : on garde le choix en cours
        }

        _chips.Children.Clear();
        _chips.Tag = string.Join("|", chips.Select(c => c.Tag));
        if (!chips.Any(c => c.Tag == _filter))
        {
            _filter = chips[0].Tag;
        }

        foreach (var (tag, text, tip) in chips)
        {
            var c = Chip("BenchFilter", text, tip, tag == _filter);
            c.Margin = new Thickness(0, 0, 6, 0);
            c.Checked += (_, _) =>
            {
                _filter = tag;
                Refresh();
            };
            _chips.Children.Add(c);
        }
    }

    private void RenderSettings()
    {
        _compareHead.Visibility = Visibility.Collapsed;
        SetChips(("all", "Tout", "Tous les réglages"), ("diff", "Différent du fichier", "Les réglages que le fichier changera à l'envoi"), ("file", "Dans le fichier", "Les réglages déjà présents dans le fichier"));
        var printer = _host.PrinterSettings;
        if (printer is null)
        {
            _families.Children.Clear();
            return;
        }

        var file = _host.FileSettings;
        var readOnly = _readOnly.IsChecked == true;
        var visible = printer
            .Where(kv => readOnly || PrinterConfig.Classify(kv.Key, kv.Value) != SettingUse.ReadOnly)
            .Where(kv => _filter switch
            {
                "diff" => file.TryGetValue(kv.Key, out var fv) && !string.Equals(fv, PrinterConfig.Unquote(kv.Value.Value), StringComparison.Ordinal),
                "file" => file.ContainsKey(kv.Key),
                _ => true,
            })
            .Where(kv => Matches(kv.Key, kv.Value.Value, file.TryGetValue(kv.Key, out var fv) ? fv : null))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();
        RenderFamilies(visible.Select(kv => kv.Key).ToList());
        var shown = visible.Where(kv => _family.Length == 0 || PrinterConfig.FamilyOf(kv.Key) == _family).ToList();

        var portable = shown.Where(kv => PrinterConfig.Classify(kv.Key, kv.Value) == SettingUse.Portable).ToList();
        var addAll = Btn("BtnSmall", F("Tout ajouter au fichier ({0})", portable.Count), "Ajoute au fichier les réglages affichés qui peuvent être repris (ceux déjà présents sont mis à jour)", (_, _) => _host.AddPrinterSettings(portable), new Thickness(0, 0, 6, 0));
        addAll.IsEnabled = portable.Count > 0;
        _footer.Children.Add(addAll);
        _footer.Children.Add(Btn("BtnSmall", "Copier la liste", "Copie la liste affichée (nom, valeur, valeurs acceptées), à coller dans un tableur", (_, _) => CopyList(shown.Select(kv => (kv.Key, PrinterConfig.Unquote(kv.Value.Value), kv.Value.Choices ?? string.Empty))), default));
        var hint = Muted(T("Clique sur une valeur de la colonne « Fichier » pour la choisir ou la saisir : elle est écrite tout de suite dans le fichier."));
        hint.Margin = new Thickness(12, 0, 0, 0);
        hint.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hint, Dock.Right);
        _footer.Children.Add(hint);

        if (shown.Count == 0)
        {
            _list.Children.Add(Muted(T(_filter == "all" ? "Aucun réglage ne correspond à la recherche." : "Aucun réglage dans ce cas : le fichier ne règle rien de différent ici.")));
            return;
        }

        _list.Children.Add(HeaderRow(T("Réglage"), T("Imprimante"), T("Fichier")));
        foreach (var kv in shown)
        {
            _list.Children.Add(SettingRow(kv.Key, kv.Value, file.TryGetValue(kv.Key, out var fv) ? fv : null));
        }
    }

    private void RenderFamilies(IReadOnlyList<string> keys)
    {
        _families.Children.Clear();
        var counts = keys.GroupBy(PrinterConfig.FamilyOf).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        if (_family.Length > 0 && !counts.ContainsKey(_family))
        {
            _family = string.Empty;
        }

        void Add(string tag, string label, int count)
        {
            var c = Chip("BenchFamily", label + "  " + count.ToString(CultureInfo.InvariantCulture), null, tag == _family);
            c.HorizontalAlignment = HorizontalAlignment.Stretch;
            c.HorizontalContentAlignment = HorizontalAlignment.Left;
            c.Checked += (_, _) =>
            {
                _family = tag;
                Refresh();
            };
            _families.Children.Add(c);
        }

        Add(string.Empty, T("Toutes"), keys.Count);
        foreach (var f in PrinterConfig.SettingFamilies.Where(counts.ContainsKey))
        {
            Add(f, T(f), counts[f]);
        }
    }

    private Grid Columns(double c3 = 1)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(c3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
        return g;
    }

    private Border HeaderRow(string a, string b, string c)
    {
        var g = Columns();
        foreach (var (text, col) in new[] { (a, 0), (b, 1), (c, 2) })
        {
            var t = Muted(text.ToUpperInvariant(), 11);
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(col == 0 ? 0 : 10, 0, 0, 0);
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        var row = new Border { Padding = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        return row;
    }

    private StackPanel NameCell(string key)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (PrinterConfig.SettingLabel(key) is { } label)
        {
            var l = new TextBlock { Text = T(label), TextWrapping = TextWrapping.Wrap, FontSize = 13 };
            l.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            left.Children.Add(l);
            left.Children.Add(Mono(key, "Muted", 11.5));
        }
        else
        {
            left.Children.Add(Mono(key, "Ink", 12));
        }

        return left;
    }

    private Border SettingRow(string key, PrinterValue value, string? inFile)
    {
        var use = PrinterConfig.Classify(key, value);
        var printerValue = PrinterConfig.Unquote(value.Value);
        var g = Columns();
        g.Children.Add(NameCell(key));

        var mid = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(Mono(printerValue));
        var note = use switch
        {
            SettingUse.ReadOnly => T("lecture seule"),
            SettingUse.Specific => T("propre à cette imprimante"),
            SettingUse.Action => T("déclenche une action"),
            _ => value.Choices is { Length: > 0 } c ? F("accepte : {0}", c) : string.Empty,
        };
        if (note.Length > 0)
        {
            var n = Muted(note, 11.5);
            n.TextTrimming = TextTrimming.CharacterEllipsis;
            n.MaxHeight = 32;
            n.ToolTip = note;
            mid.Children.Add(n);
        }

        Grid.SetColumn(mid, 1);
        g.Children.Add(mid);

        // Colonne « Fichier » : la valeur que le fichier donnera, modifiable sur place
        if (use is SettingUse.Portable or SettingUse.Specific)
        {
            var editor = FileEditor(key, value, inFile);
            Grid.SetColumn(editor, 2);
            g.Children.Add(editor);
        }

        if (use == SettingUse.Portable)
        {
            var add = new Button
            {
                Style = (Style)FindResource("BtnSmall"), Content = "+", Width = 30, Height = 30, Padding = new Thickness(0), FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                ToolTip = T(inFile is null ? "Reprendre la valeur de l'imprimante dans le fichier" : "Remettre dans le fichier la valeur de l'imprimante"),
            };
            add.Click += (_, _) => _host.AddPrinterSettings([new KeyValuePair<string, PrinterValue>(key, value)]);
            Grid.SetColumn(add, 3);
            g.Children.Add(add);
        }

        var row = new Border { Padding = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (inFile is not null && !string.Equals(inFile, printerValue, StringComparison.Ordinal))
        {
            row.SetResourceReference(Border.BackgroundProperty, "InfoBg"); // ce réglage changera à l'envoi
        }

        return row;
    }

    /// <summary>Valeur du fichier : un menu des choix annoncés, ou un champ (plage, valeur libre) ; écrite dès qu'elle est choisie.</summary>
    private FrameworkElement FileEditor(string key, PrinterValue value, string? inFile)
    {
        var choices = PrinterConfig.ChoicesOf(value);
        var panel = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        if (choices is { Options.Count: > 0 } list)
        {
            var b = new Button { Style = (Style)FindResource("BtnSmall"), HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, MaxWidth = 260 };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(Mono(inFile ?? "—", inFile is null ? "Muted" : "Ink", 12.5));
            var chev = new System.Windows.Shapes.Path { Data = (Geometry)FindResource("IconChevronDown"), Width = 24, Height = 24, StrokeThickness = 2, Stretch = Stretch.Uniform, MaxWidth = 10, MaxHeight = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            chev.SetResourceReference(Shape.StrokeProperty, "Muted");
            content.Children.Add(chev);
            b.Content = content;
            b.ToolTip = T("Choisir la valeur que le fichier donnera à l'imprimante");
            b.Click += (_, _) =>
            {
                var menu = new ContextMenu { PlacementTarget = b, Placement = PlacementMode.Bottom };
                foreach (var option in list.Options)
                {
                    var item = new MenuItem { Header = option, IsCheckable = true, IsChecked = string.Equals(option, inFile, StringComparison.Ordinal) };
                    item.Click += (_, _) => _host.SetFileValue(key, option);
                    menu.Items.Add(item);
                }

                if (inFile is not null)
                {
                    menu.Items.Add(new Separator());
                    var remove = new MenuItem { Header = T("Retirer du fichier") };
                    remove.Click += (_, _) => _host.RemoveFileSetting(key);
                    menu.Items.Add(remove);
                }

                menu.IsOpen = true;
            };
            panel.Children.Add(b);
            return panel;
        }

        var box = new TextBox { Style = (Style)FindResource("FieldInput"), Text = inFile ?? string.Empty, FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5, Padding = new Thickness(6, 4, 6, 4), MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Stretch };
        Placeholder.SetText(box, choices is { Min: { } min, Max: { } max }
            ? F("{0} à {1}", min.ToString(CultureInfo.InvariantCulture), max.ToString(CultureInfo.InvariantCulture))
            : T("valeur…"));
        box.ToolTip = T("Saisis la valeur puis Entrée : elle est écrite dans le fichier. Vide puis Entrée : le réglage est retiré du fichier.");
        var error = Muted(string.Empty, 11.5);
        error.SetResourceReference(TextBlock.ForegroundProperty, "BadFg");
        error.Visibility = Visibility.Collapsed;
        var committed = inFile ?? string.Empty;
        void Commit()
        {
            var v = box.Text.Trim();
            if (_refreshing || string.Equals(v, committed, StringComparison.Ordinal))
            {
                return;
            }


            if (v.Length == 0)
            {
                committed = v;
                _host.RemoveFileSetting(key);
                return;
            }

            if (value.Choices is { Length: > 0 } c && !PrinterConfig.Accepts(c, v))
            {
                error.Text = F("Refusé par cette imprimante (accepte : {0})", c);
                error.Visibility = Visibility.Visible;
                box.SetResourceReference(Control.BorderBrushProperty, "BadFg");
                return;
            }

            committed = v;
            _host.SetFileValue(key, v);
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                Commit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                box.Text = inFile ?? string.Empty;
            }
        };
        box.LostKeyboardFocus += (_, _) => Commit();
        panel.Children.Add(box);
        panel.Children.Add(error);
        return panel;
    }

    // ------------------------------------------------------------------ Comparer deux imprimantes

    private (IReadOnlyDictionary<string, PrinterValue>? Settings, string? Name) Other() =>
        _otherState is not null ? (_otherState, _otherName) : (_host.PrinterSettings, _host.PrinterName);

    private void RenderCompare()
    {
        SetChips(("diff", "Différences", "Les réglages qui ne sont pas identiques"), ("all", "Tout", "Tous les réglages des deux imprimantes"));
        _compareHead.Visibility = Visibility.Visible;
        _compareHead.Children.Clear();
        var (other, otherName) = Other();

        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var refButtons = new WrapPanel();
        refButtons.Children.Add(Btn("BtnSmall", "Prendre l'imprimante lue", "L'imprimante lue dans la page devient la référence ; choisis ensuite l'autre imprimante dans la page et lis-la", (_, _) => TakeReference(), new Thickness(0, 6, 6, 0)));
        refButtons.Children.Add(Btn("BtnSmall", "Ouvrir un état…", "Prend comme référence un état enregistré (fichier)", (_, _) => OpenState(asReference: true), new Thickness(0, 6, 0, 0)));
        cards.Children.Add(SideCard(T("Référence (copier depuis)"), _referenceName ?? T("Pas encore choisie"), refButtons));

        var otherButtons = new WrapPanel();
        otherButtons.Children.Add(Btn("BtnSmall", "Ouvrir un état…", "Compare avec un état enregistré au lieu de l'imprimante lue", (_, _) => OpenState(asReference: false), new Thickness(0, 6, 6, 0)));
        if (_otherState is not null)
        {
            otherButtons.Children.Add(Btn("BtnSmall", "Revenir à l'imprimante lue", null, (_, _) =>
            {
                _otherState = null;
                _otherName = null;
                Refresh();
            }, new Thickness(0, 6, 0, 0)));
        }

        var otherCard = SideCard(T("Autre imprimante (copier vers)"), otherName ?? T("Choisis-la dans la page puis « Lire l'imprimante »"), otherButtons);
        Grid.SetColumn(otherCard, 2);
        cards.Children.Add(otherCard);
        _compareHead.Children.Add(cards);

        if (_reference is null || other is null)
        {
            RenderFamilies([]);
            _list.Children.Add(Muted(T("Pour comparer : 1. lis la première imprimante dans la page et clique « Prendre l'imprimante lue » (ou ouvre un état enregistré) ; 2. choisis l'autre imprimante dans la page et lis-la. Les écarts s'affichent ici, prêts à être copiés."), 13));
            return;
        }

        if (ReferenceEquals(_reference, other))
        {
            RenderFamilies([]);
            _list.Children.Add(Muted(T("La référence est l'imprimante lue en ce moment : choisis maintenant l'autre imprimante dans la page et clique « Lire l'imprimante »."), 13));
            return;
        }

        var diff = PrinterConfig.Diff(_reference, other, _readOnly.IsChecked == true)
            .Where(d => _filter != "diff" || !d.Same)
            .Where(d => Matches(d.Key, d.Reference, d.Target))
            .ToList();
        RenderFamilies(diff.Select(d => d.Key).ToList());
        var shown = diff.Where(d => _family.Length == 0 || PrinterConfig.FamilyOf(d.Key) == _family).ToList();

        var (text, written, refused) = PrinterConfig.CopyFile(_reference, other);
        var copy = Btn("BtnAction", F("Copier la référence vers l'autre imprimante ({0})", written),
            "Écrit dans le fichier de la page les réglages de la référence qui diffèrent sur l'autre imprimante (jamais les adresses, noms ni secrets) : vérifie-le puis « Envoyer à l'imprimante »",
            async (_, _) => await _host.LoadPrinterFile(text, F("Copie de {0} vers {1}", _referenceName, otherName), written, refused.Count), new Thickness(0, 0, 6, 0));
        copy.IsEnabled = written > 0;
        _footer.Children.Add(copy);
        _footer.Children.Add(Btn("BtnSmall", "Copier la liste", "Copie la comparaison affichée, à coller dans un tableur", (_, _) => CopyList(shown.Select(d => (d.Key, d.Reference ?? "—", d.Target ?? "—"))), default));
        if (refused.Count > 0)
        {
            var r = Muted(F("{0} réglage(s) de la référence n'existent pas sur l'autre imprimante ou y seraient refusés : ils ne sont pas copiés.", refused.Count));
            r.Margin = new Thickness(12, 0, 0, 0);
            r.VerticalAlignment = VerticalAlignment.Center;
            r.ToolTip = string.Join(", ", refused);
            DockPanel.SetDock(r, Dock.Right);
            _footer.Children.Add(r);
        }

        var differences = diff.Count(d => !d.Same);
        _list.Children.Add(Muted(F("{0} différence(s) sur les réglages comparés. En bleu : ce que la copie changera.", differences)));
        if (shown.Count == 0)
        {
            _list.Children.Add(Muted(T("Aucune différence ici : les deux imprimantes sont réglées pareil.")));
            return;
        }

        _list.Children.Add(HeaderRow(T("Réglage"), T("Référence"), T("Autre imprimante")));
        foreach (var d in shown)
        {
            _list.Children.Add(DiffRow(d, other));
        }
    }

    private Border SideCard(string heading, string name, UIElement buttons)
    {
        var p = new StackPanel();
        var h = Muted(heading.ToUpperInvariant(), 11);
        h.FontWeight = FontWeights.SemiBold;
        p.Children.Add(h);
        var n = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        n.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
        p.Children.Add(n);
        p.Children.Add(buttons);
        var b = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(1), Child = p };
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.SetResourceReference(Border.BackgroundProperty, "Surface2");
        return b;
    }

    private Border DiffRow(SettingDiff d, IReadOnlyDictionary<string, PrinterValue> other)
    {
        var g = Columns();
        g.Children.Add(NameCell(d.Key));
        var a = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        a.Children.Add(Mono(d.Reference ?? "—", d.Reference is null ? "Muted" : "Ink"));
        Grid.SetColumn(a, 1);
        g.Children.Add(a);
        var b = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        b.Children.Add(Mono(d.Target ?? "—", d.Target is null ? "Muted" : "Ink"));
        var status = d switch
        {
            { Same: true } => T("identique"),
            { Target: null } => T("absent sur cette imprimante : non copié"),
            { Reference: null } => T("absent sur la référence"),
            { Use: SettingUse.Specific } => T("propre à chaque imprimante : non copié"),
            { Use: SettingUse.Action } => T("action : non copiée"),
            { Use: SettingUse.ReadOnly } => T("lecture seule"),
            _ => other.TryGetValue(d.Key, out var ov) && ov.Choices is { } c && !PrinterConfig.Accepts(c, d.Reference!) ? F("refusé par cette imprimante (accepte : {0})", c) : T("sera copié"),
        };
        var s = Muted(status, 11.5);
        s.TextTrimming = TextTrimming.CharacterEllipsis;
        s.MaxHeight = 32;
        s.ToolTip = status;
        b.Children.Add(s);
        Grid.SetColumn(b, 2);
        g.Children.Add(b);
        var row = new Border { Padding = new Thickness(0, 6, 0, 6), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
        row.SetResourceReference(Border.BorderBrushProperty, "Line");
        if (status == T("sera copié"))
        {
            row.SetResourceReference(Border.BackgroundProperty, "InfoBg");
        }

        return row;
    }

    private void TakeReference()
    {
        if (_host.PrinterSettings is not { } p)
        {
            _host.Report(LogKind.Warning, "Lis d'abord une imprimante dans la page « Conf imprimante ».");
            return;
        }

        _reference = p;
        _referenceName = (_host.PrinterName ?? T("Imprimante")) + " · " + F("lue à {0}", DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture));
        _filter = "diff";
        _chips.Tag = null;
        Refresh();
    }

    // ------------------------------------------------------------------ États enregistrés, copie de liste

    private void SaveState()
    {
        if (_host.PrinterSettings is not { } p)
        {
            _host.Report(LogKind.Warning, "Lis d'abord une imprimante : il n'y a pas encore de réglages à enregistrer.");
            return;
        }

        var name = _host.PrinterName ?? "imprimante";
        var safe = string.Concat(name.Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch)).Trim('_');
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
            File.WriteAllText(dialog.FileName, PrinterConfig.SaveState(p, name, DateTimeOffset.Now));
            _host.Report(LogKind.Ok, $"État de {name} enregistré : {System.IO.Path.GetFileName(dialog.FileName)} ({p.Count} réglages).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Report(LogKind.Error, "Enregistrement impossible : " + ex.Message);
        }
    }

    private void OpenState(bool asReference)
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

            var name = PrinterConfig.StateName(text) ?? System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            if (asReference)
            {
                _reference = settings;
                _referenceName = name;
            }
            else
            {
                _otherState = settings;
                _otherName = name;
            }

            _filter = "diff";
            _chips.Tag = null;
            _modeCompare.IsChecked = true;
            Refresh();
            _host.Report(LogKind.Info, $"État ouvert : {name} ({settings.Count} réglages).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _host.Report(LogKind.Error, "Lecture du fichier impossible : " + ex.Message);
        }
    }

    private void CopyList(IEnumerable<(string A, string B, string C)> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, list.Select(r => r.A + "\t" + r.B + "\t" + r.C)));
            _host.Report(LogKind.Ok, $"{list.Count} ligne(s) copiées.");
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            _host.Report(LogKind.Warning, "Presse-papiers occupé : réessaie.");
        }
    }
}
