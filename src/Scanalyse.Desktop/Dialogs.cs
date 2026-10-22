using System.Windows;

namespace Scanalyse.Desktop;

/// <summary>Accès aux confirmations du programme depuis n'importe quelle page.</summary>
internal static class Dialogs
{
    /// <inheritdoc cref="Views.ConfirmOverlay.AskAsync"/>
    public static Task<bool> ConfirmAsync(string title, string message, string confirmText, string? note = null, bool danger = true) =>
        Application.Current?.MainWindow is MainWindow w
            ? w.Confirm.AskAsync(title, message, confirmText, note, danger)
            : Task.FromResult(false);
}
