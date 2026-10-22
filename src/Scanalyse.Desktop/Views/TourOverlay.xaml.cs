using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Scanalyse.Desktop.Views;

/// <summary>Étape de la visite guidée : un élément de la fenêtre et son explication.</summary>
/// <param name="Target">Élément mis en avant.</param>
/// <param name="Title">Titre de la bulle.</param>
/// <param name="Text">Explication.</param>
public sealed record TourStep(FrameworkElement Target, string Title, string Text);

/// <summary>Visite guidée en quelques bulles, affichée par-dessus la fenêtre principale.</summary>
public partial class TourOverlay : UserControl
{
    private IReadOnlyList<TourStep> _steps = [];
    private int _index;

    public TourOverlay() => InitializeComponent();

    /// <summary>Fin de la visite.</summary>
    public event Action? Ended;

    public void Start(IReadOnlyList<TourStep> steps)
    {
        _steps = steps;
        _index = 0;
        Visibility = Visibility.Visible;
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
        Rect target;
        try
        {
            target = step.Target.TransformToAncestor(this).TransformBounds(new Rect(step.Target.RenderSize));
        }
        catch (InvalidOperationException)
        {
            target = new Rect(ActualWidth / 2 - 50, ActualHeight / 2 - 20, 100, 40);
        }

        target.Inflate(6, 6);
        var all = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight));
        var hole = new RectangleGeometry(target, 10, 10);
        Scrim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, all, hole);
        Canvas.SetLeft(Ring, target.X);
        Canvas.SetTop(Ring, target.Y);
        Ring.Width = target.Width;
        Ring.Height = target.Height;

        BubbleTitle.Text = step.Title;
        BubbleText.Text = step.Text;
        CountText.Text = $"{_index + 1} / {_steps.Count}";
        PrevButton.Visibility = _index > 0 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Content = _index == _steps.Count - 1 ? "C'est parti" : "Suivant";

        // La bulle se place à droite de l'élément, sinon dessous, sinon au-dessus ; toujours dans la fenêtre.
        Bubble.Measure(new Size(Bubble.Width, double.PositiveInfinity));
        var bw = Bubble.Width;
        var bh = Bubble.DesiredSize.Height;
        double x, y;
        if (target.Right + 16 + bw < ActualWidth)
        {
            x = target.Right + 16;
            y = target.Top;
        }
        else if (target.Bottom + 16 + bh < ActualHeight)
        {
            x = target.Left;
            y = target.Bottom + 16;
        }
        else
        {
            x = target.Left;
            y = target.Top - 16 - bh;
        }

        Canvas.SetLeft(Bubble, Math.Clamp(x, 12, Math.Max(12, ActualWidth - bw - 12)));
        Canvas.SetTop(Bubble, Math.Clamp(y, 12, Math.Max(12, ActualHeight - bh - 12)));
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_index >= _steps.Count - 1)
        {
            End();
            return;
        }

        _index++;
        Place();
    }

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        _index = Math.Max(0, _index - 1);
        Place();
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
