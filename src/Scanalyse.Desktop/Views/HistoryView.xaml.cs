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
        var saved = AppSettings.Load().ReportContent;
        foreach (var chip in ReportContentChips.Children.OfType<RadioButton>())
        {
            chip.IsChecked = (string)chip.Tag == saved;
        }
    }

    /// <summary>Niveau de détail choisi pour le rapport (mémorisé).</summary>
    public ReportContent ReportContent =>
        Enum.TryParse<ReportContent>(ReportContentChips.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string, out var c) ? c : ReportContent.Both;

    private void ReportContent_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        var settings = AppSettings.Load();
        settings.ReportContent = (string)((RadioButton)sender).Tag;
        settings.Save();
    }

    /// <summary>Dossier en cours : les nouvelles analyses y sont enregistrées.</summary>
    public string ActiveCaseId { get; private set; } = ServiceCase.InboxId;

    /// <summary>Le dossier en cours a changé (ou a été renommé).</summary>
    public event Action<ServiceCase>? ActiveCaseChanged;

    /// <summary>Rouvrir une entrée (image dans Analyser, comparaison au banc d'essai).</summary>
    public event Action<HistoryEntry>? ReopenRequested;

    /// <summary>Générer le rapport d'un dossier (fourni par la fenêtre principale).</summary>
    /// <remarks>Second paramètre : rapport de tous les dossiers du même client.</remarks>
    public Func<ServiceCase, bool, Task>? ReportRequested { get; set; }

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
        var sameClient = c.IsInbox ? 0 : _store!.CasesOfClient(c.Client).Count;
        ClientReportButton.Visibility = sameClient > 1 ? Visibility.Visible : Visibility.Collapsed;
        ClientReportButton.Content = $"Rapport client ({sameClient} dossiers)";
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

    private async void DeleteCase_Click(object sender, RoutedEventArgs e)
    {
        var c = SelectedCase;
        if (c.IsInbox)
        {
            return;
        }

        var count = _store!.EntriesOf(c.Id).Count;
        if (!await Dialogs.ConfirmAsync(
                "Supprimer ce dossier ?",
                $"Le dossier « {c.DisplayName} » et sa fiche (client, référence, intervenant, notes) seront supprimés."
                + (count > 0 ? $" Ses {count} analyse{(count > 1 ? "s" : string.Empty)} ne sont pas perdues : elles reviennent dans « Non classé »." : string.Empty),
                "Supprimer le dossier",
                "La fiche du dossier ne pourra pas être récupérée."))
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

    /// <summary>« Vider le dossier » : toutes ses analyses et leurs images sont supprimées définitivement.</summary>
    private async void EmptyCase_Click(object sender, RoutedEventArgs e)
    {
        var c = SelectedCase;
        var count = _store!.EntriesOf(c.Id).Count;
        if (count == 0)
        {
            Log?.Invoke(LogKind.Info, $"Le dossier « {c.DisplayName} » est déjà vide.");
            return;
        }

        if (!await Dialogs.ConfirmAsync(
                "Vider ce dossier ?",
                $"Les {count} analyse{(count > 1 ? "s" : string.Empty)} du dossier « {c.DisplayName} » seront supprimées, avec leurs images, noms et commentaires. Le dossier et sa fiche sont conservés.",
                count > 1 ? $"Supprimer les {count} analyses" : "Supprimer l'analyse",
                "Action irréversible : les analyses supprimées ne pourront pas être récupérées."))
        {
            return;
        }

        var deleted = _store.DeleteEntriesOf(c.Id);
        Log?.Invoke(LogKind.Info, $"Dossier « {c.DisplayName} » vidé : {deleted} analyse{(deleted > 1 ? "s" : string.Empty)} supprimée{(deleted > 1 ? "s" : string.Empty)}.");
        Refresh();
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (ReportRequested is not null)
        {
            await ReportRequested(SelectedCase, false);
        }
    }

    private async void ClientReport_Click(object sender, RoutedEventArgs e)
    {
        SaveFields();
        if (ReportRequested is not null)
        {
            await ReportRequested(SelectedCase, true);
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
        EntryList.ItemsSource = shown.Select(e => EntryView.From(e, _store)).ToList();
        CountText.Text = shown.Count == total ? $"{total} analyse{(total > 1 ? "s" : string.Empty)}" : $"{shown.Count} sur {total}";
        EmptyText.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = total == 0
            ? "Aucune analyse dans ce dossier pour l'instant. Analyse une image ou compare une lecture : elle s'enregistre ici."
            : "Aucune analyse ne correspond à la recherche.";
    }

    private static HistoryEntry EntryOf(object sender) => ((EntryView)((FrameworkElement)sender).DataContext).Entry;

    private void Reopen_Click(object sender, RoutedEventArgs e) => ReopenRequested?.Invoke(EntryOf(sender));

    /// <summary>« Déplacer vers… » : tous les dossiers, plus la création d'un nouveau dossier.</summary>
    private void Move_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryOf(sender);
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var c in _store!.Cases.Where(c => c.Id != entry.CaseId))
        {
            var item = new MenuItem { Header = c.Id == ActiveCaseId ? $"{c.DisplayName}  (en cours)" : c.DisplayName };
            item.Click += (_, _) => MoveTo(entry, c);
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var create = new MenuItem { Header = "Nouveau dossier…" };
        create.Click += (_, _) =>
        {
            var previous = _store.FindCase(ActiveCaseId);
            var c = new ServiceCase { Technician = previous?.Technician ?? string.Empty };
            _store.SaveCase(c);
            MoveTo(entry, c);
            _selectedCaseId = c.Id;
            SetActive(c);
            Dispatcher.InvokeAsync(() => ClientBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
        };
        menu.Items.Add(create);
        menu.IsOpen = true;
    }

    private void MoveTo(HistoryEntry entry, ServiceCase target)
    {
        _store!.Move(entry, target.Id);
        Log?.Invoke(LogKind.Info, $"« {entry.Title} » rangé dans {target.DisplayName}.");
        Refresh();
    }

    /// <summary>Renommer : un nom vide ou identique au nom automatique rend le nom automatique.</summary>
    private void Title_LostFocus(object sender, RoutedEventArgs e)
    {
        var box = (TextBox)sender;
        var entry = EntryOf(sender);
        var text = box.Text.Trim();
        var custom = text.Length == 0 || text == entry.AutoTitle ? string.Empty : text;
        if (custom != entry.CustomTitle)
        {
            entry.CustomTitle = custom;
            _store!.Update(entry);
        }

        box.Text = entry.Title;
    }

    private void Comment_LostFocus(object sender, RoutedEventArgs e)
    {
        var entry = EntryOf(sender);
        var text = ((TextBox)sender).Text.TrimEnd();
        if (text != entry.Comment)
        {
            entry.Comment = text;
            _store!.Update(entry);
        }
    }

    /// <summary>Entrée valide le nom ; Échap annule la modification.</summary>
    private void Inline_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == System.Windows.Input.Key.Escape)
        {
            box.Text = EntryOf(sender).Title;
        }

        if (e.Key is System.Windows.Input.Key.Enter or System.Windows.Input.Key.Escape)
        {
            box.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next));
            e.Handled = true;
        }
    }

    private void InReport_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryOf(sender);
        entry.InReport = ((CheckBox)sender).IsChecked == true;
        _store!.Update(entry);
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        var entry = EntryOf(sender);
        if (!await Dialogs.ConfirmAsync(
                "Supprimer cette analyse ?",
                $"« {entry.Title} » du {entry.At:dd/MM/yyyy à HH:mm} sera supprimée, avec son image, son nom et son commentaire.",
                "Supprimer l'analyse",
                "Action irréversible."))
        {
            return;
        }

        _store!.Delete(entry);
        Refresh();
    }
}

/// <summary>Entrée prête à afficher.</summary>
public sealed record EntryView(HistoryEntry Entry, string Title, string Meta, string Detail, string Verdict, Severity Worst,
    BitmapImage? Thumb, string KindLabel)
{
    public bool InReport => Entry.InReport;

    public Visibility NoThumbVisibility => Thumb is null ? Visibility.Visible : Visibility.Collapsed;

    public static EntryView From(HistoryEntry e, HistoryStore store)
    {
        var detail = e.Kind == HistoryKind.Comparison
            ? $"Attendu : {e.Expected}\nReçu : {(e.Received.Length == 0 ? "(rien)" : e.Received)}"
            : string.Join("\n", e.Codes.Take(3).Select(c => c.Text)) + (e.Codes.Count > 3 ? $"\n… et {e.Codes.Count - 3} autres" : string.Empty);
        return new EntryView(e, e.Title, $"{e.At:dd/MM/yyyy HH:mm} · {e.Source}", detail, ReportBuilder.Verdict(e), e.Worst,
            LoadThumb(store.DisplayImagePath(e)), e.Kind == HistoryKind.Comparison ? "Comparaison" : "Image");
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
