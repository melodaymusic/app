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
/// a week.
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

    public LyricsService(PlexSession session, SettingsStore settings, HttpClient http, ILogger<LyricsService> log)
    {
        _session = session;
        _settings = settings;
        _lrclib = new LrcLibClient(http, App.Version);
        _log = log;
        _session.StateChanged += (_, _) => _lyrics.Clear();
    }

    /// <summary>A song's lyrics, or null when neither Plex nor LRCLIB has any (or neither can be reached).</summary>
    public Task<TrackLyrics?> GetAsync(Track track) => _lyrics.GetOrAdd(track.RatingKey, _ => FetchAsync(track));

    /// <summary>Forgets what was found, so the next look asks again (the lookup setting changed).</summary>
    public void Clear() => _lyrics.Clear();

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
        if (!_settings.Current.LyricsFallback) return Remember(track, plex);

        // Plex has none, or only plain text: LRCLIB may have them synced.
        var other = await LrcLibAsync(track).ConfigureAwait(false);
        var best = other is { IsSynced: true } || other is { IsInstrumental: true } ? other : plex ?? other;
        return Remember(track, best);
    }

    private TrackLyrics? Remember(Track track, TrackLyrics? lyrics)
    {
        // A miss isn't remembered for the session: the next look asks again.
        if (lyrics is null) _lyrics.TryRemove(track.RatingKey, out _);
        return lyrics;
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
