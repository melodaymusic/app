using System.Collections.Concurrent;
using System.Text.Json;
using HumeMusic.Core;
using HumeMusic.Core.Lyrics;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Songs' lyrics: Plex's first (LyricFind, with Plex Pass), and where Plex has none, or only
/// unsynced ones, LRCLIB's (lrclib.net, the open lyrics database). What LRCLIB finds is kept on this
/// computer, so each song is looked up there once; a song it doesn't know is asked about again after
/// a week. What the listener has done to a song's lyrics here (shifted their timing, or timed them in the
/// Lyric Studio) is applied on top, and kept on this computer.
/// </summary>
public sealed class LyricsService
{
    private static readonly TimeSpan RetryMissingAfter = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly LrcLibClient _lrclib;
    private readonly ILogger<LyricsService> _log;
    private readonly string _folder = Path.Combine(AppPaths.Root, "cache", "lyrics");
    private readonly ConcurrentDictionary<string, Task<TrackLyrics?>> _lyrics = new();
    private readonly ConcurrentDictionary<string, Task<TrackLyrics?>> _raw = new();
    private readonly LyricEdits _edits = new(AppPaths.LyricEdits);

    /// <summary>A song's lyrics or their timing were changed by the listener (the song's rating key): whatever shows them should look again.</summary>
    public event EventHandler<string>? Changed;

    public LyricsService(PlexSession session, SettingsStore settings, HttpClient http, ILogger<LyricsService> log)
    {
        _session = session;
        _settings = settings;
        _lrclib = new LrcLibClient(http, App.Version);
        _log = log;
        _session.StateChanged += (_, _) => Clear();
    }

    /// <summary>A song's lyrics (with the listener's edits), or null when neither Plex nor LRCLIB has any (or neither can be reached).</summary>
    public Task<TrackLyrics?> GetAsync(Track track) => _lyrics.GetOrAdd(track.RatingKey, _ => WithEditsAsync(track));

    /// <summary>Forgets what was found, so the next look asks again (the lookup setting changed).</summary>
    public void Clear()
    {
        _lyrics.Clear();
        _raw.Clear();
    }

    private async Task<TrackLyrics?> WithEditsAsync(Track track)
    {
        var raw = await _raw.GetOrAdd(track.RatingKey, _ => FetchAsync(track)).ConfigureAwait(false);
        var lyrics = LyricEdit.Apply(raw, _edits.Get(EditKey(track)));
        // A miss isn't remembered for the session: the next look asks again.
        if (lyrics is null)
        {
            _lyrics.TryRemove(track.RatingKey, out _);
            _raw.TryRemove(track.RatingKey, out _);
        }
        return lyrics;
    }

    // ------------------------------------------------------------------ the listener's edits

    private string EditKey(Track track)
    {
        var server = _session.Server?.MachineIdentifier ?? "none";
        return server[..Math.Min(8, server.Length)] + "-" + track.RatingKey;
    }

    /// <summary>What the listener has done to this song's lyrics, or null.</summary>
    public LyricEdit? EditOf(Track track) => _edits.Get(EditKey(track));

    /// <summary>How far the song's lyrics have been shifted by the listener (milliseconds; later is positive).</summary>
    public int OffsetMs(Track track) => EditOf(track)?.OffsetMs ?? 0;

    /// <summary>Shifts the song's lyrics earlier or later by <paramref name="deltaMs"/>, for a version of the song the timing wasn't made for.</summary>
    public bool Nudge(Track track, int deltaMs)
    {
        var edit = EditOf(track) ?? new LyricEdit();
        edit.OffsetMs = Math.Clamp(edit.OffsetMs + deltaMs, -30000, 30000);
        return Save(track, edit);
    }

    /// <summary>Keeps lyrics the listener timed in the Lyric Studio (they stand in for the originals, and any shift starts again from none).</summary>
    public bool SaveTimed(Track track, LyricEdit timed)
    {
        timed.OffsetMs = 0;
        return Save(track, timed);
    }

    /// <summary>Takes the listener's shift and timing away, so the song's lyrics are as the source has them.</summary>
    public bool ClearEdits(Track track) => Save(track, new LyricEdit());

    private bool Save(Track track, LyricEdit edit)
    {
        var saved = _edits.Set(EditKey(track), edit);
        if (saved)
        {
            _lyrics.TryRemove(track.RatingKey, out _);
            Changed?.Invoke(this, track.RatingKey);
        }
        else _log.LogWarning("Couldn't keep the lyric edit for {Track}", track.Title);
        return saved;
    }

    /// <summary>What the sources have for the song, before any edit.</summary>
    private async Task<TrackLyrics?> FetchAsync(Track track)
    {
        TrackLyrics? plex = null;
        if (_session.Client is { } client)
        {
            try
            {
                plex = await client.GetLyricsAsync(track.RatingKey).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
            {
                _log.LogDebug("Plex lyrics unavailable for {Track}: {Message}", track.Title, ex.Message);
            }
        }
        if (plex is { IsSynced: true }) return plex;
        if (!_settings.Current.LyricsFallback) return plex;

        // Plex has none, or only plain text: LRCLIB may have them synced.
        var other = await LrcLibAsync(track).ConfigureAwait(false);
        var best = other is { IsSynced: true } || other is { IsInstrumental: true } ? other : plex ?? other;
        return best;
    }

    private sealed class Kept
    {
        public DateTimeOffset At { get; set; }
        public bool Found { get; set; }
        public bool Instrumental { get; set; }
        public List<LyricLine> Lines { get; set; } = [];
    }

    private async Task<TrackLyrics?> LrcLibAsync(Track track)
    {
        var path = Path.Combine(_folder, track.RatingKey + ".json");
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Kept>(await File.ReadAllTextAsync(path).ConfigureAwait(false), Json) is { } kept
                && (kept.Found || DateTimeOffset.UtcNow - kept.At < RetryMissingAfter))
            {
                if (!kept.Found) return null;
                return kept.Instrumental ? TrackLyrics.Instrumental(LyricsSource.LrcLib) : new TrackLyrics(kept.Lines) { Source = LyricsSource.LrcLib };
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        TrackLyrics? found;
        try
        {
            found = await _lrclib.GetAsync(track.DisplayArtist, track.Title, track.AlbumTitle, track.Duration).ConfigureAwait(false);
            // A song credited to its album artist may be listed under the track's own artist, or the reverse.
            if (found is null && track.ArtistTitle is { } albumArtist && albumArtist != track.DisplayArtist)
                found = await _lrclib.GetAsync(albumArtist, track.Title, track.AlbumTitle, track.Duration).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // LRCLIB can't be reached: nothing is kept, so it's asked again next time.
            _log.LogDebug("LRCLIB unavailable for {Track}: {Message}", track.Title, ex.Message);
            return null;
        }

        try
        {
            Directory.CreateDirectory(_folder);
            var kept = new Kept { At = DateTimeOffset.UtcNow, Found = found is not null, Instrumental = found?.IsInstrumental == true, Lines = found?.Lines.ToList() ?? [] };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(kept, Json)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        if (found is not null) _log.LogInformation("Lyrics for {Track} from LRCLIB ({Kind})", track.Title, found.IsInstrumental ? "instrumental" : found.IsSynced ? "synced" : "plain");
        return found;
    }
}
