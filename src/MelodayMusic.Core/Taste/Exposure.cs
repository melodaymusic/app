using System.Text.Json;

namespace MelodayMusic.Core.Taste;

/// <summary>
/// Songs and artists Meloday has suggested lately, fading over days, so the taste engine can rest what it has shown a lot.
/// </summary>
public sealed class Exposure
{
    private sealed class Entry
    {
        public double Seen { get; set; }
        public long At { get; set; }
    }

    private sealed class State
    {
        public Dictionary<string, Entry> Tracks { get; set; } = [];
        public Dictionary<string, Entry> Artists { get; set; } = [];
    }

    public static readonly TimeSpan TrackHalfLife = TimeSpan.FromDays(5);
    public static readonly TimeSpan ArtistHalfLife = TimeSpan.FromDays(7);

    /// <summary>Most a song can lose for being shown a lot (in the units of <see cref="TasteEngine.Like"/>).</summary>
    public const double TrackWeight = 0.45;

    /// <summary>Most a song can lose for its artist being shown a lot.</summary>
    public const double ArtistWeight = 0.35;

    /// <summary>Times shown (faded) at which a song or artist is fully rested.</summary>
    public const double TrackFull = 3, ArtistFull = 14;

    private const int MaxEntries = 12_000;
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(5);

    private readonly string? _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private State _state = new();
    private Timer? _saveTimer;

    public Exposure(string? path = null, Func<DateTimeOffset>? clock = null)
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

    /// <summary>Notes songs as suggested. A song suggested by several features in a day counts each time, which is the point.</summary>
    public void Shown(IEnumerable<(string TrackKey, string? ArtistKey)> songs)
    {
        var now = _clock();
        var any = false;
        lock (_gate)
        {
            foreach (var (track, artist) in songs)
            {
                if (string.IsNullOrEmpty(track)) continue;
                Bump(_state.Tracks, track, now, TrackHalfLife);
                if (!string.IsNullOrEmpty(artist)) Bump(_state.Artists, artist, now, ArtistHalfLife);
                any = true;
            }
            if (_state.Tracks.Count > MaxEntries) Trim(_state.Tracks, now, TrackHalfLife);
            if (_state.Artists.Count > MaxEntries) Trim(_state.Artists, now, ArtistHalfLife);
        }
        if (any) Save();
    }

    public double Track(string trackKey) => Read(_state.Tracks, trackKey, TrackHalfLife);

    public double Artist(string? artistKey) => artistKey is null ? 0 : Read(_state.Artists, artistKey, ArtistHalfLife);

    /// <summary>How much to mark a song down: up to <see cref="TrackWeight"/> for the song and <see cref="ArtistWeight"/> for its artist.</summary>
    public double Penalty(string trackKey, string? artistKey) =>
        TrackWeight * Math.Min(1, Track(trackKey) / TrackFull) + ArtistWeight * Math.Min(1, Artist(artistKey) / ArtistFull);

    public void Clear()
    {
        lock (_gate) _state = new State();
        Save();
    }

    private double Read(Dictionary<string, Entry> from, string key, TimeSpan halfLife)
    {
        var now = _clock();
        lock (_gate) return from.TryGetValue(key, out var e) ? e.Seen * Keep(e, now, halfLife) : 0;
    }

    private static double Keep(Entry e, DateTimeOffset now, TimeSpan halfLife)
    {
        var age = now - DateTimeOffset.FromUnixTimeSeconds(e.At);
        return age > TimeSpan.Zero ? Math.Pow(0.5, age / halfLife) : 1;
    }

    private static void Bump(Dictionary<string, Entry> into, string key, DateTimeOffset now, TimeSpan halfLife)
    {
        if (!into.TryGetValue(key, out var e)) into[key] = e = new Entry { At = now.ToUnixTimeSeconds() };
        e.Seen = e.Seen * Keep(e, now, halfLife) + 1;
        e.At = now.ToUnixTimeSeconds();
    }

    private static void Trim(Dictionary<string, Entry> from, DateTimeOffset now, TimeSpan halfLife)
    {
        foreach (var key in from.OrderBy(kv => kv.Value.Seen * Keep(kv.Value, now, halfLife)).Take(from.Count - MaxEntries * 3 / 4).Select(kv => kv.Key).ToList())
            from.Remove(key);
    }

    private void Save()
    {
        if (_path is null) return;
        lock (_gate)
        {
            _saveTimer ??= new Timer(_ => Write(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Write()
    {
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_state);
            Directory.CreateDirectory(Path.GetDirectoryName(_path!)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path!, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
