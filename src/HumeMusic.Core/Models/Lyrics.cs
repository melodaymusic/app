namespace HumeMusic.Core.Models;

/// <summary>One line of a song's lyrics, with its time when the lyrics are synced.</summary>
public sealed record LyricLine(TimeSpan? Start, TimeSpan? End, string Text);

/// <summary>Where a song's lyrics came from, for crediting them.</summary>
public enum LyricsSource
{
    /// <summary>Plex, from LyricFind (Plex Pass).</summary>
    Plex,
    /// <summary>LRCLIB (lrclib.net), the open lyrics database.</summary>
    LrcLib,
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
