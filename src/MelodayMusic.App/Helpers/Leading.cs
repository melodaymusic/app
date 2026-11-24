using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Helpers;

/// <summary>
/// Sets line spacing as a ratio of the font size, and keeps it in step when the size changes (a responsive or animated title).
/// A plain LineHeight below the font's own line gap is ignored, so this also switches to exact block line heights.
/// Text styles carry the ratio; pages set FontSize only.
/// </summary>
public static class Leading
{
    private static readonly ConditionalWeakTable<TextBlock, object> Hooked = new();

    public static readonly DependencyProperty RatioProperty = DependencyProperty.RegisterAttached(
        "Ratio", typeof(double), typeof(Leading), new PropertyMetadata(0.0, OnRatioChanged));
    public static double GetRatio(DependencyObject o) => (double)o.GetValue(RatioProperty);
    public static void SetRatio(DependencyObject o, double value) => o.SetValue(RatioProperty, value);

    private static void OnRatioChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        if (!Hooked.TryGetValue(block, out _))
        {
            Hooked.Add(block, new object());
            block.RegisterPropertyChangedCallback(TextBlock.FontSizeProperty, (s, _) => Apply((TextBlock)s));
        }
        Apply(block);
    }

    private static void Apply(TextBlock block)
    {
        var ratio = GetRatio(block);
        if (ratio <= 0)
        {
            block.ClearValue(TextBlock.LineStackingStrategyProperty);
            block.ClearValue(TextBlock.LineHeightProperty);
            return;
        }
        block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        block.LineHeight = Math.Round(block.FontSize * ratio);
    }
}
