using System.Text.Json;

namespace MelodayMusic.Core.Radio;

public sealed class HeadlinerHistory
{
    public static readonly TimeSpan Rest = TimeSpan.FromHours(36);
    private static readonly TimeSpan Forget = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, DateTimeOffset> _featured = [];
    private bool _loaded;

    private readonly TimeSpan _rest;

    public HeadlinerHistory(string? path = null, TimeSpan? rest = null)
    {
        _path = path;
        _rest = rest ?? Rest;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            _featured = JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _featured = [];
        }
    }

    public IReadOnlySet<string> Rested(DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _featured.Where(p => now - p.Value < _rest).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        }
    }

    public void Record(IEnumerable<string> artists, DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureLoaded();
            foreach (var artist in artists) _featured[artist] = now;
            foreach (var old in _featured.Where(p => now - p.Value > Forget).Select(p => p.Key).ToList()) _featured.Remove(old);
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, JsonSerializer.Serialize(_featured, Json));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
