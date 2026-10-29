using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public sealed class ActionWrapPanel : Panel
{
    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(ActionWrapPanel), new PropertyMetadata(8.0,
            (d, _) => ((ActionWrapPanel)d).InvalidateMeasure()));
    public double Spacing { get => (double)GetValue(SpacingProperty); set => SetValue(SpacingProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children)
            if (child.Visibility != Visibility.Collapsed)
                child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return Layout(availableSize.Width, false);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize.Width, true);
        return finalSize;
    }

    private const double Slack = 1.5;

    private Size Layout(double width, bool arrange)
    {
        double x = 0, y = 0, rowHeight = 0, usedWidth = 0;
        var gap = Services.UiScale.Size(Spacing);
        foreach (var child in Children)
        {
            if (child.Visibility == Visibility.Collapsed) continue;
            var size = child.DesiredSize;
            var next = x == 0 ? 0 : x + gap;
            if (next > 0 && next + size.Width > width + Slack)
            {
                y += rowHeight + gap;
                next = 0;
                rowHeight = 0;
            }
            if (arrange) child.Arrange(new Rect(next, y, size.Width, size.Height));
            x = next + size.Width;
            usedWidth = Math.Max(usedWidth, x);
            rowHeight = Math.Max(rowHeight, size.Height);
        }
        return new Size(Math.Ceiling(usedWidth), y + rowHeight);
    }
}
