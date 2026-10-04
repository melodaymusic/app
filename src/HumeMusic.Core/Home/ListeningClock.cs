using System.Text.Json;

namespace HumeMusic.Core.Home;

/// <summary>
/// How long the listener has listened today, in Meloday Music (Home shows it as "Today's listening"):
/// time is added while music plays, the total starts again at local midnight, and it is kept on this
/// computer so closing the app doesn't lose the day. Thread-safe.
/// </summary>
public sealed class ListeningClock
{
    private sealed class State
    {
        public string Day { get; set; } = string.Empty;
        public double Seconds { get; set; }
    }

    private static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(20);

    private readonly string? _path;
    private readonly Func<DateTime> _clock;
    private readonly object _gate = new();
    private State _state = new();
    private DateTime _lastSave;
    private int _shownMinutes = -1;

    public ListeningClock(string? path = null, Func<DateTime>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTime.Now);
        _lastSave = _clock();
        if (path is null || !File.Exists(path)) return;
        try
        {
            _state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _state = new State();
        }
    }

    /// <summary>Raised when the minutes shown change (so Home can follow it as it counts up).</summary>
    public event EventHandler? Changed;

    /// <summary>Today's listening so far.</summary>
    public TimeSpan Today
    {
        get
        {
            lock (_gate) return TimeSpan.FromSeconds(DayLocked() == _state.Day ? _state.Seconds : 0);
        }
    }

    /// <summary>Adds time spent listening (as it plays).</summary>
    public void Add(TimeSpan listened)
    {
        if (listened <= TimeSpan.Zero) return;
        bool changed;
        bool save;
        lock (_gate)
        {
            var day = DayLocked();
            if (_state.Day != day) _state = new State { Day = day };
            _state.Seconds += listened.TotalSeconds;
            var minutes = (int)(_state.Seconds / 60);
            changed = minutes != _shownMinutes;
            _shownMinutes = minutes;
            save = _clock() - _lastSave >= SaveEvery;
            if (save) _lastSave = _clock();
        }
        if (save) Flush();
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the day's total now (shutdown).</summary>
    public void Flush()
    {
        if (_path is null) return;
        string json;
        lock (_gate) json = JsonSerializer.Serialize(_state);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The total is worth little enough to lose.
        }
    }

    private string DayLocked() => _clock().ToString("yyyy-MM-dd");

    /// <summary>"2h 15m", "42m": hours and minutes, whole minutes only.</summary>
    public static string Describe(TimeSpan time)
    {
        var minutes = (int)Math.Floor(time.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
    }
}
