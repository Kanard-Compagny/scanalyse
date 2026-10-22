using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>
/// Défilement enchaîné : quand la molette tourne au-dessus d'une zone qui défile déjà (liste, programme…) et que celle-ci
/// est arrivée en haut ou en bas, c'est la page entière qui défile. Sans ça, la molette « bloque » et on n'atteint plus
/// le bas (ou le haut) de la page quand la fenêtre est petite.
/// </summary>
internal static class ScrollChaining
{
    public static void Enable(ScrollViewer page)
    {
        page.PreviewMouseWheel += (_, e) =>
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0 || e.Delta == 0)
            {
                return; // Ctrl + molette : zoom des aperçus.
            }

            var up = e.Delta > 0;
            var d = e.OriginalSource as DependencyObject;
            while (d is not null && !ReferenceEquals(d, page))
            {
                if (d is ScrollViewer inner && inner.ScrollableHeight > 0.5
                    && (up ? inner.VerticalOffset > 0.5 : inner.VerticalOffset < inner.ScrollableHeight - 0.5))
                {
                    return; // La zone sous la souris peut encore défiler : on la laisse faire.
                }

                d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }

            if (d is null)
            {
                return; // Fenêtre surgissante (liste d'imprimantes…) : pas dans la page.
            }

            if (page.ScrollableHeight > 0)
            {
                page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta / 120.0 * 48);
            }

            e.Handled = true;
        };
    }
}
