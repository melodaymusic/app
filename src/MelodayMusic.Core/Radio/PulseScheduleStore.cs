using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MelodayMusic.Core.Radio;

public sealed class PulseScheduleStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, List<PulseScheduleRule>> _rules = [];
    private bool _loaded;

    public PulseScheduleStore(string? path = null) => _path = path;

    public string? SaveError { get; private set; }
    public event EventHandler? Changed;

    public IReadOnlyList<PulseScheduleRule> Get(string scope)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _rules.GetValueOrDefault(scope)?.ToList() ?? [];
        }
    }

    public string Version(string scope)
    {
        lock (_gate)
        {
            EnsureLoaded();
            var ordered = (_rules.GetValueOrDefault(scope) ?? []).OrderBy(r => r.Id).ToList();
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ordered, Json)));
            return Convert.ToHexString(bytes.AsSpan(0, 8));
        }
    }

    public void Set(string scope, IEnumerable<PulseScheduleRule> rules)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _rules[scope] = rules.Where(r => r.IsValid).OrderBy(r => r.StartsAt).ThenBy(r => r.Source.Title).ToList();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Upsert(string scope, PulseScheduleRule rule)
    {
        if (!rule.IsValid) throw new ArgumentException("The Pulse schedule entry is incomplete.", nameof(rule));
        lock (_gate)
        {
            EnsureLoaded();
            var rules = _rules.GetValueOrDefault(scope) ?? [];
            var index = rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0) rules[index] = rule;
            else rules.Add(rule);
            _rules[scope] = rules.OrderBy(r => r.StartsAt).ThenBy(r => r.Source.Title).ToList();
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool Remove(string scope, string id)
    {
        bool removed;
        lock (_gate)
        {
            EnsureLoaded();
            removed = _rules.GetValueOrDefault(scope)?.RemoveAll(r => r.Id == id) > 0;
            if (removed) Save();
        }
        if (removed) Changed?.Invoke(this, EventArgs.Empty);
        return removed;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _rules = JsonSerializer.Deserialize<Dictionary<string, List<PulseScheduleRule>>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _rules = [];
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
            File.WriteAllText(temporary, JsonSerializer.Serialize(_rules, Json));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveError = ex.Message;
        }
    }
}
