using HumeMusic.Core.Models;

namespace HumeMusic.Core.Lyrics;

/// <summary>How the Lyric Studio is being tapped: a tap for each word, or just for each line (the words are then spaced as they are by default).</summary>
public enum StudioMode
{
    Words,
    Lines,
}

/// <summary>
/// The Lyric Studio's recording, apart from the screen: the lyrics are a run of words (or lines), the listener taps as each one
/// begins while the song plays, and the taps become timed lyrics. Taps are kept in order, one for each word so far; a tap that
/// follows the last too closely is a bounce and ignored; going back drops the taps after the point the song is taken back to.
/// </summary>
public sealed class StudioSession
{
    /// <summary>How long after a word begins a listener usually taps (hearing, then moving): taps are placed this much earlier.</summary>
    public static readonly TimeSpan ReactionTime = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan Bounce = TimeSpan.FromMilliseconds(40);

    /// <summary>A word the space bar is held down for at least this long is a held word (a long note), and its length is kept; a shorter press is just where the word begins.</summary>
    public static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(350);

    private readonly List<(int Line, string Text)> _units = [];
    private readonly List<TimeSpan> _taps = [];
    private readonly List<TimeSpan?> _ends = [];
    private readonly List<string> _lines;

    public StudioSession(string lyricsText, StudioMode mode)
    {
        Mode = mode;
        _lines = lyricsText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        for (var i = 0; i < _lines.Count; i++)
        {
            if (mode == StudioMode.Lines) _units.Add((i, _lines[i]));
            else foreach (var word in _lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) _units.Add((i, word));
        }
    }

    public StudioMode Mode { get; }
    public IReadOnlyList<string> Lines => _lines;
    public int UnitCount => _units.Count;
    public int Tapped => _taps.Count;
    public bool IsComplete => _taps.Count >= _units.Count && _units.Count > 0;

    /// <summary>The word (or line) the next tap is for, or null when all are done.</summary>
    public string? Next => _taps.Count < _units.Count ? _units[_taps.Count].Text : null;

    /// <summary>The line the next tap is in (or the last line when done).</summary>
    public int CurrentLine => _units.Count == 0 ? 0 : _units[Math.Min(_taps.Count, _units.Count - 1)].Line;

    /// <summary>The index of the unit a tap was made for, in order (for showing which words are done).</summary>
    public bool IsTapped(int unit) => unit < _taps.Count;

    public IReadOnlyList<(int Line, string Text)> Units => _units;

    /// <summary>Marks the next word as beginning now. Returns whether the tap counted.</summary>
    public bool Tap(TimeSpan position)
    {
        if (_taps.Count >= _units.Count) return false;
        var at = position > ReactionTime ? position - ReactionTime : TimeSpan.Zero;
        if (_taps.Count > 0 && at - _taps[^1] < Bounce) return false;
        _taps.Add(at);
        _ends.Add(null);
        return true;
    }

    /// <summary>
    /// The space bar came up: if it was held long enough the last word is a held word, ending now (a line's words only; a tap for
    /// a line has no length). Returns whether a held word was recorded.
    /// </summary>
    public bool Release(TimeSpan position)
    {
        if (Mode != StudioMode.Words || _taps.Count == 0 || _ends[^1] is not null) return false;
        var end = position > ReactionTime ? position - ReactionTime : TimeSpan.Zero;
        if (end - _taps[^1] < HoldThreshold) return false;
        _ends[^1] = end;
        return true;
    }

    /// <summary>Takes back the last tap.</summary>
    public bool Undo()
    {
        if (_taps.Count == 0) return false;
        _taps.RemoveAt(_taps.Count - 1);
        _ends.RemoveAt(_ends.Count - 1);
        return true;
    }

    /// <summary>The song has been taken back to <paramref name="position"/>: taps from there on are to be made again.</summary>
    public void RewindTo(TimeSpan position)
    {
        var keep = _taps.Count(t => t < position - ReactionTime);
        _taps.RemoveRange(keep, _taps.Count - keep);
        _ends.RemoveRange(keep, _ends.Count - keep);
    }

    /// <summary>Starts over.</summary>
    public void Reset()
    {
        _taps.Clear();
        _ends.Clear();
    }

    /// <summary>Where the tapping has got to: the song should be played from a little before this to carry on.</summary>
    public TimeSpan? LastTap => _taps.Count > 0 ? _taps[^1] : null;

    /// <summary>The timed lyrics so far, as an edit to keep (a line not fully tapped yet is left out).</summary>
    public LyricEdit ToEdit()
    {
        var lines = new List<EditedLine>();
        for (var l = 0; l < _lines.Count; l++)
        {
            var all = Enumerable.Range(0, _units.Count).Where(i => _units[i].Line == l).ToList();
            var indexes = all.Where(i => i < _taps.Count).ToList();
            // A line is kept once every word of it has been tapped.
            if (indexes.Count == 0 || indexes.Count < all.Count) continue;
            var line = new EditedLine { Text = _lines[l], StartMs = (long)_taps[indexes[0]].TotalMilliseconds };
            if (Mode == StudioMode.Words)
                line.Words = indexes.Select(i => new EditedWord { Text = _units[i].Text, StartMs = (long)_taps[i].TotalMilliseconds, EndMs = _ends[i] is { } end ? (long)end.TotalMilliseconds : null }).ToList();
            lines.Add(line);
        }
        // A line ends where the next begins.
        for (var i = 0; i + 1 < lines.Count; i++) lines[i].EndMs = lines[i + 1].StartMs;
        return new LyricEdit { Lines = lines };
    }
}
