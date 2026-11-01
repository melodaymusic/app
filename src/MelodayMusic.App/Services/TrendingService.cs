using System.Text.Json;
using MelodayMusic.Core.LastFm;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record Trending(DateTimeOffset At, int ChartSize, IReadOnlyList<Track> InLibrary);

public sealed class TrendingService
{
    public const string PlaylistKey = "lastfm-top100";
    private static Playlist DescribePlaylist(int count = 0, string? cover = null) => new()
    {
        RatingKey = PlaylistKey, Title = "Last.fm Top 100 Tracks", TrackCount = count, SubtitleOverride = "Chart", Thumb = cover,
        Summary = "Last.fm's highest-charting songs in your library, refreshed automatically every six hours.",
    };
    public static Playlist Playlist { get; private set; } = DescribePlaylist();
    private readonly LastFmService _lastFm;
    private readonly PlexSession _session;
    private readonly SettingsStore _settings;
    private readonly ILogger<TrendingService> _log;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _sync = new(1, 1);
    private static readonly TimeSpan ChartLife = TimeSpan.FromHours(6);
    private static readonly TimeSpan KeepSavedFor = TimeSpan.FromDays(3);
    private static readonly TimeSpan RetryAfterSuccess = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);
    private Task<Trending?>? _load;
    private Task<IReadOnlyList<Artist>>? _artists;
    private DateTimeOffset _tracksAt, _artistsAt;
    private DateTimeOffset _tracksRetryAt, _artistsRetryAt;
    private string? _restoredScope;
    private int _version;
    private Trending? _current;
    private string? _libraryScope;
    private IReadOnlyList<Artist> _currentArtists = [];
    private readonly Timer _timer;
    public event EventHandler? Changed;
    public string? PlexError { get; private set; }
    public string? TracksError { get; private set; }
    public string? ArtistsError { get; private set; }
    public bool IsOn => _lastFm.TrendingOn;
    public bool IsAvailable => _lastFm.HasApiKey;
    public bool KeepOnPlex => _settings.Current.LastFmPlexPlaylist;
    private readonly ChartHistory _chartHistory = new(Path.Combine(MelodayMusic.Core.AppPaths.Shared, "lastfm-live-chart.json"));
    private readonly ChartRails _rails = new(Path.Combine(MelodayMusic.Core.AppPaths.Shared, "lastfm-rails.json"));
    public IReadOnlyDictionary<string, ChartPosition> Positions { get; private set; } = new Dictionary<string, ChartPosition>();
    public IReadOnlyDictionary<string, ChartPosition> ArtistPositions { get; private set; } = new Dictionary<string, ChartPosition>();
    public string? CoverPath { get; private set; }

    public TrendingService(LastFmService lastFm, PlexSession session, SettingsStore settings, ILogger<TrendingService> log)
    {
        _lastFm = lastFm; _session = session; _settings = settings; _log = log;
        _lastFm.TrendingChanged += (_, _) => Invalidate();
        _session.StateChanged += (_, _) => OnSessionChanged();
        _timer = new Timer(async _ => await PrepareAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10));
    }
    private void OnSessionChanged()
    {
        if (_session.State == SessionState.SignedOut)
        {
            _libraryScope = null;
            Invalidate();
        }
        else if (_session.IsReady)
        {
            var scope = PlaylistScope;
            if (_libraryScope != scope)
            {
                _libraryScope = scope;
                Invalidate();
            }
            else
            {
                lock (_gate)
                {
                    if (_current is null || _load?.IsCompleted == false) _load = null;
                    if (_currentArtists.Count == 0 || _artists?.IsCompleted == false) _artists = null;
                }
            }
            _ = PrepareAsync();
        }
    }
    private void Invalidate()
    {
        lock (_gate)
        {
            _version++; _load = null; _artists = null; _current = null; _currentArtists = [];
            _restoredScope = null; _tracksAt = _artistsAt = _tracksRetryAt = _artistsRetryAt = default;
            TracksError = ArtistsError = PlexError = null;
            Playlist = DescribePlaylist();
            CoverPath = null;
            Positions = new Dictionary<string, ChartPosition>();
            ArtistPositions = new Dictionary<string, ChartPosition>();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private string PlaylistScope => _session.Server?.MachineIdentifier + ":" + _session.Section?.Key + ":" + _session.Account?.Id;
    public IReadOnlyList<Playlist> ForDisplay(IEnumerable<Playlist> playlists)
    {
        if (!IsOn) return playlists.ToList();
        _settings.Current.LastFmPlaylistKeys.TryGetValue(PlaylistScope, out var managed);
        return [Playlist, .. playlists.Where(p => p.RatingKey != managed && p.RatingKey != PlaylistKey)];
    }
    private async Task PrepareAsync()
    {
        try
        {
            var tracks = GetAsync();
            var artists = GetArtistsAsync();
            await Task.WhenAll(tracks, artists);
            var current = await tracks;
            if (KeepOnPlex && PlexError is not null && current is { InLibrary.Count: > 0 }
                && _session.Client is { } client && _session.Section is { } section)
            {
                await SyncAsync(client, section.Key, current.InLibrary);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex) { _log.LogWarning(ex, "Last.fm refresh failed"); }
    }
    /// <summary>The chart as it stands. While a newer chart loads, the last one (from this run or the previous one) is returned straight away.</summary>
    public Task<Trending?> GetAsync()
    {
        lock (_gate)
        {
            if (!IsOn) return Task.FromResult<Trending?>(null);
            Restore();
            var now = DateTimeOffset.UtcNow;
            if (_load is not { IsFaulted: false, IsCanceled: false } task || task.IsCompleted && now - _tracksAt >= ChartLife && now >= _tracksRetryAt)
            {
                _tracksRetryAt = now + RetryAfterSuccess;
                task = _load = LoadTracksAsync(_version);
            }
            return !task.IsCompleted && _current is { } kept ? Task.FromResult<Trending?>(kept) : task;
        }
    }
    private void Restore()
    {
        if (_session.Client is null || _session.Section is null) return;
        var scope = PlaylistScope;
        if (_restoredScope == scope) return;
        _restoredScope = scope;
        if (_rails.Get(scope) is not { } saved) return;
        var now = DateTimeOffset.UtcNow;
        if (_current is null && saved.Tracks.Count > 0 && now - saved.TracksAt < KeepSavedFor)
        {
            _current = new Trending(saved.TracksAt, saved.Scanned, saved.Tracks);
            _tracksAt = saved.TracksAt;
            _load ??= Task.FromResult<Trending?>(_current);
            Positions = _chartHistory.Get(scope);
            Playlist = DescribePlaylist(saved.Tracks.Count, Playlist.Thumb);
        }
        if (_currentArtists.Count == 0 && saved.Artists.Count > 0 && now - saved.ArtistsAt < KeepSavedFor)
        {
            _currentArtists = saved.Artists;
            _artistsAt = saved.ArtistsAt;
            _artists ??= Task.FromResult<IReadOnlyList<Artist>>(_currentArtists);
            ArtistPositions = _chartHistory.Get(scope + ":artists");
        }
    }
    private void RetrySoon(ref DateTimeOffset retryAt)
    {
        retryAt = DateTimeOffset.UtcNow + RetryAfterFailure;
        _timer.Change(RetryAfterFailure + TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(10));
    }
    private async Task<LiveChart?> LiveChartAsync()
    {
        try { return await LiveChartReader.GetAsync().ConfigureAwait(false); }
        catch (LastFmException ex)
        {
            // The Last.fm API chart still works without the website's live chart, so the rails carry on with that.
            _log.LogInformation("Last.fm live chart unavailable, using the API chart: {Message}", ex.Message);
            return null;
        }
    }
    public Task RefreshAsync(bool artists)
    {
        lock (_gate)
        {
            if (artists && _artists?.IsCompleted != false) _artists = null;
            if (!artists && _load?.IsCompleted != false) _load = null;
        }
        return artists ? GetArtistsAsync() : GetAsync();
    }
    private async Task<Trending?> LoadTracksAsync(int version)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return null;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var fresh = MelodayMusic.Core.Caching.ResponseCache.FetchFresh();
            // The library, the website's live chart and the first API page don't depend on each other, so they load together.
            var libraryTask = Paging.AllAsync((start, size) => client.GetTracksAsync(section.Key, start, size), 1000);
            var liveTask = LiveChartAsync();
            var next = _lastFm.ChartPageAsync(1);
            var library = await libraryTask.ConfigureAwait(false);
            var liveTracks = (await liveTask.ConfigureAwait(false))?.Tracks ?? [];
            _log.LogInformation("Last.fm chart sources ready in {Elapsed} ms: {Library} library songs, {Live} live chart songs", clock.ElapsedMilliseconds, library.Count, liveTracks.Count);
            var selection = new LibraryChart(library);
            foreach (var entry in liveTracks) selection.Add(entry);
            var entries = liveTracks.Select(entry => (MelodayText.CanonicalTitle(entry.Title), MelodayText.NormalizeArtist(entry.Artist))).ToHashSet();
            var scanned = liveTracks.Count;
            for (var page = 1; !selection.IsFull; page++)
            {
                var chart = await next.ConfigureAwait(false);
                // Ask for the following page while this one is matched, since a chart often needs several pages to fill.
                next = chart.Count < 100 ? Task.FromResult<IReadOnlyList<ChartTrack>>([]) : _lastFm.ChartPageAsync(page + 1);
                var added = 0;
                foreach (var entry in chart)
                {
                    if (!entries.Add((MelodayText.CanonicalTitle(entry.Title), MelodayText.NormalizeArtist(entry.Artist)))) continue;
                    added++; scanned++;
                    selection.Add(entry);
                    if (selection.IsFull) break;
                }
                if (chart.Count < 100 || added == 0) break;
            }
            var tracks = new SongSet().AddNew(selection.Tracks);
            if (version != _version || _session.Client != client || _session.Section?.Key != section.Key || !IsOn) return null;
            var cover = await PlaylistCover.RenderAsync(PlaylistScope + ":" + PlaylistKey, Playlist.Title, _log).ConfigureAwait(false);
            Trending current;
            lock (_gate)
            {
                if (version != _version || _session.Client != client || _session.Section?.Key != section.Key || !IsOn) return null;
                _tracksAt = DateTimeOffset.UtcNow;
                _tracksRetryAt = _tracksAt + RetryAfterSuccess;
                TracksError = null;
                CoverPath = cover;
                try
                {
                    Positions = _chartHistory.Update(PlaylistScope, tracks.Select(t => t.RatingKey));
                    _rails.SaveTracks(PlaylistScope, tracks, scanned, _tracksAt);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogWarning(ex, "Chart history could not be saved"); }
                Playlist = DescribePlaylist(tracks.Count, cover);
                current = _current = new Trending(_tracksAt, scanned, tracks);
            }
            _log.LogInformation("Last.fm Top 100 ready in {Elapsed} ms: {Count} songs from {Scanned} chart entries", clock.ElapsedMilliseconds, tracks.Count, scanned);
            Changed?.Invoke(this, EventArgs.Empty);
            // Updating the Plex playlist moves songs one at a time, so Home shows the chart first and Plex catches up after.
            if (KeepOnPlex)
                _ = SyncAsync(client, section.Key, tracks).ContinueWith(_ => { if (PlexError is not null) Changed?.Invoke(this, EventArgs.Empty); }, TaskScheduler.Default);
            return current;
        }
        catch (Exception ex) when (ex is LastFmException or PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Last.fm tracks unavailable");
            lock (_gate)
            {
                if (version != _version || !IsOn) return null;
                if (_current is null) RetrySoon(ref _tracksRetryAt);
                TracksError = "Last.fm is unavailable right now. Try again shortly.";
                return _current;
            }
        }
    }
    public Task<IReadOnlyList<Artist>> GetArtistsAsync()
    {
        lock (_gate)
        {
            if (!_lastFm.TrendingArtistsOn) return Task.FromResult<IReadOnlyList<Artist>>([]);
            Restore();
            var now = DateTimeOffset.UtcNow;
            if (_artists is not { IsFaulted: false, IsCanceled: false } task || task.IsCompleted && now - _artistsAt >= ChartLife && now >= _artistsRetryAt)
            {
                _artistsRetryAt = now + RetryAfterSuccess;
                task = _artists = LoadArtistsAsync(_version);
            }
            return !task.IsCompleted && _currentArtists.Count > 0 ? Task.FromResult(_currentArtists) : task;
        }
    }
    private async Task<IReadOnlyList<Artist>> LoadArtistsAsync(int version)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var fresh = MelodayMusic.Core.Caching.ResponseCache.FetchFresh();
            var libraryTask = Paging.AllAsync((start, size) => client.GetArtistsAsync(section.Key, start, size), 1000);
            var liveTask = LiveChartAsync();
            var next = _lastFm.ArtistChartPageAsync(1);
            var library = await libraryTask.ConfigureAwait(false);
            var liveArtists = (await liveTask.ConfigureAwait(false))?.Artists ?? [];
            var lookup = library.ToLookup(a => MelodayText.NormalizeArtist(a.Title));
            var result = new List<Artist>(); var seen = new HashSet<string>();
            foreach (var name in liveArtists)
            {
                var key = MelodayText.NormalizeArtist(name);
                if (lookup[key].FirstOrDefault() is { } artist && seen.Add(artist.RatingKey))
                {
                    var copy = JsonSerializer.SerializeToNode(artist)!;
                    copy[nameof(Artist.Title)] = name;
                    result.Add(copy.Deserialize<Artist>()!);
                }
            }
            var entries = liveArtists.Select(MelodayText.NormalizeArtist).ToHashSet();
            for (var page = 1; result.Count < 100; page++)
            {
                var chart = await next.ConfigureAwait(false);
                next = chart.Count < 100 ? Task.FromResult<IReadOnlyList<string>>([]) : _lastFm.ArtistChartPageAsync(page + 1);
                var added = 0;
                foreach (var name in chart)
                {
                    var key = MelodayText.NormalizeArtist(name);
                    if (!entries.Add(key)) continue;
                    added++;
                    if (lookup[key].FirstOrDefault() is { } artist && seen.Add(artist.RatingKey))
                    {
                        var copy = JsonSerializer.SerializeToNode(artist)!;
                        copy[nameof(Artist.Title)] = name;
                        result.Add(copy.Deserialize<Artist>()!);
                    }
                    if (result.Count == 100) break;
                }
                if (chart.Count < 100 || added == 0) break;
            }
            lock (_gate)
            {
                if (version != _version || _session.Client != client || _session.Section?.Key != section.Key || !_lastFm.TrendingArtistsOn) return [];
                _artistsAt = DateTimeOffset.UtcNow;
                _artistsRetryAt = _artistsAt + RetryAfterSuccess;
                ArtistsError = null;
                try
                {
                    ArtistPositions = _chartHistory.Update(PlaylistScope + ":artists", result.Select(a => a.RatingKey));
                    _rails.SaveArtists(PlaylistScope, result, _artistsAt);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.LogWarning(ex, "Artist chart history could not be saved"); }
                _currentArtists = result;
            }
            _log.LogInformation("Last.fm Trending Artists ready in {Elapsed} ms: {Count} artists", clock.ElapsedMilliseconds, result.Count);
            Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
        catch (Exception ex) when (ex is LastFmException or PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Last.fm artists unavailable");
            lock (_gate)
            {
                if (version != _version || !_lastFm.TrendingArtistsOn) return [];
                if (_currentArtists.Count == 0) RetrySoon(ref _artistsRetryAt);
                ArtistsError = "Last.fm is unavailable right now. Try again shortly.";
                return _currentArtists;
            }
        }
    }
    public async Task PlayArtistsAsync(IReadOnlyList<Artist> artists)
    {
        if (_session.Client is not { } client) return;
        var tracks = new List<Track>();
        foreach (var artist in artists) tracks.AddRange(await client.GetArtistTracksAsync(artist.RatingKey));
        await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(MelodayText.Dedupe(tracks));
    }
    public async Task SetKeepOnPlexAsync(bool on)
    {
        _settings.Current.LastFmPlexPlaylist = on; _settings.Save();
        if (on && await GetAsync() is { InLibrary.Count: > 0 } current && _session.Client is { } client && _session.Section is { } section)
            await SyncAsync(client, section.Key, current.InLibrary);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private async Task SyncAsync(PlexServerClient client, string section, IReadOnlyList<Track> tracks)
    {
        await _sync.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsOn || !KeepOnPlex || _session.Client != client || _session.Section?.Key != section) return;
            PlexError = null;
            var scope = client.Server.MachineIdentifier + ":" + section + ":" + _session.Account?.Id;
            var keys = tracks.Select(t => t.RatingKey).ToList();
            _settings.Current.LastFmPlaylistKeys.TryGetValue(scope, out var saved);
            Playlist? playlist = null;
            if (saved is not null)
            {
                try { playlist = await client.GetPlaylistAsync(saved).ConfigureAwait(false); }
                catch (PlexApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
            }
            if (playlist is null)
            {
                if (tracks.Count == 0) return;
                playlist = await client.CreatePlaylistAsync(Playlist.Title, keys).ConfigureAwait(false);
                if (playlist is null) throw new IOException("Plex did not create the playlist.");
                _settings.Current.LastFmPlaylistKeys[scope] = playlist.RatingKey; _settings.Save();
            }
            else
            {
                var existing = new List<Track>();
                for (var start = 0; ; start += 200)
                {
                    var page = await client.GetPlaylistItemsAsync(playlist.RatingKey, start, 200).ConfigureAwait(false);
                    existing.AddRange(page.Items); if (page.Items.Count < 200) break;
                }
                if (!existing.Select(t => t.RatingKey).SequenceEqual(keys))
                {
                    var have = existing.Select(t => t.RatingKey).ToHashSet();
                    var missing = keys.Where(k => !have.Contains(k)).ToList();
                    if (missing.Count > 0) await client.AddToPlaylistAsync(playlist.RatingKey, missing).ConfigureAwait(false);
                    var wanted = keys.ToHashSet(); var kept = new HashSet<string>();
                    foreach (var item in existing)
                        if ((!wanted.Contains(item.RatingKey) || !kept.Add(item.RatingKey)) && item.PlaylistItemId is { } id)
                            await client.RemovePlaylistItemAsync(playlist.RatingKey, id).ConfigureAwait(false);
                    var updated = await client.GetPlaylistItemsAsync(playlist.RatingKey, 0, 200).ConfigureAwait(false);
                    var ids = updated.Items.Where(t => t.PlaylistItemId is not null).ToDictionary(t => t.RatingKey, t => t.PlaylistItemId!);
                    string? after = null;
                    foreach (var key in keys)
                    {
                        if (!ids.TryGetValue(key, out var id)) throw new IOException("Plex did not return all playlist items.");
                        await client.MovePlaylistItemAsync(playlist.RatingKey, id, after).ConfigureAwait(false); after = id;
                    }
                }
            }
            await client.EditPlaylistAsync(playlist.RatingKey, Playlist.Title, "Last.fm's highest-charting songs in your library. Updated every six hours while Meloday Music is open.").ConfigureAwait(false);
            var cover = await PlaylistCover.RenderAsync(scope + ":" + PlaylistKey, Playlist.Title, _log).ConfigureAwait(false);
            if (cover is not null) await client.SetPlaylistCoverAsync(playlist.RatingKey, await File.ReadAllBytesAsync(cover)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlexException or IOException or HttpRequestException or TaskCanceledException)
        { PlexError = ex.Message; _log.LogWarning(ex, "Last.fm Plex update failed"); }
        finally { _sync.Release(); }
    }
}
