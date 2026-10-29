using System.Text.Json;

namespace MelodayMusic.Core.Meloday;

public sealed record DupeSide(string Key, string Title, string Artist, string? Album, string? Thumb);

public sealed record DupePair(string Server, DupeSide Keeper, DupeSide Dupe, DateTimeOffset At);

public sealed class DupeStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private List<DupePair> _pairs = [];
    private bool _loaded;

    public DupeStore(string? path = null) => _path = path;

    public event EventHandler? Changed;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _pairs = JsonSerializer.Deserialize<List<DupePair>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _pairs = [];
        }
    }

    private void Save()
    {
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_pairs, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<DupePair> For(string server)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Where(p => p.Server == server).OrderByDescending(p => p.At).ToList();
        }
    }

    public IReadOnlyDictionary<string, string> Map(string server)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Where(p => p.Server == server).ToDictionary(p => p.Dupe.Key, p => p.Keeper.Key, StringComparer.Ordinal);
        }
    }

    public bool IsDupe(string server, string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Any(p => p.Server == server && p.Dupe.Key == key);
        }
    }

    public string KeeperOf(string server, string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.FirstOrDefault(p => p.Server == server && p.Dupe.Key == key)?.Keeper.Key ?? key;
        }
    }

    public bool Add(DupePair pair)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (pair.Keeper.Key == pair.Dupe.Key) return false;
            var keeper = pair.Keeper;
            if (_pairs.FirstOrDefault(p => p.Server == pair.Server && p.Dupe.Key == keeper.Key) is { } up) keeper = up.Keeper;
            if (keeper.Key == pair.Dupe.Key) return false;
            for (var i = 0; i < _pairs.Count; i++)
                if (_pairs[i].Server == pair.Server && _pairs[i].Keeper.Key == pair.Dupe.Key) _pairs[i] = _pairs[i] with { Keeper = keeper };
            _pairs.RemoveAll(p => p.Server == pair.Server && p.Dupe.Key == pair.Dupe.Key);
            _pairs.Add(pair with { Keeper = keeper });
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool Remove(string server, string dupeKey)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (_pairs.RemoveAll(p => p.Server == server && p.Dupe.Key == dupeKey) == 0) return false;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

public static class Dupes
{
    public static IReadOnlyList<string> Collapse(IEnumerable<string> keys, IReadOnlyDictionary<string, string> keepers)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var key in keys)
        {
            var chosen = keepers.TryGetValue(key, out var keeper) ? keeper : key;
            if (seen.Add(chosen)) result.Add(chosen);
        }
        return result;
    }
}
