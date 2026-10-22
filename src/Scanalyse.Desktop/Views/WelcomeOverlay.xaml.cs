using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop.Views;

/// <summary>Accueil du premier lancement : présentation, choix des dossiers, proposition de visite guidée.</summary>
public partial class WelcomeOverlay : UserControl
{
    private int _page;

    public WelcomeOverlay() => InitializeComponent();

    /// <summary>Dossier de l'historique en vigueur : fourni par la fenêtre principale.</summary>
    public Func<string>? HistoryFolder { get; set; }

    /// <summary>Changement du dossier de l'historique (copie de l'existant faite par la fenêtre principale).</summary>
    public event Func<string, Task>? HistoryFolderChangeRequested;

    /// <summary>Accueil terminé ; vrai si la visite guidée est demandée.</summary>
    public event Action<bool>? Finished;

    public void Show(int page = 0)
    {
        _page = page;
        Visibility = Visibility.Visible;
        Refresh();
        NextButton.Focus();
    }

    private void Refresh()
    {
        Page1.Visibility = _page == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page2.Visibility = _page == 1 ? Visibility.Visible : Visibility.Collapsed;
        Page3.Visibility = _page == 2 ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = _page switch { 0 => "Bienvenue dans Scanalyse", 1 => "Où ranger tes fichiers", _ => "Une visite guidée ?" };
        StepText.Text = $"{_page + 1} / 3";
        BackButton.Visibility = _page > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _page switch { 0 => "Commencer", 1 => "Continuer", _ => "Lancer la visite" };
        SkipButton.Content = _page == 2 ? "Plus tard" : "Passer";
        var s = AppSettings.Load();
        HistoryPath.Text = HistoryFolder?.Invoke() ?? string.Empty;
        ReportsPath.Text = s.ReportsFolderOrDefault;
        LabelsPath.Text = s.LabelsFolderOrDefault;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page < 2)
        {
            _page++;
            Refresh();
            return;
        }

        Close(tour: true);
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _page = Math.Max(0, _page - 1);
        Refresh();
    }

    private void Skip_Click(object sender, RoutedEventArgs e) => Close(tour: false);

    private void Close(bool tour)
    {
        var s = AppSettings.Load();
        s.WelcomeDone = true;
        s.Save();
        Visibility = Visibility.Collapsed;
        Finished?.Invoke(tour);
    }

    private static string? Pick(string title, string current)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = title, InitialDirectory = current };
        return dialog.ShowDialog(Application.Current.MainWindow) == true ? dialog.FolderName : null;
    }

    private async void PickHistory_Click(object sender, RoutedEventArgs e)
    {
        if (Pick("Dossier de l'historique Scanalyse (local ou réseau)", HistoryPath.Text) is { } folder && HistoryFolderChangeRequested is { } change)
        {
            await change(folder);
            Refresh();
        }
    }

    private void PickReports_Click(object sender, RoutedEventArgs e)
    {
        if (Pick("Dossier des rapports PDF", ReportsPath.Text) is { } folder)
        {
            var s = AppSettings.Load();
            s.ReportsFolder = folder;
            s.Save();
            Refresh();
        }
    }

    private void PickLabels_Click(object sender, RoutedEventArgs e)
    {
        if (Pick("Dossier des programmes d'étiquettes", LabelsPath.Text) is { } folder)
        {
            var s = AppSettings.Load();
            s.LabelsFolder = folder;
            s.Save();
            Refresh();
        }
    }
}
