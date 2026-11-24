using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelodayMusic.Core.Radio;

/// <summary>Each library's edited Pulse lineup. A library with nothing saved uses <see cref="PulseLineup.Defaults"/>.</summary>
public sealed class PulseLineupStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, List<PulseSlot>> _lineups = [];
    private bool _loaded;

    public PulseLineupStore(string? path = null) => _path = path;

    public string? SaveError { get; private set; }
    public event EventHandler? Changed;

    public bool IsEdited(string scope)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _lineups.ContainsKey(scope);
        }
    }

    public IReadOnlyList<PulseSlot> Get(string scope)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _lineups.TryGetValue(scope, out var saved) ? saved.Select(s => s with { Days = [.. s.Days] }).ToList() : PulseLineup.Defaults;
        }
    }

    public string Version(string scope)
    {
        lock (_gate)
        {
            EnsureLoaded();
            if (!_lineups.TryGetValue(scope, out var saved)) return "DEFAULT";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(saved, Json)));
            return Convert.ToHexString(bytes.AsSpan(0, 8));
        }
    }

    public void Set(string scope, IEnumerable<PulseSlot> slots)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _lineups[scope] = PulseLineup.Order(slots);
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset(string scope)
    {
        bool removed;
        lock (_gate)
        {
            EnsureLoaded();
            removed = _lineups.Remove(scope);
            if (removed) Save();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _lineups = JsonSerializer.Deserialize<Dictionary<string, List<PulseSlot>>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _lineups = [];
        }
    }

    private void Save()
    {
        SaveError = null;
        if (_path is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_lineups, Json));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex.Message;
        }
    }
}
