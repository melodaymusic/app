namespace HumeMusic.Services;

/// <summary>
/// Keeps the loaded view models of recently visited detail pages (artists, albums, playlists,
/// collections) so going back to one shows it instantly, where it was left, instead of loading it
/// again. Holds a bounded number of pages and lets entries expire so data doesn't go stale.
/// </summary>
public sealed class DetailCache
{
    private const int Capacity = 16;
    // Going back shows the page as it was left for a good while; opening it anew wants fresher data.
    private static readonly TimeSpan BackLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private sealed class Entry(object viewModel)
    {
        public object ViewModel { get; } = viewModel;
        public DateTime LoadedAt { get; set; } = DateTime.UtcNow;
        public double ScrollOffset { get; set; }
    }

    private readonly LinkedList<(string Key, Entry Entry)> _entries = new();

    /// <summary>The cached view model for a page, or a new one (<paramref name="isNew"/>) that the page must load.</summary>
    public T Get<T>(string key, Func<T> create, out bool isNew, bool goingBack = false) where T : class
    {
        var node = Find(key);
        if (node is not null && node.Value.Entry.ViewModel is T cached && DateTime.UtcNow - node.Value.Entry.LoadedAt < (goingBack ? BackLifetime : Lifetime))
        {
            _entries.Remove(node);
            _entries.AddFirst(node);
            isNew = false;
            return cached;
        }
        if (node is not null) _entries.Remove(node);
        var created = create();
        _entries.AddFirst((key, new Entry(created)));
        while (_entries.Count > Capacity) _entries.RemoveLast();
        isNew = true;
        return created;
    }

    /// <summary>How long ago a page's data was loaded (or last confirmed fresh).</summary>
    public TimeSpan Age(string key) => Find(key) is { } node ? DateTime.UtcNow - node.Value.Entry.LoadedAt : TimeSpan.MaxValue;

    /// <summary>Records that a page's data was just refreshed.</summary>
    public void MarkFresh(string key)
    {
        if (Find(key) is { } node) node.Value.Entry.LoadedAt = DateTime.UtcNow;
    }

    /// <summary>Forgets a page (e.g. its load failed, so the next visit tries again).</summary>
    public void Forget(string key)
    {
        if (Find(key) is { } node) _entries.Remove(node);
    }

    /// <summary>Where the page was scrolled to when it was left.</summary>
    public double GetScroll(string key) => Find(key)?.Value.Entry.ScrollOffset ?? 0;

    public void SetScroll(string key, double offset)
    {
        if (Find(key) is { } node) node.Value.Entry.ScrollOffset = offset;
    }

    /// <summary>Everything is dropped when the library changes.</summary>
    public void Clear() => _entries.Clear();

    private LinkedListNode<(string Key, Entry Entry)>? Find(string key)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
            if (node.Value.Key == key) return node;
        return null;
    }
}
