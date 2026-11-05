using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Scanalyse.Desktop;

/// <summary>
/// Taille de l'interface (Paramètres) : 80 %, 90 %, 100 %, 115 % ou 130 %. Une seule valeur pour tout le programme ; les
/// éléments hors de la fenêtre principale (listes surgissantes, fenêtres détachées) la suivent aussi, en direct.
/// </summary>
internal static class UiScale
{
    /// <summary>Tailles proposées.</summary>
    public static IReadOnlyList<double> Choices { get; } = [0.8, 0.9, 1, 1.15, 1.3];

    /// <summary>Taille en cours (1 = 100 %).</summary>
    public static double Value { get; private set; } = 1;

    /// <summary>La taille a changé (Paramètres).</summary>
    public static event Action? Changed;

    /// <summary>Ramène une valeur enregistrée à la taille proposée la plus proche (fichier ancien ou modifié à la main).</summary>
    public static double Normalize(double value) =>
        double.IsFinite(value) ? Choices.MinBy(c => Math.Abs(c - value)) : 1;

    public static void Set(double value)
    {
        value = Normalize(value);
        if (Math.Abs(value - Value) < 0.001)
        {
            return;
        }

        Value = value;
        if (Application.Current is { } app)
        {
            // Bulles d'aide et menus contextuels (styles du thème) : ressource remplacée, ils suivent tous.
            var t = new ScaleTransform(value, value);
            t.Freeze();
            app.Resources["UiScaleTransform"] = t;
        }

        Changed?.Invoke();
    }

    /// <summary>Transformation à poser sur un élément (aucune à 100 %, pour ne rien changer à l'affichage habituel).</summary>
    public static Transform Transform => Math.Abs(Value - 1) < 0.001 ? System.Windows.Media.Transform.Identity : new ScaleTransform(Value, Value);

    /// <summary>L'élément prend la taille de l'interface, maintenant et à chaque changement, tant qu'il est chargé.</summary>
    public static void Follow(FrameworkElement element)
    {
        void Apply() => element.LayoutTransform = Transform;
        Apply();
        element.Loaded += (_, _) =>
        {
            Changed -= Apply;
            Changed += Apply;
            Apply();
        };
        element.Unloaded += (_, _) => Changed -= Apply;
        if (element.IsLoaded)
        {
            Changed += Apply;
        }
    }

    /// <summary>Le contenu d'une liste surgissante suit la taille de l'interface (il n'hérite pas de celle de la page).</summary>
    public static void Follow(Popup popup)
    {
        if (popup.Child is FrameworkElement child)
        {
            child.LayoutTransform = Transform;
            Changed += () => child.LayoutTransform = Transform;
        }
    }
}
