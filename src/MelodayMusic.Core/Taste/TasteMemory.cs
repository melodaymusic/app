using System.Text.Json;

namespace MelodayMusic.Core.Taste;

public enum TasteSignal
{
    Skip,
    Finish,
    Pick,
}

public readonly record struct Remembered(double Skips, double Finishes, double Picks)
{
    public double SkipRisk => Skips / (Skips + Finishes + Picks + 1);
    public double Warmth => (Finishes + 2 * Picks) / (Skips + Finishes + 2 * Picks + 1);
    public bool IsEmpty => Skips + Finishes + Picks < 0.05;
}

public sealed class TasteMemory
{
    private sealed class Entry
    {
        public double Skips { get; set; }
        public double Finishes { get; set; }
        public double Picks { get; set; }
        public long At { get; set; }
    }

    private sealed class State
    {
        public Dictionary<string, Entry> Tracks { get; set; } = [];
        public Dictionary<string, Entry> Artists { get; set; } = [];
    }

    public static readonly TimeSpan HalfLife = TimeSpan.FromDays(30);
    private const int MaxEntries = 8000;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(4);

    private readonly string? _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private State _state = new();
    private Timer? _saveTimer;

    public TasteMemory(string? path = null, Func<DateTimeOffset>? clock = null, string? legacyPath = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        var source = path is not null && File.Exists(path) ? path : legacyPath is not null && File.Exists(legacyPath) ? legacyPath : null;
        if (source is null) return;
        try
        {
            _state = JsonSerializer.Deserialize<State>(File.ReadAllText(source)) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _state = new State();
        }
    }

    public event EventHandler? Changed;

    public int Songs { get { lock (_gate) return _state.Tracks.Count; } }

    public void Record(TasteSignal signal, string trackKey, string? artistKey)
    {
        if (string.IsNullOrEmpty(trackKey)) return;
        var now = _clock();
        lock (_gate)
        {
            Bump(_state.Tracks, trackKey, signal, now, 1);
            if (!string.IsNullOrEmpty(artistKey)) Bump(_state.Artists, artistKey, signal, now, 0.5);
            if (_state.Tracks.Count > MaxEntries) Trim(_state.Tracks);
            if (_state.Artists.Count > MaxEntries) Trim(_state.Artists);
        }
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Remembered Track(string trackKey) => Read(_state.Tracks, trackKey);
    public Remembered Artist(string? artistKey) => artistKey is null ? default : Read(_state.Artists, artistKey);

    public IReadOnlyList<(string Key, Remembered Memory)> RememberedTracks()
    {
        var now = _clock();
        lock (_gate)
            return _state.Tracks.Select(kv => (kv.Key, Faded(kv.Value, now))).ToList();
    }

    public double SkipRisk(string trackKey, string? artistKey) => Math.Max(Track(trackKey).SkipRisk, Artist(artistKey).SkipRisk * 0.5);

    public (double Skips, double Finishes, double Picks) Totals()
    {
        var now = _clock();
        lock (_gate)
        {
            double s = 0, f = 0, p = 0;
            foreach (var e in _state.Tracks.Values)
            {
                var keep = Keep(e, now);
                s += e.Skips * keep;
                f += e.Finishes * keep;
                p += e.Picks * keep;
            }
            return (s, f, p);
        }
    }

    public void Clear()
    {
        lock (_gate) _state = new State();
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Remembered Read(Dictionary<string, Entry> from, string key)
    {
        var now = _clock();
        lock (_gate)
        {
            return from.TryGetValue(key, out var e) ? Faded(e, now) : default;
        }
    }

    private static Remembered Faded(Entry e, DateTimeOffset now)
    {
        var keep = Keep(e, now);
        return new Remembered(e.Skips * keep, e.Finishes * keep, e.Picks * keep);
    }

    private static double Keep(Entry e, DateTimeOffset now)
    {
        var age = now - DateTimeOffset.FromUnixTimeSeconds(e.At);
        return age > TimeSpan.Zero ? Math.Pow(0.5, age / HalfLife) : 1;
    }

    private static void Bump(Dictionary<string, Entry> into, string key, TasteSignal signal, DateTimeOffset now, double amount)
    {
        if (!into.TryGetValue(key, out var e)) into[key] = e = new Entry { At = now.ToUnixTimeSeconds() };
        var keep = Keep(e, now);
        e.Skips *= keep;
        e.Finishes *= keep;
        e.Picks *= keep;
        e.At = now.ToUnixTimeSeconds();
        switch (signal)
        {
            case TasteSignal.Skip: e.Skips += amount; break;
            case TasteSignal.Finish: e.Finishes += amount; break;
            default: e.Picks += amount; break;
        }
    }

    private static void Trim(Dictionary<string, Entry> from)
    {
        foreach (var key in from.OrderBy(kv => kv.Value.At).Take(from.Count - MaxEntries * 3 / 4).Select(kv => kv.Key).ToList())
            from.Remove(key);
    }

    private void Save()
    {
        if (_path is null) return;
        lock (_gate)
        {
            _saveTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
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
}
