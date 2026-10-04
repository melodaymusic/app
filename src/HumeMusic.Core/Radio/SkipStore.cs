using System.Text.Json;

namespace HumeMusic.Core.Radio;

/// <summary>
/// What the listener skips and plays through, kept on this computer only (Plex isn't told), so radio
/// can learn: a song skipped early counts against it, and a little against its artist; one played
/// through counts for it. Old signals fade (a skip from two months ago counts for a quarter), so
/// a phase passes. Thread-safe; saved a moment after a change.
/// </summary>
public sealed class SkipStore
{
    private sealed class Entry
    {
        public double Skips { get; set; }
        public double Finishes { get; set; }
        public long At { get; set; }
    }

    private sealed class State
    {
        public Dictionary<string, Entry> Tracks { get; set; } = [];
        public Dictionary<string, Entry> Artists { get; set; } = [];
    }

    /// <summary>A signal counts half as much after this long.</summary>
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(30);
    private const int MaxEntries = 6000;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(4);

    private readonly string? _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private State _state = new();
    private Timer? _saveTimer;

    public SkipStore(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
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

    /// <summary>A song was skipped early (<paramref name="skipped"/>) or played through.</summary>
    public void Record(string trackKey, string? artistKey, bool skipped)
    {
        var now = _clock();
        lock (_gate)
        {
            Bump(_state.Tracks, trackKey, skipped, now, 1);
            // An artist's skips count for less: one song's skip says little about the rest.
            if (artistKey is not null) Bump(_state.Artists, artistKey, skipped, now, 0.5);
            if (_state.Tracks.Count > MaxEntries) Trim(_state.Tracks);
            if (_state.Artists.Count > MaxEntries) Trim(_state.Artists);
        }
        Save();
    }

    /// <summary>How much the listener tends to skip this song (0 never, towards 1 always), for radio to avoid it.</summary>
    public double Penalty(string trackKey, string? artistKey)
    {
        var now = _clock();
        lock (_gate)
        {
            var track = _state.Tracks.TryGetValue(trackKey, out var t) ? Ratio(t, now) : 0;
            var artist = artistKey is not null && _state.Artists.TryGetValue(artistKey, out var a) ? Ratio(a, now) : 0;
            return Math.Max(track, artist * 0.5);
        }
    }

    private static void Bump(Dictionary<string, Entry> into, string key, bool skipped, DateTimeOffset now, double amount)
    {
        if (!into.TryGetValue(key, out var e)) into[key] = e = new Entry { At = now.ToUnixTimeSeconds() };
        Fade(e, now);
        if (skipped) e.Skips += amount;
        else e.Finishes += amount;
    }

    private static void Fade(Entry e, DateTimeOffset now)
    {
        var age = now - DateTimeOffset.FromUnixTimeSeconds(e.At);
        if (age > TimeSpan.Zero)
        {
            var keep = Math.Pow(0.5, age / HalfLife);
            e.Skips *= keep;
            e.Finishes *= keep;
        }
        e.At = now.ToUnixTimeSeconds();
    }

    private static double Ratio(Entry e, DateTimeOffset now)
    {
        var copy = new Entry { Skips = e.Skips, Finishes = e.Finishes, At = e.At };
        Fade(copy, now);
        return copy.Skips / (copy.Skips + copy.Finishes + 1);
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

    /// <summary>Writes what's pending now (shutdown).</summary>
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
            // Not kept this time; what's learnt is worth little enough to lose.
        }
    }
}
