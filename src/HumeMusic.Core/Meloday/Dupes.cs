using System.Text.Json;

namespace HumeMusic.Core.Meloday;

/// <summary>One song of a dupe pair, as it was when the pair was made (so the pair can be shown without asking Plex).</summary>
public sealed record DupeSide(string Key, string Title, string Artist, string? Album, string? Thumb);

/// <summary>
/// Two songs the listener has said are the same song (a single and its album version, "Sexy Chick" and "Sexy Bitch"):
/// the <see cref="Keeper"/> is the one that plays, the <see cref="Dupe"/> is the one that never turns up in a Meloday
/// again (when it would have, the keeper stands in its place). Kept per server, since a song's key is the server's.
/// </summary>
public sealed record DupePair(string Server, DupeSide Keeper, DupeSide Dupe, DateTimeOffset At);

/// <summary>
/// The listener's dupes, kept on this computer. Pairs never chain: a keeper is always a song that isn't itself a dupe,
/// so asking for a song's keeper is one lookup and merging into a keeper that is already a dupe goes to its keeper.
/// </summary>
public sealed class DupeStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private List<DupePair> _pairs = [];
    private bool _loaded;

    public DupeStore(string? path = null) => _path = path;

    /// <summary>Raised when a pair is made or undone.</summary>
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
            // Pairs that can't be kept last until the app closes.
        }
    }

    /// <summary>The pairs made on a server, the newest first.</summary>
    public IReadOnlyList<DupePair> For(string server)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Where(p => p.Server == server).OrderByDescending(p => p.At).ToList();
        }
    }

    /// <summary>Each dupe's keeper (song key to song key) on a server.</summary>
    public IReadOnlyDictionary<string, string> Map(string server)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Where(p => p.Server == server).ToDictionary(p => p.Dupe.Key, p => p.Keeper.Key, StringComparer.Ordinal);
        }
    }

    /// <summary>Whether a song is a dupe of another on a server (its keeper plays in its place).</summary>
    public bool IsDupe(string server, string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.Any(p => p.Server == server && p.Dupe.Key == key);
        }
    }

    /// <summary>The song that plays in place of <paramref name="key"/>: its keeper if it is a dupe, else itself.</summary>
    public string KeeperOf(string server, string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _pairs.FirstOrDefault(p => p.Server == server && p.Dupe.Key == key)?.Keeper.Key ?? key;
        }
    }

    /// <summary>
    /// Makes a pair. False when it can't be (a song with itself, or a keeper that is already this dupe's own dupe). A
    /// keeper that is itself a dupe is replaced by its keeper, and songs that were dupes of the new dupe move to the keeper.
    /// </summary>
    public bool Add(DupePair pair)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (pair.Keeper.Key == pair.Dupe.Key) return false;
            var keeper = pair.Keeper;
            if (_pairs.FirstOrDefault(p => p.Server == pair.Server && p.Dupe.Key == keeper.Key) is { } up) keeper = up.Keeper;
            if (keeper.Key == pair.Dupe.Key) return false;
            // The new dupe may have been a keeper itself: those songs now follow the new keeper.
            for (var i = 0; i < _pairs.Count; i++)
                if (_pairs[i].Server == pair.Server && _pairs[i].Keeper.Key == pair.Dupe.Key) _pairs[i] = _pairs[i] with { Keeper = keeper };
            _pairs.RemoveAll(p => p.Server == pair.Server && p.Dupe.Key == pair.Dupe.Key);
            _pairs.Add(pair with { Keeper = keeper });
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Undoes the pair of a dupe: it is a song of its own again.</summary>
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
    /// <summary>
    /// The songs with each dupe replaced by its keeper (in the dupe's place), and any song appearing twice
    /// as a result kept once, where it first stood.
    /// </summary>
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
