using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scanalyse.Desktop.Views;

/// <summary>Étape de la visite guidée : un élément de la fenêtre et son explication.</summary>
/// <param name="Target">Élément mis en avant ; aucun pour une bulle au centre de la fenêtre (accueil).</param>
/// <param name="Title">Titre de la bulle.</param>
/// <param name="Text">Explication.</param>
/// <param name="Enter">Action faite en arrivant sur l'étape (ouvrir la page présentée), avant le placement.</param>
public sealed record TourStep(FrameworkElement? Target, string Title, string Text, Action? Enter = null);

/// <summary>Visite guidée en quelques bulles, affichée par-dessus la fenêtre principale.</summary>
public partial class TourOverlay : UserControl
{
    private IReadOnlyList<TourStep> _steps = [];
    private int _index;

    public TourOverlay() => InitializeComponent();

    /// <summary>Fin de la visite.</summary>
    public event Action? Ended;

    /// <summary>Vrai si la visite est allée jusqu'au bout (« C'est parti »), faux si elle a été quittée en route.</summary>
    public bool Completed { get; private set; }

    public void Start(IReadOnlyList<TourStep> steps)
    {
        _steps = steps;
        _index = 0;
        Completed = false;
        Visibility = Visibility.Visible;
        Go();
    }

    /// <summary>Ouvre la page de l'étape puis place la bulle une fois la page affichée et mise en page.</summary>
    private void Go()
    {
        _steps[_index].Enter?.Invoke();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Place();
            Focus();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void Place()
    {
        if (_steps.Count == 0 || ActualWidth < 10)
        {
            return;
        }

        var step = _steps[_index];
        // L'élément n'est pas un enfant de la visite : position calculée par rapport à elle via la fenêtre.
        var target = Rect.Empty;
        try
        {
            if (step.Target is { IsVisible: true, ActualWidth: > 0 } element)
            {
                target = element.TransformToVisual(this).TransformBounds(new Rect(element.RenderSize));
            }
        }
        catch (InvalidOperationException)
        {
            target = Rect.Empty;
        }

        // Ne pas sortir de la zone couverte (la barre de titre n'en fait pas partie).
        if (!target.IsEmpty)
        {
            target.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
        }

        var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var centered = target.IsEmpty;
        if (centered)
        {
            // Étape sans élément (accueil) ou élément invisible : tout est assombri, la bulle est au centre.
            Scrim.Data = all;
            Ring.Visibility = Visibility.Collapsed;
        }
        else
        {
            target.Inflate(6, 6);
            Scrim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, new RectangleGeometry(target, 10, 10));
            Ring.Visibility = Visibility.Visible;
            Canvas.SetLeft(Ring, target.X);
            Canvas.SetTop(Ring, target.Y);
            Ring.Width = target.Width;
            Ring.Height = target.Height;
        }

        BubbleTitle.Text = step.Title;
        BubbleText.Text = step.Text;
        CountText.Text = $"{_index + 1} / {_steps.Count}";
        PrevButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _index == _steps.Count - 1 ? "C'est parti" : "Suivant";

        // La bulle se place à droite de l'élément, sinon dessous, sinon au-dessus, sinon dans son coin bas droit
        // (élément qui occupe presque toute la fenêtre, comme une page) ; toujours dans la fenêtre.
        Bubble.Measure(new Size(Bubble.Width, double.PositiveInfinity));
        var bw = Bubble.Width;
        var bh = Bubble.DesiredSize.Height;
        double x, y;
        if (centered)
        {
            x = (ActualWidth - bw) / 2;
            y = (ActualHeight - bh) / 2;
        }
        else if (target.Right + 16 + bw < ActualWidth)
        {
            x = target.Right + 16;
            y = target.Top;
        }
        else if (target.Bottom + 16 + bh < ActualHeight)
        {
            x = target.Left;
            y = target.Bottom + 16;
        }
        else if (target.Top - 16 - bh > 0)
        {
            x = target.Left;
            y = target.Top - 16 - bh;
        }
        else
        {
            x = target.Right - bw - 24;
            y = target.Bottom - bh - 24;
        }

        Canvas.SetLeft(Bubble, Math.Clamp(x, 12, Math.Max(12, ActualWidth - bw - 12)));
        Canvas.SetTop(Bubble, Math.Clamp(y, 12, Math.Max(12, ActualHeight - bh - 12)));
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count - 1)
        {
            Completed = true;
            End();
            return;
        }

        _index++;
        Go();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _index = Math.Max(0, _index - 1);
        Go();
    }

    private void End_Click(object sender, RoutedEventArgs e) => End();

    private void End()
    {
        Visibility = Visibility.Collapsed;
        Ended?.Invoke();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsVisible)
        {
            Place();
        }
    }

    private void Root_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                End();
                e.Handled = true;
                break;
            case Key.Right or Key.Enter:
                Next_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Left:
                Prev_Click(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }
}
