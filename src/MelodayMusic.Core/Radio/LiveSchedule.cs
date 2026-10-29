namespace MelodayMusic.Core.Radio;

public sealed record LiveEntry(string Key, int DurationMs, int Block = 0, string? Artist = null, string? Song = null, string? Programme = null);

public sealed record LiveBlockSlot(int Number, int Position, int Count);

public sealed class LiveSchedule
{
    public const int FallbackDurationMs = 200_000;

    public DateTimeOffset StartedAt { get; set; }
    public List<LiveEntry> Entries { get; set; } = [];
    public List<RotationPlay> Rotation { get; set; } = [];
    public int ProgrammeRound { get; set; }
    public string? PulseGuideVersion { get; set; }
    public List<string> PulseOccurrences { get; set; } = [];

    public void ImportRotation()
    {
        if (Rotation.Count > 0) return;
        var at = StartedAt;
        foreach (var entry in Entries)
        {
            Rotation.Add(new RotationPlay(entry.Key, entry.Artist ?? "", entry.Song ?? entry.Key, at, entry.Block > 0));
            at = at.AddMilliseconds(entry.DurationMs);
        }
        if (Rotation.Count > LiveRotation.Remembered) Rotation.RemoveRange(0, Rotation.Count - LiveRotation.Remembered);
    }

    public void AppendProgrammed(IEnumerable<LiveEntry> entries) => Entries.AddRange(entries.Select(e =>
        e with { DurationMs = e.DurationMs > 0 ? e.DurationMs : FallbackDurationMs }));

    public long TotalMs => Entries.Sum(e => (long)e.DurationMs);

    public DateTimeOffset EndsAt => StartedAt.AddMilliseconds(TotalMs);

    public int NextBlock => Entries.Count == 0 ? 1 : Entries.Max(e => e.Block) + 1;

    public LiveBlockSlot? SlotOf(string key, DateTimeOffset? now = null)
    {
        var entry = EntryOf(key, now);
        if (entry is null || entry.Block == 0) return null;
        var members = Entries.Where(e => e.Block == entry.Block).ToList();
        return new LiveBlockSlot(entry.Block, members.FindIndex(e => e.Key == key) + 1, members.Count);
    }

    public LiveEntry? EntryOf(string key, DateTimeOffset? now = null)
    {
        var start = now is { } at && Locate(at) is { } on ? on.Index : 0;
        return Entries.Skip(start).FirstOrDefault(e => e.Key == key);
    }

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

    public TimeSpan Remaining(DateTimeOffset now) => EndsAt > now ? EndsAt - now : TimeSpan.Zero;

    public void Trim(DateTimeOffset now, TimeSpan keep)
    {
        var cutoff = now - keep;
        while (Entries.Count > 1 && StartedAt.AddMilliseconds(Entries[0].DurationMs) < cutoff)
        {
            StartedAt = StartedAt.AddMilliseconds(Entries[0].DurationMs);
            Entries.RemoveAt(0);
        }
    }

    public int DropAfterCurrent(DateTimeOffset now)
    {
        if (Locate(now) is not { } at) return 0;
        var dropped = Entries.Count - (at.Index + 1);
        if (dropped > 0) Entries.RemoveRange(at.Index + 1, dropped);
        return Math.Max(0, dropped);
    }

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

    public static LiveSchedule Begin(IEnumerable<LiveEntry> entries, DateTimeOffset now, TimeSpan intoFirst)
    {
        var schedule = new LiveSchedule { StartedAt = now - intoFirst };
        schedule.Append(entries);
        return schedule;
    }
}
