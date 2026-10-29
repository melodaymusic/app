using System.Text.Json;
using MelodayMusic.Core.Audio;

namespace MelodayMusic.Core.Tagging;

public sealed record MoodPlan(string Key, IReadOnlyList<string> Current, IReadOnlyList<(string Mood, double Confidence)> Add)
{
    public IReadOnlyList<string> Result => [.. Current, .. Add.Select(a => a.Mood)];
    public bool HasChange => Add.Count > 0;
}

public static class MoodTags
{
    public const double Minimum = 0.35;
    public const int MaxMoods = 2;

    public static IReadOnlyList<(string Mood, double Confidence)> Heard(SoundDescription? description) =>
        description?["mood"]?.Words.Where(w => w.Confidence >= Minimum).OrderByDescending(w => w.Confidence)
            .Take(MaxMoods).Select(w => (w.Label, w.Confidence)).ToList() ?? [];

    public static MoodPlan Plan(string key, IReadOnlyList<string> current, IReadOnlyList<(string Mood, double Confidence)> heard)
    {
        var have = new HashSet<string>(current.Select(m => m.Trim()), StringComparer.OrdinalIgnoreCase);
        var add = heard.Where(h => !string.IsNullOrWhiteSpace(h.Mood) && have.Add(h.Mood.Trim())).Select(h => (h.Mood.Trim(), h.Confidence)).ToList();
        return new MoodPlan(key, current, add);
    }

    public static IReadOnlyList<string> ToRemove(IReadOnlyList<string> current, IReadOnlyList<string> added)
    {
        var has = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
        return added.Where(has.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class MoodTagEntry
{
    public List<string> Added { get; set; } = [];
    public bool WasLocked { get; set; }
    public DateTimeOffset At { get; set; }
}

public sealed class MoodTagLedger
{
    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, Dictionary<string, MoodTagEntry>> _state = [];

    public MoodTagLedger(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            _state = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, MoodTagEntry>>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _state = [];
        }
    }

    public MoodTagEntry? Get(string library, string key)
    {
        lock (_gate) return _state.TryGetValue(library, out var songs) && songs.TryGetValue(key, out var entry) ? entry : null;
    }

    public IReadOnlyList<string> Keys(string library)
    {
        lock (_gate) return _state.TryGetValue(library, out var songs) ? songs.Keys.ToList() : [];
    }

    public int Count(string library)
    {
        lock (_gate) return _state.TryGetValue(library, out var songs) ? songs.Count : 0;
    }

    public void Record(string library, string key, IEnumerable<string> added, bool wasLocked, DateTimeOffset at)
    {
        lock (_gate)
        {
            if (!_state.TryGetValue(library, out var songs)) _state[library] = songs = [];
            if (!songs.TryGetValue(key, out var entry)) songs[key] = entry = new MoodTagEntry { WasLocked = wasLocked };
            foreach (var mood in added)
                if (!entry.Added.Contains(mood, StringComparer.OrdinalIgnoreCase)) entry.Added.Add(mood);
            entry.At = at;
        }
        Save();
    }

    public void Forget(string library, string key)
    {
        lock (_gate)
        {
            if (!_state.TryGetValue(library, out var songs) || !songs.Remove(key)) return;
            if (songs.Count == 0) _state.Remove(library);
        }
        Save();
    }

    private void Save()
    {
        if (_path is null) return;
        string json;
        lock (_gate) json = JsonSerializer.Serialize(_state);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
