using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Scanalyse.Desktop;

/// <summary>
/// Texte d'aide d'un champ : confié au champ lui-même (<see cref="Placeholder"/>), qui le dessine à l'endroit exact du
/// texte saisi. Le texte d'aide d'origine reste dans la page (traduction, visibilité gérée par la vue) mais n'est plus
/// affiché : le champ reprend son texte, y compris après un changement de langue.
/// </summary>
internal static class HintAligner
{
    public static void Attach(TextBox box, FrameworkElement hint)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(hint);
        if (hint is TextBlock text)
        {
            BindingOperations.SetBinding(box, Placeholder.TextProperty, new Binding(nameof(TextBlock.Text)) { Source = text });
        }

        hint.Opacity = 0;
        hint.IsHitTestVisible = false;
    }
}
