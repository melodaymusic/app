using System.Text.Json;

namespace MelodayMusic.Core.Home;

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

    public event EventHandler? Changed;

    public TimeSpan Today
    {
        get
        {
            lock (_gate) return TimeSpan.FromSeconds(DayLocked() == _state.Day ? _state.Seconds : 0);
        }
    }

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
        }
    }

    private string DayLocked() => _clock().ToString("yyyy-MM-dd");

    public static string Describe(TimeSpan time)
    {
        var minutes = (int)Math.Floor(time.TotalMinutes);
        return minutes >= 60 ? $"{minutes / 60}h {minutes % 60}m" : $"{minutes}m";
    }
}
