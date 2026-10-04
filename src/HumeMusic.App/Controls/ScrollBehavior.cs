using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace HumeMusic.Controls;

/// <summary>
/// Keeps horizontally scrolling rows from hijacking vertical page scrolling.
/// A horizontal-only ScrollViewer normally converts vertical mouse-wheel input into horizontal
/// movement; with this behaviour an ordinary vertical wheel notch scrolls the enclosing vertical
/// scroller instead. Shift+wheel, tilt-wheel (horizontal wheel) and touchpad/touch horizontal
/// gestures still scroll the row itself.
/// </summary>
public static class ScrollBehavior
{
    public static readonly DependencyProperty PassVerticalWheelProperty = DependencyProperty.RegisterAttached(
        "PassVerticalWheel", typeof(bool), typeof(ScrollBehavior), new PropertyMetadata(false, OnChanged));

    public static bool GetPassVerticalWheel(DependencyObject o) => (bool)o.GetValue(PassVerticalWheelProperty);
    public static void SetPassVerticalWheel(DependencyObject o, bool value) => o.SetValue(PassVerticalWheelProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        // Handled on the way *into* the row's scroller: the element must sit inside that scroller
        // (e.g. the items panel) so the row's ScrollViewer sees the event as already handled.
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
        // Always consume vertical wheel input here so the row never turns it into horizontal movement.
        e.Handled = true;
        if (outer is null) return;

        var delta = props.MouseWheelDelta;
        // Successive notches accumulate onto the pending target so fast wheel spins are not lost
        // while a previous animated scroll is still running.
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
