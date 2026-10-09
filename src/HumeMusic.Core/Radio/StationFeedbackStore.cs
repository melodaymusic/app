using System.Text.Json;

namespace HumeMusic.Core.Radio;

/// <summary>
/// What the listener has said does not suit a station: songs taken off it ("not for this station"), kept on this
/// computer per station (and server) so a station never plays them again. It is the station's own: the same song
/// is still welcome on every other station. Pure of the app: it only keeps and answers.
/// </summary>
public sealed class StationFeedbackStore
{
    /// <summary>The most songs kept per station (the oldest are forgotten first).</summary>
    public const int MaxPerStation = 2000;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, List<string>> _off = [];
    private bool _loaded;

    public StationFeedbackStore(string? path = null) => _path = path;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _off = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _off = [];
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_off, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Feedback that can't be kept lasts until the app closes.
        }
    }

    /// <summary>The songs taken off a station.</summary>
    public IReadOnlySet<string> Off(string station)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _off.TryGetValue(station, out var songs) ? songs.ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
        }
    }

    public bool IsOff(string station, string song)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _off.TryGetValue(station, out var songs) && songs.Contains(song);
        }
    }

    /// <summary>Takes a song off a station, or (with <paramref name="off"/> false) lets it back on.</summary>
    public void Set(string station, string song, bool off)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (off)
            {
                if (!_off.TryGetValue(station, out var songs)) _off[station] = songs = [];
                if (songs.Contains(song)) return;
                songs.Add(song);
                if (songs.Count > MaxPerStation) songs.RemoveRange(0, songs.Count - MaxPerStation);
            }
            else if (_off.TryGetValue(station, out var songs) && songs.Remove(song))
            {
                if (songs.Count == 0) _off.Remove(station);
            }
            else
            {
                return;
            }
            Save();
        }
    }

    /// <summary>How many songs have been taken off any station.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                EnsureLoaded();
                return _off.Values.Sum(s => s.Count);
            }
        }
    }

    /// <summary>Lets every song back on every station.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_off.Count == 0) return;
            _off = [];
            Save();
        }
    }
}
