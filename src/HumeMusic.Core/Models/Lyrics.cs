namespace HumeMusic.Core.Models;

/// <summary>A word of a line, with the time it is sung (only when the lyrics carry timing for each word) and, when the timing says, the time it stops being held.</summary>
public sealed record LyricWord(TimeSpan Start, string Text)
{
    /// <summary>When a word held on a long note ends (the Lyric Studio records it); null for the usual word that is simply sung.</summary>
    public TimeSpan? End { get; init; }
}

/// <summary>One line of a song's lyrics, with its time when the lyrics are synced, and its words' times when they have them.</summary>
public sealed record LyricLine(TimeSpan? Start, TimeSpan? End, string Text)
{
    /// <summary>Each word with its own time, when the source gives them (most lyrics are timed by the line only).</summary>
    public IReadOnlyList<LyricWord>? Words { get; init; }
}

/// <summary>Where a song's lyrics came from, for crediting them.</summary>
public enum LyricsSource
{
    /// <summary>Plex, from LyricFind (Plex Pass).</summary>
    Plex,
    /// <summary>LRCLIB (lrclib.net), the open lyrics database.</summary>
    LrcLib,
    /// <summary>Timed by the listener, in the Lyric Studio.</summary>
    Custom,
}

/// <summary>A song's lyrics: synced line by line, or plain text.</summary>
public sealed record TrackLyrics(IReadOnlyList<LyricLine> Lines)
{
    public LyricsSource Source { get; init; }

    /// <summary>Known to have no singing (an instrumental): no lines, and no need to look further.</summary>
    public bool IsInstrumental { get; init; }

    public static TrackLyrics Instrumental(LyricsSource source) => new([]) { Source = source, IsInstrumental = true };

    /// <summary>Whether every line carries its time, so the lyrics can follow the song.</summary>
    public bool IsSynced { get; } = Lines.Count > 0 && Lines.All(l => l.Start is not null);

    /// <summary>
    /// The line being sung at <paramref name="position"/>: the last line started by then, or -1 before
    /// the first (and always -1 for unsynced lyrics).
    /// </summary>
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

    /// <summary>Whether the song is between lines at <paramref name="position"/> (an instrumental break).</summary>
    public bool IsBreakAt(TimeSpan position)
    {
        var i = IndexAt(position);
        if (i < 0) return true;
        var line = Lines[i];
        return string.IsNullOrWhiteSpace(line.Text) || (line.End is { } end && position > end + TimeSpan.FromSeconds(3) &&
            (i + 1 >= Lines.Count || Lines[i + 1].Start - end > TimeSpan.FromSeconds(6)));
    }
}
