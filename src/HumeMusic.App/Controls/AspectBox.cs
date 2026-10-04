using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace HumeMusic.Controls;

/// <summary>
/// Sizes its content to a fixed aspect ratio of the width it is given (square by default).
/// The height is worked out inside the layout pass itself; setting heights from SizeChanged
/// instead feeds sizes back into layout and can end in a layout cycle.
/// </summary>
public sealed partial class AspectBox : Panel
{
    public static readonly DependencyProperty RatioProperty = DependencyProperty.Register(
        nameof(Ratio), typeof(double), typeof(AspectBox), new PropertyMetadata(1.0, (d, _) => ((AspectBox)d).InvalidateMeasure()));

    /// <summary>Height as a fraction of width (1 = square, 0.5625 = 16:9).</summary>
    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Without a width constraint there is nothing to derive the height from.
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
