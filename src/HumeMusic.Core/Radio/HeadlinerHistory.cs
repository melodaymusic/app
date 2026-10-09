using System.Text.Json;

namespace HumeMusic.Core.Radio;

/// <summary>
/// The artists Headliners has featured lately, kept on this computer so it doesn't open on the same artists each time the
/// app starts: an artist featured in the last day and a half is rested. Pure of the app: it only keeps and answers.
/// </summary>
public sealed class HeadlinerHistory
{
    /// <summary>How long Headliners rests an artist after featuring them.</summary>
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

    /// <summary>The artists featured within the rest period before <paramref name="now"/>.</summary>
    public IReadOnlySet<string> Rested(DateTimeOffset now)
    {
        lock (_gate)
        {
            EnsureLoaded();
            return _featured.Where(p => now - p.Value < _rest).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
        }
    }

    /// <summary>Notes that these artists have been featured (and forgets those long ago).</summary>
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
                // Kept until the app closes.
            }
        }
    }
}
