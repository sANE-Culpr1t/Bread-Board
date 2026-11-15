using System.Windows;
using System.Windows.Controls;

namespace BreadBoard.App.Controls;

/// <summary>
/// Responsive grid: as many columns of at least <see cref="MinItemWidth"/> as fit, items stretched
/// to fill the row, fixed item height. Used for the sound pads.
/// </summary>
public sealed class AdaptiveGrid : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(170.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(136.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(AdaptiveGrid), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    private (int Cols, double ItemWidth) Layout(double width)
    {
        if (double.IsInfinity(width)) width = MinItemWidth * 4 + Spacing * 3;
        var cols = Math.Max(1, (int)((width + Spacing) / (MinItemWidth + Spacing)));
        return (cols, Math.Max(0, (width - Spacing * (cols - 1)) / cols));
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var (cols, w) = Layout(availableSize.Width);
        foreach (UIElement c in InternalChildren) c.Measure(new Size(w, ItemHeight));
        var visible = InternalChildren.Cast<UIElement>().Count(c => c.Visibility != Visibility.Collapsed);
        var rows = (visible + cols - 1) / cols;
        var width = double.IsInfinity(availableSize.Width) ? cols * w + (cols - 1) * Spacing : availableSize.Width;
        return new Size(width, rows == 0 ? 0 : rows * ItemHeight + (rows - 1) * Spacing);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var (cols, w) = Layout(finalSize.Width);
        var i = 0;
        foreach (UIElement c in InternalChildren)
        {
            if (c.Visibility == Visibility.Collapsed) continue;
            var col = i % cols;
            var row = i / cols;
            c.Arrange(new Rect(col * (w + Spacing), row * (ItemHeight + Spacing), w, ItemHeight));
            i++;
        }
        return finalSize;
    }
}

