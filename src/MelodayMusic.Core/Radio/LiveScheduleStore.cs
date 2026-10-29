using System.Text.Json;

namespace MelodayMusic.Core.Radio;

public sealed class LiveScheduleStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, LiveSchedule> _schedules = [];
    private bool _loaded;
    public string? SaveError { get; private set; }

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

    public LiveSchedule? Take(string key)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _schedules.Remove(key, out var schedule) ? schedule : null;
        }
    }

    public void Set(string key, LiveSchedule schedule)
    {
        lock (_gate)
        {
            EnsureLoaded();
            _schedules[key] = schedule;
            SaveError = null;
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(_schedules, Json));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SaveError = ex.Message;
            }
        }
    }
}
