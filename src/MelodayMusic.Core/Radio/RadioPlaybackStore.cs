using System.Text.Json;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Radio;

public sealed class RadioPlaybackStore
{
    private readonly string? _path;
    private readonly object _gate = new();
    private Dictionary<string, List<RotationPlay>>? _history;
    public string? SaveError { get; private set; }

    public RadioPlaybackStore(string? path = null) => _path = path;

    private void Load()
    {
        if (_history is not null) return;
        try
        {
            _history = _path is not null && File.Exists(_path)
                ? JsonSerializer.Deserialize<Dictionary<string, List<RotationPlay>>>(File.ReadAllText(_path)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _history = []; }
    }

    public IReadOnlyList<RotationPlay> Get(string scope)
    {
        lock (_gate) { Load(); return _history!.GetValueOrDefault(scope)?.ToArray() ?? []; }
    }

    public void Record(string scope, Track track, DateTimeOffset at, bool featured = false)
    {
        lock (_gate)
        {
            Load();
            if (!_history!.TryGetValue(scope, out var plays)) _history[scope] = plays = [];
            plays.Add(new RotationPlay(track.RatingKey, LiveRotation.ArtistOf(track), LiveRotation.SongOf(track), at, featured));
            if (plays.Count > 1 && plays[^2].At > at) plays.Sort((a, b) => a.At.CompareTo(b.At));
            if (plays.Count > LiveRotation.Remembered) plays.RemoveRange(0, plays.Count - LiveRotation.Remembered);
            SaveError = null;
            if (_path is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
                var temporary = _path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_history));
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SaveError = ex.Message; }
        }
    }
}
