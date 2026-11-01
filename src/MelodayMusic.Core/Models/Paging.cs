namespace MelodayMusic.Core.Models;

public static class Paging
{
    /// <summary>Reads every page of a list. Once the first page gives the total, the rest load a few at a time instead of one by one.</summary>
    public static async Task<List<T>> AllAsync<T>(Func<int, int, Task<PagedResult<T>>> fetch, int size, int parallel = 4)
    {
        var first = await fetch(0, size).ConfigureAwait(false);
        var items = new List<T>(first.Items);
        if (first.Items.Count < size) return items;
        if (first.TotalSize is not { } total)
        {
            for (var start = size; ; start += size)
            {
                var page = await fetch(start, size).ConfigureAwait(false);
                items.AddRange(page.Items); if (page.Items.Count < size) return items;
            }
        }
        var starts = Enumerable.Range(1, Math.Max(0, (total - 1) / size)).Select(i => i * size).ToList();
        var pages = new PagedResult<T>[starts.Count];
        using var gate = new SemaphoreSlim(Math.Max(1, parallel));
        await Task.WhenAll(starts.Select(async (start, i) =>
        {
            await gate.WaitAsync().ConfigureAwait(false);
            try { pages[i] = await fetch(start, size).ConfigureAwait(false); }
            finally { gate.Release(); }
        })).ConfigureAwait(false);
        foreach (var page in pages) items.AddRange(page.Items);
        return items;
    }
}
