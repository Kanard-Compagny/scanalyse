using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop;

/// <summary>
/// Place le texte d'aide d'un champ (« ex. 192.168.1.50 », « Rechercher… ») exactement là où le texte saisi apparaîtra,
/// en le mesurant dans le champ lui-même : le curseur tombe pile au début de l'aide.
/// </summary>
internal static class HintAligner
{
    public static void Attach(TextBox box, FrameworkElement hint)
    {
        hint.HorizontalAlignment = HorizontalAlignment.Left;
        hint.VerticalAlignment = VerticalAlignment.Top;

        void Align()
        {
            if (!box.IsLoaded || !hint.IsLoaded || hint.Parent is not UIElement parent)
            {
                return;
            }

            try
            {
                var r = box.GetRectFromCharacterIndex(0);
                if (r.IsEmpty || double.IsInfinity(r.Top))
                {
                    return;
                }

                var p = box.TranslatePoint(new Point(r.Left, r.Top), parent);
                hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var top = p.Y + Math.Max(0, (r.Height - hint.DesiredSize.Height) / 2);
                hint.Margin = new Thickness(p.X, top, 0, 0);
            }
            catch (InvalidOperationException)
            {
                // Mise en page pas prête : on réessaiera.
            }
        }

        void Later() => box.Dispatcher.BeginInvoke(new Action(Align), System.Windows.Threading.DispatcherPriority.Loaded);
        box.Loaded += (_, _) => Later();
        box.SizeChanged += (_, _) => Later();
        hint.Loaded += (_, _) => Later();
        box.IsVisibleChanged += (_, _) => Later();
    }
}
