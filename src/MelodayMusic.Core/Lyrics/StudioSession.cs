namespace MelodayMusic.Core.Lyrics;

public enum StudioMode
{
    Words,
    Lines,
}

public sealed class StudioSession
{
    public static readonly TimeSpan ReactionTime = TimeSpan.FromMilliseconds(130);
    private static readonly TimeSpan Bounce = TimeSpan.FromMilliseconds(40);

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

    public string? Next => _taps.Count < _units.Count ? _units[_taps.Count].Text : null;

    public int CurrentLine => _units.Count == 0 ? 0 : _units[Math.Min(_taps.Count, _units.Count - 1)].Line;

    public bool IsTapped(int unit) => unit < _taps.Count;

    public IReadOnlyList<(int Line, string Text)> Units => _units;

    public bool Tap(TimeSpan position)
    {
        if (_taps.Count >= _units.Count) return false;
        var at = position > ReactionTime ? position - ReactionTime : TimeSpan.Zero;
        if (_taps.Count > 0 && at - _taps[^1] < Bounce) return false;
        _taps.Add(at);
        _ends.Add(null);
        return true;
    }

    public bool Release(TimeSpan position)
    {
        if (Mode != StudioMode.Words || _taps.Count == 0 || _ends[^1] is not null) return false;
        var end = position > ReactionTime ? position - ReactionTime : TimeSpan.Zero;
        if (end - _taps[^1] < HoldThreshold) return false;
        _ends[^1] = end;
        return true;
    }

    public bool Undo()
    {
        if (_taps.Count == 0) return false;
        _taps.RemoveAt(_taps.Count - 1);
        _ends.RemoveAt(_ends.Count - 1);
        return true;
    }

    public void RewindTo(TimeSpan position)
    {
        var keep = _taps.Count(t => t < position - ReactionTime);
        _taps.RemoveRange(keep, _taps.Count - keep);
        _ends.RemoveRange(keep, _ends.Count - keep);
    }

    public void Reset()
    {
        _taps.Clear();
        _ends.Clear();
    }

    public TimeSpan? LastTap => _taps.Count > 0 ? _taps[^1] : null;

    public LyricEdit ToEdit()
    {
        var lines = new List<EditedLine>();
        for (var l = 0; l < _lines.Count; l++)
        {
            var all = Enumerable.Range(0, _units.Count).Where(i => _units[i].Line == l).ToList();
            var indexes = all.Where(i => i < _taps.Count).ToList();
            if (indexes.Count == 0 || indexes.Count < all.Count) continue;
            var line = new EditedLine { Text = _lines[l], StartMs = (long)_taps[indexes[0]].TotalMilliseconds };
            if (Mode == StudioMode.Words)
                line.Words = indexes.Select(i => new EditedWord { Text = _units[i].Text, StartMs = (long)_taps[i].TotalMilliseconds, EndMs = _ends[i] is { } end ? (long)end.TotalMilliseconds : null }).ToList();
            lines.Add(line);
        }
        for (var i = 0; i + 1 < lines.Count; i++) lines[i].EndMs = lines[i + 1].StartMs;
        return new LyricEdit { Lines = lines };
    }
}
