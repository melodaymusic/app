using System.Text.Json;

namespace HumeMusic.Core.Radio;

/// <summary>The running orders of the live stations, kept on this computer (one per station per server) so a station carries on between visits.</summary>
public sealed class LiveScheduleStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, LiveSchedule> _schedules = [];
    private bool _loaded;

    public LiveScheduleStore(string? path = null) => _path = path;

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _schedules = JsonSerializer.Deserialize<Dictionary<string, LiveSchedule>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _schedules = [];
        }
    }

    public LiveSchedule? Get(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _schedules.GetValueOrDefault(key);
        }
    }

    public void Set(string key, LiveSchedule schedule)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _schedules[key] = schedule;
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_schedules, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A station that can't be remembered simply starts afresh next time.
            }
        }
    }
}
