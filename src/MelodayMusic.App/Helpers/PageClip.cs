using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace MelodayMusic.Helpers;

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
