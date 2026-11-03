using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop;

/// <summary>
/// Barre « Imprimante » en fenêtre étroite : le champ d'adresse (ou le choix de l'imprimante installée) passe sur sa
/// propre ligne, en pleine largeur, au lieu d'être écrasé entre les puces et les boutons.
/// </summary>
internal static class ResponsiveTarget
{
    /// <summary>Place <paramref name="area"/> dans <paramref name="wide"/> ou <paramref name="narrow"/> selon la largeur de <paramref name="card"/>.</summary>
    /// <returns>De quoi replacer la zone quand la barre change (un bouton qui apparaît ou disparaît).</returns>
    public static Action Attach(FrameworkElement card, Decorator wide, Decorator narrow, FrameworkElement area, double threshold = 900, Func<double>? extra = null)
    {
        void Place()
        {
            var compact = card.ActualWidth > 0 && card.ActualWidth < threshold + (extra?.Invoke() ?? 0);
            var target = compact ? narrow : wide;
            if (ReferenceEquals(area.Parent, target))
            {
                return;
            }

            if (area.Parent is Decorator current)
            {
                current.Child = null;
            }

            target.Child = area;
            narrow.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        }

        card.SizeChanged += (_, _) => Place();
        card.Loaded += (_, _) => Place();
        return Place;
    }
}
