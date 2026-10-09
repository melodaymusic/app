namespace HumeMusic.Core.Radio;

/// <summary>
/// One song in a live station's running order. <paramref name="Block"/> is the artist's block it belongs to
/// (Headliners plays an artist at a time), or 0 for a station that plays no blocks.
/// </summary>
public sealed record LiveEntry(string Key, int DurationMs, int Block = 0);

/// <summary>Where a song is in its artist's block: the block's number, the song's place in it (1-based) and how many songs it has.</summary>
public sealed record LiveBlockSlot(int Number, int Position, int Count);

/// <summary>
/// A live station's running order, as if it had been playing since <see cref="StartedAt"/>: the songs
/// follow each other back to back, so at any moment there is a song that is "on" and a point in it.
/// Tuning in joins there, in the middle of the song. Kept between visits, so coming back later joins
/// where the station would have got to. Pure: it only does the arithmetic of the running order.
/// </summary>
public sealed class LiveSchedule
{
    /// <summary>Where a song's length is unknown, this stands in.</summary>
    public const int FallbackDurationMs = 200_000;

    public DateTimeOffset StartedAt { get; set; }
    public List<LiveEntry> Entries { get; set; } = [];

    public long TotalMs => Entries.Sum(e => (long)e.DurationMs);

    public DateTimeOffset EndsAt => StartedAt.AddMilliseconds(TotalMs);

    /// <summary>The number the next block added would take (one more than any in the running order).</summary>
    public int NextBlock => Entries.Count == 0 ? 1 : Entries.Max(e => e.Block) + 1;

    /// <summary>The place of a song in its artist's block, or null where it isn't in one.</summary>
    public LiveBlockSlot? SlotOf(string key)
    {
        var entry = Entries.FirstOrDefault(e => e.Key == key);
        if (entry is null || entry.Block == 0) return null;
        var members = Entries.Where(e => e.Block == entry.Block).ToList();
        return new LiveBlockSlot(entry.Block, members.FindIndex(e => e.Key == key) + 1, members.Count);
    }

    /// <summary>The song that is on at <paramref name="now"/> and how far into it; null before the first song or after the last.</summary>
    public (int Index, int OffsetMs)? Locate(DateTimeOffset now)
    {
        var elapsed = (long)(now - StartedAt).TotalMilliseconds;
        if (elapsed < 0) return null;
        for (var i = 0; i < Entries.Count; i++)
        {
            if (elapsed < Entries[i].DurationMs) return (i, (int)elapsed);
            elapsed -= Entries[i].DurationMs;
        }
        return null;
    }

    /// <summary>How much is still to come after <paramref name="now"/>.</summary>
    public TimeSpan Remaining(DateTimeOffset now) => EndsAt > now ? EndsAt - now : TimeSpan.Zero;

    /// <summary>Forgets the songs that finished more than <paramref name="keep"/> ago, moving the start along so the rest are unchanged.</summary>
    public void Trim(DateTimeOffset now, TimeSpan keep)
    {
        var cutoff = now - keep;
        while (Entries.Count > 1 && StartedAt.AddMilliseconds(Entries[0].DurationMs) < cutoff)
        {
            StartedAt = StartedAt.AddMilliseconds(Entries[0].DurationMs);
            Entries.RemoveAt(0);
        }
    }

    /// <summary>
    /// Forgets what was planned after the song that is on at <paramref name="now"/> (it carries on, so the station is still where it
    /// was), to be planned again. Returns how many songs were dropped.
    /// </summary>
    public int DropAfterCurrent(DateTimeOffset now)
    {
        if (Locate(now) is not { } at) return 0;
        var dropped = Entries.Count - (at.Index + 1);
        if (dropped > 0) Entries.RemoveRange(at.Index + 1, dropped);
        return Math.Max(0, dropped);
    }

    /// <summary>Adds songs to the end, skipping any the running order already holds.</summary>
    public int Append(IEnumerable<LiveEntry> entries)
    {
        var known = Entries.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var entry in entries)
        {
            if (!known.Add(entry.Key)) continue;
            Entries.Add(entry with { DurationMs = entry.DurationMs > 0 ? entry.DurationMs : FallbackDurationMs });
            added++;
        }
        return added;
    }

    /// <summary>A running order of <paramref name="entries"/> that began <paramref name="intoFirst"/> ago (so tuning in lands that far in).</summary>
    public static LiveSchedule Begin(IEnumerable<LiveEntry> entries, DateTimeOffset now, TimeSpan intoFirst)
    {
        var schedule = new LiveSchedule { StartedAt = now - intoFirst };
        schedule.Append(entries);
        return schedule;
    }
}
