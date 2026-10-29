using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record FeaturedPlaylist(Playlist Playlist, int Songs);

public sealed class PlaylistArtistIndex
{
    private const int MaxPlaylistSize = 2500;
    private static readonly TimeSpan SmartRefresh = TimeSpan.FromHours(12);
    private static readonly TimeSpan WarmUpDelay = TimeSpan.FromSeconds(20);

    private sealed class Entry
    {
        public string Title { get; set; } = string.Empty;
        public string? Thumb { get; set; }
        public string? Composite { get; set; }
        public int? TrackCount { get; set; }
        public long DurationMs { get; set; }
        public bool Smart { get; set; }
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset FetchedAt { get; set; }
        public List<string?[]> Credits { get; set; } = [];

        public Playlist ToPlaylist(string key, int songs) => new()
        {
            RatingKey = key,
            Title = Title,
            Thumb = Thumb,
            Composite = Composite,
            TrackCount = TrackCount,
            DurationMs = DurationMs,
            IsSmart = Smart,
            UpdatedAt = UpdatedAt,
            SubtitleOverride = songs switch { 0 => null, 1 => "1 song", _ => $"{songs} songs" },
        };
    }

    private readonly PlexSession _session;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, Entry>? _entries;
    private string? _server;
    private Task? _refresh;
    private DateTime _refreshedAt;

    public PlaylistArtistIndex(PlexSession session, ILogger<PlaylistArtistIndex> log)
    {
        _session = session;
        _log = log;
        _session.StateChanged += (_, _) =>
        {
            if (_session.IsReady) _ = Task.Delay(WarmUpDelay).ContinueWith(_ => EnsureFreshAsync(), TaskScheduler.Default);
        };
    }

    public async Task<IReadOnlyList<FeaturedPlaylist>> GetFeaturingAsync(Artist artist, CancellationToken ct = default)
    {
        await LoadAsync().ConfigureAwait(false);
        var refresh = EnsureFreshAsync();
        if (_entries is not { Count: > 0 }) await refresh.WaitAsync(ct).ConfigureAwait(false);
        var entries = _entries;
        if (entries is null) return [];
        return entries
            .Select(e => (Key: e.Key, Entry: e.Value, Songs: e.Value.Credits.Count(c =>
                c.Length > 0 && c[0] == artist.RatingKey || c.Length > 1 && ArtistCredit.Mentions(c[1], artist.Title))))
            .Select(x => (x.Key, x.Entry, x.Songs, Named: ArtistCredit.NamedIn(x.Entry.Title, artist.Title)))
            .Where(x => x.Songs > 0 || x.Named)
            .OrderByDescending(x => x.Named)
            .ThenByDescending(x => x.Songs)
            .ThenBy(x => x.Entry.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new FeaturedPlaylist(x.Entry.ToPlaylist(x.Key, x.Songs), x.Songs))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<string, double>> CoPlayAsync(string artistRatingKey, CancellationToken ct = default)
    {
        await LoadAsync().ConfigureAwait(false);
        _ = EnsureFreshAsync();
        var shared = new Dictionary<string, double>();
        if (_entries is not { } entries) return shared;
        foreach (var entry in entries.Values)
        {
            var artists = entry.Credits.Where(c => c.Length > 0 && c[0] is not null).Select(c => c[0]!).Distinct().ToList();
            if (artists.Count < 2 || !artists.Contains(artistRatingKey)) continue;
            var weight = 1 / Math.Sqrt(entry.Credits.Count);
            foreach (var other in artists)
                if (other != artistRatingKey) shared[other] = shared.GetValueOrDefault(other) + weight;
        }
        return shared;
    }

    public void Reset()
    {
        _entries = null;
        _server = null;
        _refresh = null;
    }

    private Task EnsureFreshAsync()
    {
        if (_refresh is { IsCompleted: false } running) return running;
        if (_refresh is not null && DateTime.UtcNow - _refreshedAt < TimeSpan.FromMinutes(10)) return _refresh;
        _refreshedAt = DateTime.UtcNow;
        return _refresh = Task.Run(RefreshAsync);
    }

    private async Task RefreshAsync()
    {
        if (_session.Client is not { } client) return;
        try
        {
            await LoadAsync().ConfigureAwait(false);
            var playlists = new List<Playlist>();
            for (var start = 0; start < 5000; start += 300)
            {
                var page = await client.GetPlaylistsAsync(start, 300).ConfigureAwait(false);
                playlists.AddRange(page.Items);
                if (!page.HasMore || page.Items.Count == 0) break;
            }

            var known = _entries ?? [];
            var next = new Dictionary<string, Entry>();
            var stale = new List<Playlist>();
            foreach (var p in playlists)
            {
                if (p.TrackCount > MaxPlaylistSize)
                {
                    next[p.RatingKey] = Describe(new Entry { FetchedAt = DateTimeOffset.UtcNow }, p);
                    continue;
                }
                if (known.TryGetValue(p.RatingKey, out var entry) && entry.UpdatedAt == p.UpdatedAt
                    && (!p.IsSmart || DateTimeOffset.UtcNow - entry.FetchedAt < SmartRefresh))
                    next[p.RatingKey] = Describe(entry, p);
                else
                    stale.Add(p);
            }

            using var throttle = new SemaphoreSlim(3, 3);
            await Task.WhenAll(stale.Select(async p =>
            {
                await throttle.WaitAsync().ConfigureAwait(false);
                try
                {
                    var credits = await client.GetPlaylistCreditsAsync(p.RatingKey).ConfigureAwait(false);
                    var entry = Describe(new Entry { FetchedAt = DateTimeOffset.UtcNow }, p);
                    entry.Credits = credits.Select(c => new[] { c.ArtistRatingKey, c.TrackArtist }).ToList();
                    lock (next) next[p.RatingKey] = entry;
                }
                catch (PlexException ex)
                {
                    _log.LogInformation("Playlist {Playlist} skipped: {Message}", p.Title, ex.Message);
                    if (known.TryGetValue(p.RatingKey, out var old)) lock (next) next[p.RatingKey] = old;
                }
                finally
                {
                    throttle.Release();
                }
            })).ConfigureAwait(false);

            _entries = next;
            if (stale.Count > 0) await SaveAsync(next).ConfigureAwait(false);
            _log.LogInformation("Playlist index: {Count} playlists ({Read} read)", next.Count, stale.Count);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Playlist index not refreshed: {Message}", ex.Message);
        }
    }

    private static Entry Describe(Entry entry, Playlist p)
    {
        entry.Title = p.Title;
        entry.Thumb = p.Thumb;
        entry.Composite = p.Composite;
        entry.TrackCount = p.TrackCount;
        entry.DurationMs = p.DurationMs;
        entry.Smart = p.IsSmart;
        entry.UpdatedAt = p.UpdatedAt;
        return entry;
    }

    private string? CachePath => _session.Server?.MachineIdentifier is { } server
        ? Path.Combine(AppPaths.Root, "cache", $"playlist-artists-{server}.json")
        : null;

    private async Task LoadAsync()
    {
        var server = _session.Server?.MachineIdentifier;
        if (_entries is not null && _server == server) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_entries is not null && _server == server) return;
            _server = server;
            _entries = null;
            if (CachePath is { } path && File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    _entries = await JsonSerializer.DeserializeAsync<Dictionary<string, Entry>>(stream).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _entries = null;
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveAsync(Dictionary<string, Entry> entries)
    {
        if (CachePath is not { } path) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await using (var stream = File.Create(temp)) await JsonSerializer.SerializeAsync(stream, entries).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (IOException ex)
        {
            _log.LogInformation("Playlist index not saved: {Message}", ex.Message);
        }
    }
}
