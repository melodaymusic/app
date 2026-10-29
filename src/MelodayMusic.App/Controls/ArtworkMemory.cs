using Microsoft.UI.Xaml.Media.Imaging;

namespace MelodayMusic.Controls;

internal static class ArtworkMemory
{
    private const long Budget = 16_000_000;

    private sealed record Entry(string Path, int Size, BitmapImage Bitmap)
    {
        public long Pixels => (long)Size * Size;
    }

    private static readonly LinkedList<Entry> Recent = new();
    private static readonly Dictionary<string, List<LinkedListNode<Entry>>> ByPath = new(StringComparer.Ordinal);
    private static long _pixels;

    public static BitmapImage? Find(string path, int size)
    {
        if (!ByPath.TryGetValue(path, out var nodes)) return null;
        LinkedListNode<Entry>? best = null;
        foreach (var node in nodes)
            if (node.Value.Size >= size && (best is null || node.Value.Size < best.Value.Size)) best = node;
        if (best is null) return null;
        Recent.Remove(best);
        Recent.AddFirst(best);
        return best.Value.Bitmap;
    }

    public static void Keep(string path, int size, BitmapImage bitmap)
    {
        if (!ByPath.TryGetValue(path, out var nodes)) ByPath[path] = nodes = [];
        if (nodes.Any(n => n.Value.Size == size)) return;
        var node = Recent.AddFirst(new Entry(path, size, bitmap));
        nodes.Add(node);
        _pixels += node.Value.Pixels;
        while (_pixels > Budget && Recent.Last is { } oldest && !ReferenceEquals(oldest, node)) Drop(oldest);
    }

    public static void Clear()
    {
        Recent.Clear();
        ByPath.Clear();
        _pixels = 0;
    }

    private static void Drop(LinkedListNode<Entry> node)
    {
        Recent.Remove(node);
        _pixels -= node.Value.Pixels;
        if (ByPath.TryGetValue(node.Value.Path, out var nodes))
        {
            nodes.Remove(node);
            if (nodes.Count == 0) ByPath.Remove(node.Value.Path);
        }
    }
}
