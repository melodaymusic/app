namespace HumeMusic.Core.Mixes;

/// <summary>
/// How much a song counts against for having been played lately, wherever Meloday makes something for the listener (the daily
/// Meloday, the mixes, the live stations, the radios): a song played an hour ago is very unlikely to be offered, one played
/// a week ago is held back, and by a month it is as welcome as any. Never a ban: a small library that has played everything
/// still gets songs, those not played for longest first.
/// </summary>
public static class Freshness
{
    /// <summary>The days before a song stops counting against it.</summary>
    public const double Days = 30;

    /// <summary>A song played within this long is left out where there are enough others.</summary>
    public static readonly TimeSpan Rest = TimeSpan.FromDays(3);

    /// <summary>What to take from a song's score for its last play: 1.2 just played, easing to 0 over a month (0 when never played).</summary>
    public static double Penalty(DateTimeOffset? lastViewed, DateTimeOffset now)
    {
        if (lastViewed is not { } last) return 0;
        var days = Math.Max(0, (now - last).TotalDays);
        return days >= Days ? 0 : 1.2 * Math.Pow(1 - days / Days, 2);
    }

    /// <summary>The chance weight of a song for its last play: 1 for one not played lately, down to 0.06 for one just played.</summary>
    public static double Weight(DateTimeOffset? lastViewed, DateTimeOffset now) => Math.Max(0.06, 1 - Penalty(lastViewed, now) / 1.2);

    /// <summary>
    /// The songs not played within <see cref="Rest"/> in their order, so that taking the first <paramref name="needed"/> leaves
    /// out what was played lately; where fewer than that are left, the rest follow, those not played for longest first.
    /// </summary>
    public static IReadOnlyList<T> Prefer<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> lastViewed, DateTimeOffset now, int needed)
    {
        var all = items.ToList();
        var fresh = all.Where(t => lastViewed(t) is not { } last || now - last >= Rest).ToList();
        if (fresh.Count >= needed) return fresh;
        var recent = all.Where(t => lastViewed(t) is { } last && now - last < Rest).OrderBy(t => lastViewed(t)).ToList();
        return [.. fresh, .. recent];
    }

    /// <summary>The songs in a random order that favours those not played lately (each more likely to come early the longer since it was played).</summary>
    public static IEnumerable<T> WeightedOrder<T>(IEnumerable<T> items, Func<T, DateTimeOffset?> lastViewed, DateTimeOffset now, Random random) =>
        items.Select(t => (Item: t, Key: Math.Pow(random.NextDouble(), 1 / Weight(lastViewed(t), now))))
            .OrderByDescending(x => x.Key)
            .Select(x => x.Item);
}
