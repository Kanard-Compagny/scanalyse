using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Scanalyse.Desktop.Views;

/// <summary>
/// Demande de confirmation dans la fenêtre du programme : titre, explication, conséquence irréversible
/// en rouge, « Annuler » sélectionné par défaut (Entrée n'efface jamais rien par mégarde), Échap pour annuler.
/// </summary>
public partial class ConfirmOverlay : UserControl
{
    private TaskCompletionSource<bool>? _pending;

    public ConfirmOverlay()
    {
        InitializeComponent();
    }

    /// <summary>Affiche la demande et attend la réponse (<c>true</c> : confirmé).</summary>
    /// <param name="title">Question courte (« Supprimer ce dossier ? »).</param>
    /// <param name="message">Ce qui va se passer.</param>
    /// <param name="confirmText">Libellé du bouton de confirmation (« Supprimer »).</param>
    /// <param name="note">Conséquence à souligner (« Action irréversible »), ou <c>null</c>.</param>
    /// <param name="danger">Action destructrice : bouton rouge ; sinon bouton d'action normal.</param>
    public Task<bool> AskAsync(string title, string message, string confirmText, string? note = null, bool danger = true)
    {
        _pending?.TrySetResult(false);
        _pending = new TaskCompletionSource<bool>();
        TitleText.Text = title;
        MessageText.Text = message;
        NoteText.Text = note ?? string.Empty;
        NoteBox.Visibility = string.IsNullOrWhiteSpace(note) ? Visibility.Collapsed : Visibility.Visible;
        ConfirmButton.Content = confirmText;
        ConfirmButton.Style = (Style)FindResource(danger ? "BtnDanger" : "BtnAction");
        IconBox.SetResourceReference(Border.BackgroundProperty, danger ? "BadBg" : "WarnBg");
        IconPath.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, danger ? "BadFg" : "WarnFg");
        Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(() => CancelButton.Focus(), DispatcherPriority.Input);
        return _pending.Task;
    }

    private void Close(bool result)
    {
        Visibility = Visibility.Collapsed;
        var p = _pending;
        _pending = null;
        p?.TrySetResult(result);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(false);

    private void Scrim_MouseDown(object sender, MouseButtonEventArgs e) => Close(false);

    private void Card_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }
}
