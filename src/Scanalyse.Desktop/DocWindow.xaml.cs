using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>
/// Affiche un texte Markdown du programme (Quoi de neuf, licences) dans une fenêtre aux couleurs de Scanalyse :
/// titres, listes, gras, code, liens et tableaux.
/// </summary>
public partial class DocWindow : Window
{
    public DocWindow(string title, string markdown)
    {
        InitializeComponent();
        WindowFrame.Attach(this);
        TitleText.Text = Localization.T(title);
        Title = TitleText.Text + " — Scanalyse";
        Viewer.Document = Build(markdown);
    }

    private FlowDocument Build(string markdown)
    {
        var doc = new FlowDocument
        {
            FontFamily = (FontFamily)FindResource("UiFont"),
            FontSize = 14,
            PagePadding = new Thickness(36, 26, 36, 36),
            LineHeight = 21,
            TextAlignment = TextAlignment.Left,
        };
        doc.SetResourceReference(FlowDocument.ForegroundProperty, "Ink");
        doc.SetResourceReference(FlowDocument.BackgroundProperty, "Bg");

        List? list = null;
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            var bullet = Bullet().Match(line);
            if (bullet.Success)
            {
                if (list is null)
                {
                    list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(22, 0, 0, 0) };
                    doc.Blocks.Add(list);
                }

                var p = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                AddInlines(p.Inlines, bullet.Groups[1].Value);
                var item = new ListItem(p);
                if (bullet.Groups["indent"].Value.Length >= 2)
                {
                    item.Margin = new Thickness(18, 0, 0, 0);
                }

                list.ListItems.Add(item);
                continue;
            }

            list = null;
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith('|') && i + 1 < lines.Length && lines[i + 1].TrimStart().StartsWith("|-", StringComparison.Ordinal))
            {
                var rows = new List<string> { line };
                i += 2;
                while (i < lines.Length && lines[i].TrimStart().StartsWith('|'))
                {
                    rows.Add(lines[i].Trim());
                    i++;
                }

                i--;
                doc.Blocks.Add(BuildTable(rows));
                continue;
            }

            var heading = Heading().Match(line);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                var h = new Paragraph
                {
                    FontSize = level switch { 1 => 24, 2 => 19, _ => 15.5 },
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, level == 1 ? 0 : 18, 0, level == 1 ? 14 : 8),
                };
                if (level == 2)
                {
                    h.SetResourceReference(Paragraph.ForegroundProperty, "BrandText");
                }

                AddInlines(h.Inlines, heading.Groups[2].Value);
                doc.Blocks.Add(h);
                continue;
            }

            // Lignes consécutives = un seul paragraphe (comme en Markdown ; texte de licence coupé à 80 colonnes).
            var text = line.Trim();
            while (i + 1 < lines.Length && lines[i + 1].Trim().Length > 0 && !IsBlockStart(lines[i + 1]))
            {
                text += " " + lines[++i].Trim();
            }

            var para = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
            AddInlines(para.Inlines, text);
            doc.Blocks.Add(para);
        }

        return doc;
    }

    private static bool IsBlockStart(string line) =>
        Bullet().IsMatch(line.TrimEnd()) || Heading().IsMatch(line.TrimEnd()) || line.TrimStart().StartsWith('|') || Numbered().IsMatch(line);

    private Table BuildTable(List<string> rows)
    {
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 6, 0, 16), FontSize = 13 };
        var cells = rows.Select(r => r.Trim('|').Split('|').Select(c => c.Trim()).ToArray()).ToList();
        foreach (var _ in cells[0])
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        for (var r = 0; r < cells.Count; r++)
        {
            var row = new TableRow();
            if (r == 0)
            {
                row.FontWeight = FontWeights.SemiBold;
                row.SetResourceReference(TableRow.BackgroundProperty, "Surface2");
            }

            foreach (var text in cells[r])
            {
                var p = new Paragraph { Margin = new Thickness(0) };
                AddInlines(p.Inlines, text);
                var cell = new TableCell(p) { Padding = new Thickness(10, 7, 10, 7), BorderThickness = new Thickness(0, 0, 0, 1) };
                cell.SetResourceReference(TableCell.BorderBrushProperty, "Line");
                row.Cells.Add(cell);
            }

            group.Rows.Add(row);
        }

        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>Gras (**…**), code (`…`) et liens ([texte](adresse)) dans une ligne.</summary>
    private void AddInlines(InlineCollection target, string text)
    {
        var pos = 0;
        foreach (Match m in InlineToken().Matches(text))
        {
            if (m.Index > pos)
            {
                target.Add(new Run(text[pos..m.Index]));
            }

            if (m.Groups["bold"].Success)
            {
                var bold = new Bold();
                AddInlines(bold.Inlines, m.Groups["bold"].Value);
                target.Add(bold);
            }
            else if (m.Groups["code"].Success)
            {
                var run = new Run(m.Groups["code"].Value) { FontFamily = (FontFamily)FindResource("MonoFont"), FontSize = 12.5 };
                run.SetResourceReference(TextElement.BackgroundProperty, "Surface2");
                target.Add(run);
            }
            else
            {
                var link = new Hyperlink(new Run(m.Groups["label"].Value)) { TextDecorations = null, Cursor = Cursors.Hand };
                link.SetResourceReference(Hyperlink.ForegroundProperty, "BrandText");
                if (Uri.TryCreate(m.Groups["url"].Value, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                {
                    link.ToolTip = uri.AbsoluteUri;
                    link.Click += (_, _) => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }

                target.Add(link);
            }

            pos = m.Index + m.Length;
        }

        if (pos < text.Length)
        {
            target.Add(new Run(text[pos..]));
        }
    }

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        RootBorder.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxIcon.Data = Geometry.Parse(WindowState == WindowState.Maximized ? "M2.5 0.5h7v7M0.5 2.5h7v7h-7z" : "M0.5 0.5h9v9h-9z");
    }

    [GeneratedRegex(@"^(?<indent>\s*)[-*]\s+(.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^\s*\d+\.\s")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^(#{1,4})\s+(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<label>[^\]]+)\]\((?<url>[^)\s]+)\)")]
    private static partial Regex InlineToken();
}
