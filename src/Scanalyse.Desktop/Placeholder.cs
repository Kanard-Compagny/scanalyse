using System.Windows;

namespace Scanalyse.Desktop;

/// <summary>
/// Texte d'aide d'un champ (« ex. 192.168.1.50 », « Rechercher… »), dessiné par le modèle du champ lui-même, exactement
/// là où le texte saisi apparaît : même police, même marge, même alignement. Le curseur tombe pile au début de l'aide,
/// quels que soient la taille du champ, sa police ou son état (masqué, redimensionné).
/// </summary>
public static class Placeholder
{
    /// <summary>Texte d'aide affiché tant que le champ est vide.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Placeholder), new FrameworkPropertyMetadata(string.Empty));

    /// <summary>Lit le texte d'aide.</summary>
    public static string GetText(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (string)element.GetValue(TextProperty);
    }

    /// <summary>Change le texte d'aide.</summary>
    public static void SetText(DependencyObject element, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(TextProperty, value);
    }
}
