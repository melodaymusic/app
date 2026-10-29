using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public sealed partial class AspectBox : Panel
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectBox), new PropertyMetadata(1.0, (d, _) => ((AspectBox)d).InvalidateMeasure()));

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        var size = new Size(width, Math.Round(width * Ratio));
        foreach (var child in Children) child.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = new Size(finalSize.Width, Math.Round(finalSize.Width * Ratio));
        foreach (var child in Children) child.Arrange(new Rect(0, 0, size.Width, size.Height));
        return size;
    }
}
