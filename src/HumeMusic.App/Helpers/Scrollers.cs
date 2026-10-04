using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Helpers;

public static class Scrollers
{
    /// <summary>The element itself if it is a ScrollViewer, else the first one inside it (lists and grids).</summary>
    public static ScrollViewer? Find(DependencyObject? root)
    {
        if (root is null) return null;
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer sv) return sv;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    /// <summary>
    /// Returns a page to where it was scrolled once its content is laid out (a restored page lays
    /// out over a few passes, so this retries until the content is tall enough or gives up).
    /// </summary>
    public static void Restore(FrameworkElement source, double offset)
    {
        if (offset <= 0) return;
        var attempts = 0;
        void Try()
        {
            if (Find(source) is { } sv && sv.ScrollableHeight >= offset - 1)
            {
                sv.ChangeView(null, offset, null, disableAnimation: true);
                return;
            }
            if (++attempts < 20) source.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Try);
        }
        source.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Try);
    }
}
