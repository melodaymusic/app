using System.Text.Json;

namespace MelodayMusic.Core.Taste;

/// <summary>
/// Playlists, albums, mixes and stations the listener has asked not to shape their taste ("Don't use for my taste profile"),
/// and the songs heard from them. Those plays still count in Plex and still scrobble; the taste engine just leaves them out:
/// no skips, finishes or picks are remembered for them, and their Plex plays are dropped when the taste profile is built.
/// </summary>
public sealed class TasteExclusions
{
    private sealed class Heard
    {
        public string Track { get; set; } = string.Empty;
        public long At { get; set; }
    }

    private sealed class State
    {
        public HashSet<string> Items { get; set; } = [];
        public List<Heard> Plays { get; set; } = [];
    }

    /// <summary>How long a heard song is kept to match against Plex history (the taste profile looks back no further).</summary>
    public static readonly TimeSpan KeepPlays = TimeSpan.FromDays(TasteProfile.LookbackDays + 7);

    /// <summary>A Plex play this soon after a song started from an excluded source is that play.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(45);

    private readonly string? _path;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();
    private State _state = new();

    public TasteExclusions(string? path = null, Func<DateTimeOffset>? clock = null)
    {
        _path = path;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        if (path is null || !File.Exists(path)) return;
        try
        {
            _state = JsonSerializer.Deserialize<State>(File.ReadAllText(path)) ?? new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _state = new State();
        }
    }

    public event EventHandler? Changed;

    public bool IsExcluded(string? itemKey)
    {
        if (string.IsNullOrEmpty(itemKey)) return false;
        lock (_gate) return _state.Items.Contains(itemKey);
    }

    public void Set(string itemKey, bool excluded)
    {
        if (string.IsNullOrEmpty(itemKey)) return;
        bool changed;
        lock (_gate) changed = excluded ? _state.Items.Add(itemKey) : _state.Items.Remove(itemKey);
        if (!changed) return;
        Write();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RecordPlay(string trackKey, DateTimeOffset at)
    {
        if (string.IsNullOrEmpty(trackKey)) return;
        var cutoff = (at - KeepPlays).ToUnixTimeSeconds();
        lock (_gate)
        {
            _state.Plays.RemoveAll(p => p.At < cutoff);
            _state.Plays.Add(new Heard { Track = trackKey, At = at.ToUnixTimeSeconds() });
        }
        Write();
    }

    /// <summary>Whether a Plex play of this song at this time was one heard from an excluded source.</summary>
    public bool IsUntasted(string? trackKey, DateTimeOffset viewedAt)
    {
        if (string.IsNullOrEmpty(trackKey)) return false;
        var at = viewedAt.ToUnixTimeSeconds();
        var window = (long)Window.TotalSeconds;
        lock (_gate) return _state.Plays.Any(p => p.Track == trackKey && at >= p.At - 300 && at <= p.At + window);
    }

    private void Write()
    {
        if (_path is null) return;
        try
        {
            string json;
            lock (_gate) json = JsonSerializer.Serialize(_state);
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
