namespace HumeMusic.Core.Mixes;

/// <summary>A tag the listener plays a lot of, and the items (artists) that carry most of it.</summary>
public sealed record RankedTag<T>(string Id, string Title, double Weight, IReadOnlyList<T> Leaders);

/// <summary>
/// The listener's favourite genres, styles, moods or decades: every item (an artist, a track, an
/// album) lends its weight (plays) to each of its tags; the heaviest tags win, each with the items
/// that contributed most to it.
/// </summary>
public static class TasteRanking
{
    public static IReadOnlyList<RankedTag<T>> Rank<T>(
        IEnumerable<T> items,
        Func<T, IEnumerable<(string Id, string Title)>> tags,
        Func<T, double> weight,
        int take,
        int leaders = 3,
        Func<string, bool>? ignore = null)
    {
        var totals = new Dictionary<string, (string Title, double Weight, List<(T Item, double Weight)> Items)>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            var w = weight(item);
            if (w <= 0) continue;
            foreach (var (id, title) in tags(item).DistinctBy(t => t.Id))
            {
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) || ignore?.Invoke(title) == true) continue;
                if (!totals.TryGetValue(id, out var entry)) entry = (title, 0, []);
                entry.Weight += w;
                entry.Items.Add((item, w));
                totals[id] = entry;
            }
        }
        return totals
            .OrderByDescending(t => t.Value.Weight)
            .ThenBy(t => t.Value.Title, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .Select(t => new RankedTag<T>(t.Key, t.Value.Title, t.Value.Weight,
                t.Value.Items.GroupBy(i => i.Item).Select(g => (Item: g.Key, Weight: g.Sum(x => x.Weight)))
                    .OrderByDescending(i => i.Weight).Take(leaders).Select(i => i.Item).ToList()))
            .ToList();
    }
}
