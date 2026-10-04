using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace HumeMusic.Helpers;

/// <summary>
/// Lets a page's scrolling area show its content on to the window's left edge, under the glass
/// sidebar, while still cutting it off at the top, bottom and right as before. Horizontal rows reach
/// that far, so cards scrolled away slide beneath the sidebar instead of stopping at its edge.
/// </summary>
public static class PageClip
{
    private static readonly ConditionalWeakTable<ScrollViewer, object> Extended = [];

    public static void Extend(ScrollViewer page, double left)
    {
        if (Extended.TryGetValue(page, out var known) && known is double d && Math.Abs(d - left) < 0.5) return;
        Extended.AddOrUpdate(page, left);
        ScrollViewer.SetCanContentRenderOutsideBounds(page, true);
        var visual = ElementCompositionPreview.GetElementVisual(page);
        visual.Clip = visual.Compositor.CreateInsetClip(-(float)left, 0, 0, 0);
    }
}
