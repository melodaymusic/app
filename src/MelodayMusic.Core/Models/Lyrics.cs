namespace MelodayMusic.Core.Models;

public sealed record LyricWord(TimeSpan Start, string Text)
{
    public TimeSpan? End { get; init; }
}

public sealed record LyricLine(TimeSpan? Start, TimeSpan? End, string Text)
{
    public IReadOnlyList<LyricWord>? Words { get; init; }
}

public enum LyricsSource
{
    Plex,
    LrcLib,
    Custom,
}

public sealed record TrackLyrics(IReadOnlyList<LyricLine> Lines)
{
    public LyricsSource Source { get; init; }

    public bool IsInstrumental { get; init; }

    public static TrackLyrics Instrumental(LyricsSource source) => new([]) { Source = source, IsInstrumental = true };

    public bool IsSynced { get; } = Lines.Count > 0 && Lines.All(l => l.Start is not null);

    public int IndexAt(TimeSpan position)
    {
        if (!IsSynced) return -1;
        int lo = 0, hi = Lines.Count - 1, found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (Lines[mid].Start <= position)
            {
                found = mid;
                lo = mid + 1;
            }
            else hi = mid - 1;
        }
        return found;
    }

    public bool IsBreakAt(TimeSpan position)
    {
        var i = IndexAt(position);
        if (i < 0) return true;
        var line = Lines[i];
        return string.IsNullOrWhiteSpace(line.Text) || (line.End is { } end && position > end + TimeSpan.FromSeconds(3) &&
            (i + 1 >= Lines.Count || Lines[i + 1].Start - end > TimeSpan.FromSeconds(6)));
    }
}
