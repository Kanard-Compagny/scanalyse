using System.Windows;
using System.Windows.Controls;

namespace Scanalyse.Desktop;

/// <summary>
/// Range ses éléments en colonnes selon la largeur disponible (1 à <see cref="MaxColumns"/>), chacun sous la colonne
/// la moins haute, dans l'ordre : les cartes des Paramètres occupent ainsi un grand écran sans laisser de vide.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(
        nameof(ColumnWidth), typeof(double), typeof(MasonryPanel), new FrameworkPropertyMetadata(520.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(MasonryPanel), new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(MasonryPanel), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Largeur minimale d'une colonne.</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>Nombre maximal de colonnes.</summary>
    public int MaxColumns
    {
        get => (int)GetValue(MaxColumnsProperty);
        set => SetValue(MaxColumnsProperty, value);
    }

    /// <summary>Écart entre les cartes.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private readonly List<Rect> _slots = [];

    private int Columns(double width) =>
        double.IsInfinity(width) ? 1 : Math.Clamp((int)((width + Spacing) / (ColumnWidth + Spacing)), 1, Math.Max(1, MaxColumns));

    protected override Size MeasureOverride(Size availableSize)
    {
        var n = Columns(availableSize.Width);
        var width = double.IsInfinity(availableSize.Width) ? ColumnWidth : availableSize.Width;
        var colWidth = (width - (n - 1) * Spacing) / n;
        var heights = new double[n];
        _slots.Clear();
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                _slots.Add(Rect.Empty);
                continue;
            }

            child.Measure(new Size(colWidth, double.PositiveInfinity));
            var col = Array.IndexOf(heights, heights.Min());
            _slots.Add(new Rect(col * (colWidth + Spacing), heights[col], colWidth, child.DesiredSize.Height));
            heights[col] += child.DesiredSize.Height + Spacing;
        }

        return new Size(width, Math.Max(0, heights.Max() - Spacing));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (_slots.Count != InternalChildren.Count)
        {
            MeasureOverride(finalSize);
        }

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var slot = _slots[i];
            InternalChildren[i].Arrange(slot.IsEmpty ? new Rect(0, 0, 0, 0) : slot);
        }

        return finalSize;
    }
}
