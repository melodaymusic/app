using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace MelodayMusic.Controls;

public static class ScrollBehavior
{
    public static readonly DependencyProperty PassVerticalWheelProperty = DependencyProperty.RegisterAttached(
        "PassVerticalWheel", typeof(bool), typeof(ScrollBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetPassVerticalWheel(DependencyObject o) => (bool)o.GetValue(PassVerticalWheelProperty);
    public static void SetPassVerticalWheel(DependencyObject o, bool value) => o.SetValue(PassVerticalWheelProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        if ((bool)e.NewValue) element.PointerWheelChanged += OnWheel;
        else element.PointerWheelChanged -= OnWheel;
    }

    private static void OnWheel(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((UIElement)sender);
        var props = point.Properties;
        if (props.IsHorizontalMouseWheel) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift) return;

        var outer = FindVerticalScroller((DependencyObject)sender);
        e.Handled = true;
        if (outer is null) return;

        var delta = props.MouseWheelDelta;
        var now = Environment.TickCount64;
        var baseOffset = Pending.TryGetValue(outer, out var pending) && now - pending.Time < 350 ? pending.Target : outer.VerticalOffset;
        var target = Math.Clamp(baseOffset - delta, 0, outer.ScrollableHeight);
        Pending.AddOrUpdate(outer, new PendingScroll(target, now));
        outer.ChangeView(null, target, null, disableAnimation: !Helpers.Motion.IsEnabled || Math.Abs(delta) < 120);
        e.Handled = true;
    }

    private sealed record PendingScroll(double Target, long Time);

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ScrollViewer, PendingScroll> Pending = new();

    private static ScrollViewer? FindVerticalScroller(DependencyObject start)
    {
        for (var p = VisualTreeHelper.GetParent(start); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ScrollViewer sv && sv.VerticalScrollMode != ScrollMode.Disabled && sv.ScrollableHeight > 0)
                return sv;
        }
        return null;
    }
}
