namespace HumeMusic.Core.Meloday;

/// <summary>A part of the day Meloday makes a mix for, with its hours.</summary>
public sealed record MelodayPeriod(string Name, IReadOnlyList<int> Hours, string Phrase)
{
    /// <summary>When this instance of the period began (a Late Night that began yesterday at 23:00 counts from then).</summary>
    public DateTime StartOf(DateTime now)
    {
        var first = Hours[0];
        var start = now.Date.AddHours(first);
        return start > now ? start.AddDays(-1) : start;
    }

    /// <summary>When the next period begins: the hour after this one's last.</summary>
    public DateTime NextChange(DateTime now)
    {
        var next = now.Date.AddHours((Hours[^1] + 1) % 24);
        while (next <= now) next = next.AddDays(1);
        return next;
    }
}

/// <summary>Meloday's seven parts of the day (the hours of the script's config).</summary>
public static class MelodayPeriods
{
    public static IReadOnlyList<MelodayPeriod> All { get; } =
    [
        new("Dawn", [3, 4, 5], "at dawn"),
        new("Early Morning", [6, 7, 8], "in the early morning"),
        new("Morning", [9, 10, 11, 12], "in the morning"),
        new("Afternoon", [13, 14, 15, 16], "during the afternoon"),
        new("Evening", [17, 18, 19], "in the evening"),
        new("Night", [20, 21, 22], "at night"),
        new("Late Night", [23, 0, 1, 2], "late at night"),
    ];

    public static MelodayPeriod At(DateTime now) =>
        All.FirstOrDefault(p => p.Hours.Contains(now.Hour)) ?? All[^1];

    public static MelodayPeriod? Named(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
}
