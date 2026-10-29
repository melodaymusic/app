namespace MelodayMusic.Core.Mixes;

public static class Freshness
{
    public const double Days = 30;

    public static readonly TimeSpan Rest = TimeSpan.FromDays(3);

    public static double Penalty(DateTimeOffset? lastViewed, DateTimeOffset now)
    {
        if (lastViewed is not { } last) return 0;
        var days = Math.Max(0, (now - last).TotalDays);
        return days >= Days ? 0 : 1.2 * Math.Pow(1 - days / Days, 2);
    }

    public static double Weight(DateTimeOffset? lastViewed, DateTimeOffset now) => Math.Max(0.06, 1 - Penalty(lastViewed, now) / 1.2);

    public static IReadOnlyList<T> Prefer<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> lastViewed, DateTimeOffset now, int needed)
    {
        var all = items.ToList();
        var fresh = all.Where(t => lastViewed(t) is not { } last || now - last >= Rest).ToList();
        if (fresh.Count >= needed) return fresh;
        var recent = all.Where(t => lastViewed(t) is { } last && now - last < Rest).OrderBy(t => lastViewed(t)).ToList();
        return [.. fresh, .. recent];
    }

    public static IEnumerable<T> WeightedOrder<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> lastViewed, DateTimeOffset now, Random random,
        Func<T, double>? like = null) =>
        items.Select(t => (Item: t, Key: Math.Pow(random.NextDouble(), 1 / (Weight(lastViewed(t), now) * Math.Exp(0.8 * (like?.Invoke(t) ?? 0))))))
            .OrderByDescending(x => x.Key)
            .Select(x => x.Item);
}
