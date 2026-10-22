using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Scanalyse.Core.Content;
using Scanalyse.Core.History;

namespace Scanalyse.Desktop.Views;

/// <summary>Historique : dossiers SAV, analyses enregistrées et rapport PDF.</summary>
public partial class HistoryView : UserControl
{
    private HistoryStore? _store;
    private string _selectedCaseId = ServiceCase.InboxId;
    private bool _filling;

    public HistoryView()
    {
        InitializeComponent();
    }

    /// <summary>Dossier en cours : les nouvelles analyses y sont enregistrées.</summary>
    public string ActiveCaseId { get; private set; } = ServiceCase.InboxId;

    /// <summary>Le dossier en cours a changé (ou a été renommé).</summary>
    public event Action<ServiceCase>? ActiveCaseChanged;

    /// <summary>Rouvrir une entrée (image dans Analyser, comparaison au banc d'essai).</summary>
    public event Action<HistoryEntry>? ReopenRequested;

    /// <summary>Générer le rapport d'un dossier (fourni par la fenêtre principale).</summary>
    public Func<ServiceCase, Task>? ReportRequested { get; set; }

    /// <summary>Message pour le journal.</summary>
    public event Action<LogKind, string>? Log;

    /// <summary>Branche l'historique chargé et rétablit le dossier en cours.</summary>
    public void Attach(HistoryStore store, string activeCaseId)
    {
        _store = store;
        ActiveCaseId = store.FindCase(activeCaseId) is null ? ServiceCase.InboxId : activeCaseId;
        _selectedCaseId = ActiveCaseId;
        Refresh();
        ActiveCaseChanged?.Invoke(store.FindCase(ActiveCaseId)!);
    }

    /// <summary>Redessine les dossiers et les entrées (après un enregistrement).</summary>
    public void Refresh()
    {
        if (_store is null)
        {
            return;
        }

        FillCases();
        FillCase();
        FillEntries();
    }

    // ------------------------------------------------------------------ Dossiers

    private void FillCases()
    {
        var store = _store!;
        CaseList.Children.Clear();
        foreach (var c in store.Cases)
        {
            var count = store.Entries.Count(e => e.CaseId == c.Id);
            var text = new StackPanel();
            var head = new DockPanel();
            if (c.Id == ActiveCaseId)
            {
                var badge = new Border { Style = (Style)FindResource("Pill"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                badge.SetResourceReference(Border.BackgroundProperty, "BrandSoft");
                var bt = new TextBlock { Text = "En cours", FontSize = 11, FontWeight = FontWeights.SemiBold };
                bt.SetResourceReference(TextBlock.ForegroundProperty, "BrandText");
                badge.Child = bt;
                DockPanel.SetDock(badge, Dock.Right);
                head.Children.Add(badge);
            }

            head.Children.Add(new TextBlock { Text = c.DisplayName, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            text.Children.Add(head);
            var sub = new TextBlock
            {
                Text = (count == 0 ? "aucune analyse" : $"{count} analyse{(count > 1 ? "s" : string.Empty)}")
                    + (c.IsInbox ? string.Empty : $" · créé le {c.Created:dd/MM/yyyy}"),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0),
            };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
            text.Children.Add(sub);

            var card = new RadioButton
            {
                Style = (Style)FindResource("ChoiceCard"),
                GroupName = "Case",
                Tag = c.Id,
                Margin = new Thickness(0, 0, 0, 8),
                Content = text,
                IsChecked = c.Id == _selectedCaseId,
            };
            card.Checked += (_, _) =>
            {
                if (_filling)
                {
                    return;
                }

                SaveFields();
                _selectedCaseId = (string)card.Tag;
                FillCase();
                FillEntries();
            };
            CaseList.Children.Add(card);
        }
    }

    private ServiceCase SelectedCase => _store!.FindCase(_selectedCaseId) ?? _store!.FindCase(ServiceCase.InboxId)!;

    private void FillCase()
    {
        var c = SelectedCase;
        _selectedCaseId = c.Id;
        _filling = true;
        CaseTitle.Text = c.DisplayName;
        ClientBox.Text = c.Client;
        ReferenceBox.Text = c.Reference;
        TechnicianBox.Text = c.Technician;
        NotesBox.Text = c.Notes;
        _filling = false;
        CaseFields.Visibility = c.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        InboxHint.Visibility = c.IsInbox ? Visibility.Visible : Visibility.Collapsed;
        DeleteCaseButton.Visibility = c.IsInbox ? Visibility.Collapsed : Visibility.Visible;
        ActiveBadge.Visibility = c.Id == ActiveCaseId ? Visibility.Visible : Visibility.Collapsed;
        UseCaseButton.Visibility = c.Id == ActiveCaseId ? Visibility.Collapsed : Visibility.Visible;
    }

    private void NewCase_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        var previous = _store!.FindCase(ActiveCaseId);
        var c = new ServiceCase { Technician = previous?.Technician ?? string.Empty };
        _store.SaveCase(c);
        _selectedCaseId = c.Id;
        SetActive(c);
        Dispatcher.InvokeAsync(() => ClientBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void UseCase_Click(object sender, RoutedEventArgs e) => SetActive(SelectedCase);

    private void SetActive(ServiceCase c)
    {
        ActiveCaseId = c.Id;
        _filling = true;
        Refresh();
        _filling = false;
        ActiveCaseChanged?.Invoke(c);
        Log?.Invoke(LogKind.Info, $"Dossier en cours : {c.DisplayName}. Les prochaines analyses y seront enregistrées.");
    }

    private void CaseField_LostFocus(object sender, RoutedEventArgs e) => SaveFields();

    private void SaveFields()
    {
        if (_store is null || _filling)
        {
            return;
        }

        var c = SelectedCase;
        if (c.IsInbox)
        {
            return;
        }

        if (c.Client == ClientBox.Text.Trim() && c.Reference == ReferenceBox.Text.Trim()
            && c.Technician == TechnicianBox.Text.Trim() && c.Notes == NotesBox.Text.TrimEnd())
        {
            return;
        }

        c.Client = ClientBox.Text.Trim();
        c.Reference = ReferenceBox.Text.Trim();
        c.Technician = TechnicianBox.Text.Trim();
        c.Notes = NotesBox.Text.TrimEnd();
        _store.SaveCase(c);
        CaseTitle.Text = c.DisplayName;
        _filling = true;
        FillCases();
        _filling = false;
        if (c.Id == ActiveCaseId)
        {
            ActiveCaseChanged?.Invoke(c);
        }
    }

    private void DeleteCase_Click(object sender, RoutedEventArgs e)
    {
        var c = SelectedCase;
        if (c.IsInbox)
        {
            return;
        }

        _store!.DeleteCase(c);
        Log?.Invoke(LogKind.Info, $"Dossier « {c.DisplayName} » supprimé : ses analyses sont dans « Non classé ».");
        _selectedCaseId = ServiceCase.InboxId;
        if (ActiveCaseId == c.Id)
        {
            SetActive(_store.FindCase(ServiceCase.InboxId)!);
        }
        else
        {
            Refresh();
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (ReportRequested is not null)
        {
            await ReportRequested(SelectedCase);
        }
    }

    // ------------------------------------------------------------------ Entrées

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (SearchPlaceholder is not null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        }

        FillEntries();
    }

    /// <summary>Entrées du dossier sélectionné, après recherche et filtre.</summary>
    internal IReadOnlyList<HistoryEntry> VisibleEntries()
    {
        if (_store is null)
        {
            return [];
        }

        var kind = KindChips?.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "all";
        return [.. _store.EntriesOf(_selectedCaseId)
            .Where(e => kind switch
            {
                "image" => e.Kind == HistoryKind.ImageAnalysis,
                "compare" => e.Kind == HistoryKind.Comparison,
                "issues" => e.Worst >= Severity.Warning,
                _ => true,
            })
            .Where(e => HistorySearch.Matches(e, SearchBox?.Text))];
    }

    private void FillEntries()
    {
        if (_store is null || EntryList is null)
        {
            return;
        }

        var total = _store.EntriesOf(_selectedCaseId).Count;
        var shown = VisibleEntries();
        EntryList.ItemsSource = shown.Select(e => EntryView.From(e, _store, e.CaseId != ActiveCaseId)).ToList();
        CountText.Text = shown.Count == total ? $"{total} analyse{(total > 1 ? "s" : string.Empty)}" : $"{shown.Count} sur {total}";
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = total == 0
            ? "Aucune analyse dans ce dossier pour l'instant. Analyse une image ou compare une lecture : elle s'enregistre ici."
            : "Aucune analyse ne correspond à la recherche.";
    }

    private static HistoryEntry EntryOf(object sender) => ((EntryView)((FrameworkElement)sender).DataContext).Entry;

    private void Reopen_Click(object sender, RoutedEventArgs e) => ReopenRequested?.Invoke(EntryOf(sender));

    private void Move_Click(object sender, RoutedEventArgs e)
    {
        _store!.Move(EntryOf(sender), ActiveCaseId);
        Refresh();
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        _store!.Delete(EntryOf(sender));
        Refresh();
    }
}

/// <summary>Entrée prête à afficher.</summary>
public sealed record EntryView(HistoryEntry Entry, string Title, string Meta, string Detail, string Verdict, Severity Worst,
    BitmapImage? Thumb, string KindLabel, Visibility MoveVisibility)
{
    public Visibility NoThumbVisibility => Thumb is null ? Visibility.Visible : Visibility.Collapsed;

    public static EntryView From(HistoryEntry e, HistoryStore store, bool canMove)
    {
        var detail = e.Kind == HistoryKind.Comparison
            ? $"Attendu : {e.Expected}\nReçu : {(e.Received.Length == 0 ? "(rien)" : e.Received)}"
            : string.Join("\n", e.Codes.Take(3).Select(c => c.Text)) + (e.Codes.Count > 3 ? $"\n… et {e.Codes.Count - 3} autres" : string.Empty);
        return new EntryView(e, e.Title, $"{e.At:dd/MM/yyyy HH:mm} · {e.Source}", detail, ReportBuilder.Verdict(e), e.Worst,
            LoadThumb(store.ImagePath(e)), e.Kind == HistoryKind.Comparison ? "Comparaison" : "Image",
            canMove ? Visibility.Visible : Visibility.Collapsed);
    }

    private static BitmapImage? LoadThumb(string? path)
    {
        if (path is null)
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 256;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException or UriFormatException)
        {
            return null;
        }
    }
}
