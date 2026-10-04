using System.Globalization;
using System.Text;
using HumeMusic.Core.Caching;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex.Dto;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Plex;

/// <summary>Plex library item type ids used by the /all endpoint.</summary>
public static class PlexTypes
{
    public const int Artist = 8;
    public const int Album = 9;
    public const int Track = 10;
    public const int Playlist = 15;
}

/// <summary>Playback states understood by the /:/timeline endpoint.</summary>
public enum TimelineState
{
    Playing,
    Paused,
    Buffering,
    Stopped,
}

/// <summary>
/// Low-level access to one Plex Media Server over a chosen connection. Knows the HTTP endpoints
/// and returns typed models; it holds no UI or playback state.
/// </summary>
public sealed class PlexServerClient
{
    private const string LibraryIdentifier = "com.plexapp.plugins.library";

    private readonly PlexHttp _http;
    private readonly ResponseCache _cache;
    private readonly ILogger _log;

    public PlexServerClient(PlexHttp http, PlexServer server, ServerConnection connection, ResponseCache cache, ILogger log)
    {
        _http = http;
        Server = server;
        Connection = connection;
        _cache = cache;
        _log = log;
        BaseUri = new Uri(connection.Uri.AbsoluteUri.TrimEnd('/') + "/");
    }

    public PlexServer Server { get; }
    public ServerConnection Connection { get; }
    public Uri BaseUri { get; }
    private string? Token => Server.AccessToken;

    private static readonly TimeSpan ShortTtl = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MediumTtl = TimeSpan.FromMinutes(5);

    // ---------------------------------------------------------------- core request helpers

    internal Uri Build(string path, IEnumerable<KeyValuePair<string, string?>>? query = null)
    {
        var sb = new StringBuilder(path.TrimStart('/'));
        var first = !path.Contains('?');
        if (query is not null)
        {
            foreach (var (k, v) in query)
            {
                if (v is null) continue;
                sb.Append(first ? '?' : '&');
                first = false;
                sb.Append(k).Append('=').Append(Uri.EscapeDataString(v));
            }
        }
        return new Uri(BaseUri, sb.ToString());
    }

    public async Task<MediaContainerDto> GetContainerAsync(Uri uri, CancellationToken ct, TimeSpan? cacheTtl = null)
    {
        async Task<MediaContainerDto> Fetch()
        {
            var response = await _http.SendJsonAsync<PmsResponse>(HttpMethod.Get, uri, Token, ct).ConfigureAwait(false);
            return response.MediaContainer ?? new MediaContainerDto();
        }

        if (cacheTtl is null) return await Fetch().ConfigureAwait(false);
        return await _cache.GetOrAddAsync($"{Server.MachineIdentifier}|{uri.PathAndQuery}", cacheTtl.Value, Fetch).ConfigureAwait(false);
    }

    private Task<MediaContainerDto> GetAsync(string path, IEnumerable<KeyValuePair<string, string?>>? query, CancellationToken ct, TimeSpan? ttl = null)
        => GetContainerAsync(Build(path, query), ct, ttl);

    private static IEnumerable<KeyValuePair<string, string?>> Page(int start, int size) =>
    [
        new("X-Plex-Container-Start", start.ToString(CultureInfo.InvariantCulture)),
        new("X-Plex-Container-Size", size.ToString(CultureInfo.InvariantCulture)),
    ];

    private static PagedResult<T> ToPage<T>(MediaContainerDto c, int start, Func<MetadataDto, T?> map) where T : class
    {
        var items = (c.Metadata ?? []).Select(map).Where(x => x is not null).Select(x => x!).ToList();
        return new PagedResult<T> { Items = items, Offset = c.Offset ?? start, TotalSize = c.TotalSize ?? (c.Offset is null ? null : c.Size) };
    }

    private static readonly AsyncLocal<bool> Sampling = new();

    /// <summary>
    /// Within the returned scope, random tracks are a sample for showing (a mix's artists, a station's
    /// picture), so the last sample is reused while a new one is fetched behind it. Music to play is
    /// always asked for fresh.
    /// </summary>
    public static IDisposable ForShowing()
    {
        var before = Sampling.Value;
        Sampling.Value = true;
        return new Scope(() => Sampling.Value = before);
    }

    private sealed class Scope(Action end) : IDisposable
    {
        public void Dispose() => end();
    }

    public void InvalidateCache() => _cache.Invalidate(k => k.StartsWith(Server.MachineIdentifier + "|", StringComparison.Ordinal));

    /// <summary>
    /// Answers held in memory are let go; those kept on disk are still shown at once, and fetched
    /// again behind them (coming back to the app after a while).
    /// </summary>
    public void MarkStale() => _cache.MarkStale();

    /// <summary>Every answer kept on disk is checked with the server before it's used again (the library changed).</summary>
    public void RequireRevalidation() => _cache.RequireRevalidation();

    /// <summary>Forgets every kept answer, in memory and on disk (Settings → Refresh Library).</summary>
    public void ClearCache() => _cache.Clear();

    /// <summary>
    /// Asks Plex to look through the library's folders for music added, changed or removed (as Plex's
    /// own Scan Library Files does). Plex does the scan; the music itself is untouched.
    /// </summary>
    public Task ScanLibraryAsync(string sectionKey, CancellationToken ct = default) =>
        _http.SendAndForgetBodyAsync(HttpMethod.Get, Build($"/library/sections/{sectionKey}/refresh"), Token, ct);

    // ---------------------------------------------------------------- sections

    public async Task<IReadOnlyList<MusicSection>> GetMusicSectionsAsync(CancellationToken ct = default)
    {
        var c = await GetAsync("/library/sections", null, ct).ConfigureAwait(false);
        return (c.Directory ?? [])
            .Where(d => d.Type == "artist" && !string.IsNullOrEmpty(d.Key))
            .Select(PlexMapper.ToSection)
            .ToList();
    }

    // ---------------------------------------------------------------- browsing

    public async Task<PagedResult<Artist>> GetArtistsAsync(string sectionKey, int start, int size, string sort = "titleSort", CancellationToken ct = default, params KeyValuePair<string, string?>[] filters)
    {
        var q = new List<KeyValuePair<string, string?>> { new("type", "8"), new("sort", sort) };
        q.AddRange(filters);
        q.AddRange(Page(start, size));
        var c = await GetAsync($"/library/sections/{sectionKey}/all", q, ct, ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, PlexMapper.ToArtist);
    }

    public async Task<PagedResult<Album>> GetAlbumsAsync(string sectionKey, int start, int size, string sort = "titleSort", CancellationToken ct = default, params KeyValuePair<string, string?>[] filters)
    {
        var q = new List<KeyValuePair<string, string?>> { new("type", "9"), new("sort", sort) };
        q.AddRange(filters);
        q.AddRange(Page(start, size));
        var c = await GetAsync($"/library/sections/{sectionKey}/all", q, ct, ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, PlexMapper.ToAlbum);
    }

    public async Task<PagedResult<Track>> GetTracksAsync(string sectionKey, int start, int size, string sort = "titleSort", CancellationToken ct = default, params KeyValuePair<string, string?>[] filters)
    {
        var q = new List<KeyValuePair<string, string?>> { new("type", "10"), new("sort", sort) };
        q.AddRange(filters);
        q.AddRange(Page(start, size));
        // A random order is asked for to get fresh tracks each time, so it isn't served from the cache,
        // except for a sample shown on screen (a mix's board): the last one is shown and a new one fetched behind.
        var c = await GetAsync($"/library/sections/{sectionKey}/all", q, ct, sort == "random" && !Sampling.Value ? null : ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, PlexMapper.ToTrack);
    }

    public async Task<MetadataDto?> GetMetadataAsync(string ratingKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{ratingKey}", null, ct, ShortTtl).ConfigureAwait(false);
        return c.Metadata?.FirstOrDefault();
    }

    /// <summary>
    /// Fetches several items in one request (/library/metadata/1,2,3). Items that no longer exist
    /// are simply absent from the result, which callers use to detect deleted content.
    /// </summary>
    public async Task<IReadOnlyList<MediaItem>> GetItemsAsync(IEnumerable<string> ratingKeys, CancellationToken ct = default)
    {
        var results = new List<MediaItem>();
        foreach (var chunk in ratingKeys.Distinct().Chunk(50))
        {
            var c = await GetAsync($"/library/metadata/{string.Join(',', chunk)}", null, ct).ConfigureAwait(false);
            results.AddRange((c.Metadata ?? []).Select(PlexMapper.ToMediaItem).Where(i => i is not null).Select(i => i!));
        }
        return results;
    }

    public async Task<Artist?> GetArtistAsync(string ratingKey, CancellationToken ct = default)
        => await GetMetadataAsync(ratingKey, ct).ConfigureAwait(false) is { } m ? PlexMapper.ToArtist(m) : null;

    public async Task<Album?> GetAlbumAsync(string ratingKey, CancellationToken ct = default)
        => await GetMetadataAsync(ratingKey, ct).ConfigureAwait(false) is { } m ? PlexMapper.ToAlbum(m) : null;

    public async Task<Track?> GetTrackAsync(string ratingKey, CancellationToken ct = default)
        => await GetMetadataAsync(ratingKey, ct).ConfigureAwait(false) is { } m ? PlexMapper.ToTrack(m) : null;

    public async Task<IReadOnlyList<Album>> GetArtistAlbumsAsync(string artistKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{artistKey}/children", [new("excludeAllLeaves", "1")], ct, ShortTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Where(m => m.Type == "album").Select(PlexMapper.ToAlbum).ToList();
    }

    public async Task<IReadOnlyList<Track>> GetArtistTracksAsync(string artistKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{artistKey}/allLeaves", null, ct, ShortTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToTrack).ToList();
    }

    public async Task<IReadOnlyList<Track>> GetAlbumTracksAsync(string albumKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{albumKey}/children", null, ct, ShortTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToTrack).ToList();
    }

    /// <summary>
    /// Most popular tracks by an artist, using the same filters as Artist.popularTracks() in the
    /// Plex API reference (ranked by Last.fm listener count, excluding compilations and live albums).
    /// </summary>
    public async Task<IReadOnlyList<Track>> GetPopularTracksAsync(string sectionKey, string artistKey, int limit = 10, CancellationToken ct = default)
    {
        var q = new List<KeyValuePair<string, string?>>
        {
            new("type", "10"),
            new("album.subformat!", "Compilation,Live"),
            new("artist.id", artistKey),
            new("group", "title"),
            new("ratingCount>>", "0"),
            new("sort", "ratingCount:desc"),
        };
        q.AddRange(Page(0, limit));
        var c = await GetAsync($"/library/sections/{sectionKey}/all", q, ct, MediumTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToTrack).ToList();
    }

    /// <summary>Sonically similar items (Audio.sonicallySimilar in the Plex API reference).</summary>
    public async Task<IReadOnlyList<T>> GetSonicallySimilarAsync<T>(string ratingKey, int limit = 20, double maxDistance = 0.25, CancellationToken ct = default) where T : MediaItem
    {
        var q = new List<KeyValuePair<string, string?>>
        {
            new("limit", limit.ToString(CultureInfo.InvariantCulture)),
            new("maxDistance", maxDistance.ToString(CultureInfo.InvariantCulture)),
        };
        var c = await GetAsync($"/library/metadata/{ratingKey}/nearest", q, ct, MediumTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToMediaItem).OfType<T>().ToList();
    }

    /// <summary>
    /// A section's listening history since a time, newest first (/status/sessions/history/all): the
    /// track, its album and artist, and when it was played. Only the given account's plays, when set.
    /// </summary>
    public async Task<IReadOnlyList<Meloday.HistoryPlay>> GetHistoryAsync(string sectionKey, DateTimeOffset since, string? accountId = null, int max = 6000, CancellationToken ct = default)
    {
        const int PageSize = 1000;
        var plays = new List<Meloday.HistoryPlay>();
        for (var start = 0; start < max; start += PageSize)
        {
            var q = new List<KeyValuePair<string, string?>>
            {
                new("sort", "viewedAt:desc"),
                new("librarySectionID", sectionKey),
                new("viewedAt>", since.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
            };
            if (accountId is not null) q.Add(new("accountID", accountId));
            q.AddRange(Page(start, PageSize));
            var c = await GetAsync("/status/sessions/history/all", q, ct).ConfigureAwait(false);
            var page = c.Metadata ?? [];
            foreach (var m in page)
            {
                if (m.Type != "track" || m.RatingKey is not { } key || m.ViewedAt is not { } at) continue;
                var album = m.ParentRatingKey ?? m.ParentKey?[(m.ParentKey.LastIndexOf('/') + 1)..];
                plays.Add(new Meloday.HistoryPlay(key, album, m.GrandparentTitle, DateTimeOffset.FromUnixTimeSeconds(at)));
            }
            if (page.Count < PageSize) break;
        }
        return plays;
    }

    /// <summary>
    /// A track's lyrics (LyricFind, through Plex Pass): synced when Plex has times for them, or null
    /// when it has none. Plex sometimes answers a lyrics request with a 404 that a moment later works,
    /// so a failed fetch is tried once more.
    /// </summary>
    public async Task<TrackLyrics?> GetLyricsAsync(string ratingKey, CancellationToken ct = default)
    {
        var meta = await GetAsync($"/library/metadata/{ratingKey}", null, ct, MediumTtl).ConfigureAwait(false);
        var streams = meta.Metadata?.FirstOrDefault()?.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.Stream ?? [];
        return await ReadLyricsAsync(streams, ct).ConfigureAwait(false);
    }

    private async Task<TrackLyrics?> ReadLyricsAsync(IReadOnlyList<StreamDto> streams, CancellationToken ct)
    {
        // Synced lyrics first, plain text otherwise.
        var stream = streams.Where(s => s.StreamType == 4 && !string.IsNullOrEmpty(s.Key))
            .OrderByDescending(s => s.Timed is "1" or "true").FirstOrDefault();
        if (stream is null) return null;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var content = await GetAsync(stream.Key!, null, ct, MediumTtl).ConfigureAwait(false);
                var lines = (content.Lyrics?.FirstOrDefault()?.Line ?? [])
                    .Select(l => new LyricLine(
                        l.StartOffset is { } start ? TimeSpan.FromMilliseconds(start) : null,
                        l.EndOffset is { } end ? TimeSpan.FromMilliseconds(end) : null,
                        string.Concat((l.Span ?? []).Select(sp => sp.Text)).Trim()))
                    .ToList();
                // Trailing and leading blank lines carry nothing.
                while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
                while (lines.Count > 0 && lines[0].Text.Length == 0) lines.RemoveAt(0);
                // Synced lyrics sometimes leave a break (a blank line) without a time: it takes the time
                // of the line before, so the lyrics as a whole still follow the song.
                if (lines.Any(l => l.Start is not null))
                {
                    TimeSpan last = TimeSpan.Zero;
                    for (var i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].Start is { } start) last = start;
                        else lines[i] = lines[i] with { Start = last };
                    }
                }
                return lines.Count == 0 ? null : new TrackLyrics(lines);
            }
            catch (PlexException) when (attempt == 0)
            {
                await Task.Delay(400, ct).ConfigureAwait(false);
            }
            catch (PlexException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A track's loudness from Plex's analysis: its gain and its level across its length (the envelope
    /// Plexamp draws as a waveform), or null when the track hasn't been analysed.
    /// </summary>
    public async Task<Playback.TrackLoudness?> GetLoudnessAsync(string ratingKey, int points = 128, CancellationToken ct = default)
    {
        var meta = await GetAsync($"/library/metadata/{ratingKey}", null, ct, MediumTtl).ConfigureAwait(false);
        var streams = meta.Metadata?.FirstOrDefault()?.Media?.FirstOrDefault()?.Part?.FirstOrDefault()?.Stream ?? [];
        var audio = streams.FirstOrDefault(s => s.StreamType == 2);
        if (audio?.Id is not { } stream) return null;
        // Synced lyrics (Plex Pass) show where the singing starts and ends.
        var lyrics = await ReadLyricsAsync(streams, ct).ConfigureAwait(false);
        var vocals = lyrics is { IsSynced: true }
            ? Playback.VocalSpan.From(lyrics.Lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)).Select(l => (l.Start!.Value, l.End)).ToList())
            : null;
        double? gain = double.TryParse(audio.Gain, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var g) ? g : null;
        var levels = await GetAsync($"/library/streams/{stream}/levels", [new("subsample", points.ToString(CultureInfo.InvariantCulture))], ct, MediumTtl).ConfigureAwait(false);
        var values = (levels.Level ?? []).Select(l => l.V).ToList();
        return values.Count == 0 && gain is null ? null : new Playback.TrackLoudness(values, gain, vocals);
    }

    /// <summary>
    /// A short-lived token from the server (Plex's transient delegation token, valid about two days),
    /// for addresses handed to other devices (casting), so they never carry the account's own token.
    /// </summary>
    public async Task<string?> GetTransientTokenAsync(CancellationToken ct = default)
    {
        var c = await GetAsync("/security/token", [new("type", "delegation"), new("scope", "all")], ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(c.Token) ? null : c.Token;
    }

    /// <summary>Tracks forming a sonic path between two tracks (MusicSection.sonicAdventure).</summary>
    public async Task<IReadOnlyList<Track>> GetSonicAdventureAsync(string sectionKey, string startTrackKey, string endTrackKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/sections/{sectionKey}/computePath",
            [new("startID", startTrackKey), new("endID", endTrackKey)], ct).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToTrack).ToList();
    }

    // ---------------------------------------------------------------- hubs

    public async Task<IReadOnlyList<Hub>> GetSectionHubsAsync(string sectionKey, int count = 12, CancellationToken ct = default)
    {
        var c = await GetAsync($"/hubs/sections/{sectionKey}",
            [new("count", count.ToString(CultureInfo.InvariantCulture)), new("includeStations", "0")], ct, ShortTtl).ConfigureAwait(false);
        return (c.Hub ?? []).Select(PlexMapper.ToHub).Where(h => h.Items.Count > 0).ToList();
    }

    /// <summary>An item's own hubs, e.g. an artist's most popular and most played tracks.</summary>
    public async Task<IReadOnlyList<Hub>> GetItemHubsAsync(string ratingKey, int count = 12, CancellationToken ct = default)
    {
        var c = await GetAsync($"/hubs/metadata/{ratingKey}",
            [new("count", count.ToString(CultureInfo.InvariantCulture))], ct, MediumTtl).ConfigureAwait(false);
        return (c.Hub ?? []).Select(PlexMapper.ToHub).Where(h => h.Items.Count > 0).ToList();
    }

    public async Task<IReadOnlyList<Hub>> GetRelatedHubsAsync(string ratingKey, int count = 12, CancellationToken ct = default)
    {
        var c = await GetAsync($"/hubs/metadata/{ratingKey}/related",
            [new("count", count.ToString(CultureInfo.InvariantCulture))], ct, MediumTtl).ConfigureAwait(false);
        return (c.Hub ?? []).Select(PlexMapper.ToHub).Where(h => h.Items.Count > 0).ToList();
    }

    public async Task<IReadOnlyList<MediaItem>> GetHubItemsAsync(string hubKey, int start, int size, CancellationToken ct = default)
    {
        var c = await GetContainerAsync(Build(hubKey, Page(start, size)), ct, ShortTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToMediaItem).Where(i => i is not null).Select(i => i!).ToList();
    }

    // ---------------------------------------------------------------- tags

    public async Task<IReadOnlyList<TagItem>> GetTagsAsync(string sectionKey, TagKind kind, int libraryType = PlexTypes.Album, CancellationToken ct = default)
    {
        var field = TagField(kind);
        var c = await GetAsync($"/library/sections/{sectionKey}/{field}",
            [new("type", libraryType.ToString(CultureInfo.InvariantCulture))], ct, MediumTtl).ConfigureAwait(false);
        return (c.Directory ?? []).Select(d => PlexMapper.ToTag(d, kind)).Where(t => t is not null).Select(t => t!).ToList();
    }

    public static string TagField(TagKind kind) => kind switch
    {
        TagKind.Genre => "genre",
        TagKind.Mood => "mood",
        TagKind.Style => "style",
        TagKind.Decade => "decade",
        TagKind.Year => "year",
        TagKind.Country => "country",
        _ => "genre",
    };

    // ---------------------------------------------------------------- playlists & collections

    public async Task<PagedResult<Playlist>> GetPlaylistsAsync(int start, int size, CancellationToken ct = default, string sort = "titleSort")
    {
        var q = new List<KeyValuePair<string, string?>> { new("playlistType", "audio"), new("sort", sort) };
        q.AddRange(Page(start, size));
        var c = await GetAsync("/playlists", q, ct, ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, m => m.Type == "playlist" ? PlexMapper.ToPlaylist(m) : null);
    }

    public async Task<Playlist?> GetPlaylistAsync(string ratingKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/playlists/{ratingKey}", null, ct, ShortTtl).ConfigureAwait(false);
        return c.Metadata?.FirstOrDefault() is { } m ? PlexMapper.ToPlaylist(m) : null;
    }

    public async Task<PagedResult<Track>> GetPlaylistItemsAsync(string ratingKey, int start, int size, CancellationToken ct = default)
    {
        var c = await GetAsync($"/playlists/{ratingKey}/items", Page(start, size), ct, ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, m => m.Type == "track" ? PlexMapper.ToTrack(m) : null);
    }

    /// <summary>
    /// Who is credited on each track of a playlist (album artist key and track artist), fetched lean
    /// (no media, tags or artwork) and uncached: for working out which playlists an artist is on.
    /// </summary>
    public async Task<IReadOnlyList<(string? ArtistRatingKey, string? TrackArtist)>> GetPlaylistCreditsAsync(string ratingKey, CancellationToken ct = default)
    {
        const int page = 1000;
        var credits = new List<(string?, string?)>();
        for (var start = 0; start < 20000; start += page)
        {
            var q = new List<KeyValuePair<string, string?>>
            {
                new("excludeElements", "Media,Genre,Mood,Image,UltraBlurColors"),
                new("excludeFields", "summary,thumb,parentThumb,grandparentThumb,art,grandparentArt"),
            };
            q.AddRange(Page(start, page));
            var c = await GetAsync($"/playlists/{ratingKey}/items", q, ct).ConfigureAwait(false);
            var items = c.Metadata ?? [];
            credits.AddRange(items.Where(m => m.Type == "track").Select(m => (m.GrandparentRatingKey, PlexMapper.CleanTitle(m.OriginalTitle))));
            if (items.Count < page) break;
        }
        return credits;
    }

    public async Task<IReadOnlyList<MusicCollection>> GetCollectionsAsync(string sectionKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/sections/{sectionKey}/collections", null, ct, MediumTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToCollection).ToList();
    }

    /// <summary>Collections in a music section, paged and sortable (library type 18).</summary>
    public async Task<PagedResult<MusicCollection>> GetCollectionsAsync(string sectionKey, int start, int size, string sort, CancellationToken ct = default)
    {
        var q = new List<KeyValuePair<string, string?>> { new("type", "18"), new("sort", sort) };
        q.AddRange(Page(start, size));
        var c = await GetAsync($"/library/sections/{sectionKey}/all", q, ct, ShortTtl).ConfigureAwait(false);
        return ToPage(c, start, m => PlexMapper.ToCollection(m));
    }

    /// <summary>
    /// The artists in a playlist or collection, most frequent first (used when there is no summary).
    /// Only the first few hundred entries are sampled to keep this light.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetContainerArtistsAsync(MediaItem container, CancellationToken ct = default)
    {
        IEnumerable<string?> names;
        switch (container)
        {
            case Playlist p:
                var items = await GetPlaylistItemsAsync(p.RatingKey, 0, 300, ct).ConfigureAwait(false);
                names = items.Items.Select(t => t.DisplayArtist);
                break;
            case MusicCollection c:
                var children = await GetCollectionItemsAsync(c.RatingKey, ct).ConfigureAwait(false);
                names = children.Select(i => i switch
                {
                    Track t => t.DisplayArtist,
                    Album a => a.ArtistTitle,
                    Artist ar => ar.Title,
                    _ => null,
                });
                break;
            default:
                return [];
        }
        return ArtistRanking.Rank(names);
    }

    public async Task<IReadOnlyList<MediaItem>> GetCollectionItemsAsync(string ratingKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/collections/{ratingKey}/children", null, ct, ShortTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(PlexMapper.ToMediaItem).Where(i => i is not null).Select(i => i!).ToList();
    }

    // ---------------------------------------------------------------- search

    public async Task<SearchResults> SearchAsync(string query, string sectionKey, int limit = 12, CancellationToken ct = default)
    {
        var c = await GetAsync("/hubs/search",
        [
            new("query", query),
            new("limit", limit.ToString(CultureInfo.InvariantCulture)),
            new("includeCollections", "1"),
        ], ct).ConfigureAwait(false);

        var artists = new List<Artist>();
        var albums = new List<Album>();
        var tracks = new List<Track>();
        var playlists = new List<Playlist>();
        var collections = new List<MusicCollection>();
        var tags = new List<TagItem>();

        foreach (var hub in c.Hub ?? [])
        {
            foreach (var m in hub.Metadata ?? [])
            {
                // Global search spans all libraries; keep this music section plus section-less playlists.
                if (m.Type != "playlist" && !string.IsNullOrEmpty(m.LibrarySectionID) && m.LibrarySectionID != sectionKey) continue;
                switch (m.Type)
                {
                    case "artist": artists.Add(PlexMapper.ToArtist(m)); break;
                    case "album": albums.Add(PlexMapper.ToAlbum(m)); break;
                    case "track": tracks.Add(PlexMapper.ToTrack(m)); break;
                    case "playlist" when m.PlaylistType is null or "audio": playlists.Add(PlexMapper.ToPlaylist(m)); break;
                    case "collection" when !string.IsNullOrEmpty(m.LibrarySectionID): collections.Add(PlexMapper.ToCollection(m)); break;
                }
            }
            var kind = hub.Type switch { "genre" => TagKind.Genre, "mood" => TagKind.Mood, "style" => TagKind.Style, _ => (TagKind?)null };
            if (kind is not null)
            {
                foreach (var d in hub.Directory ?? [])
                {
                    if (!string.IsNullOrEmpty(d.LibrarySectionID) && d.LibrarySectionID != sectionKey) continue;
                    if (PlexMapper.ToTag(new DirectoryDto { Key = d.Id ?? d.Key, Title = d.Tag ?? d.Title, Count = d.Count }, kind.Value) is { } t)
                        tags.Add(t);
                }
            }
        }

        return new SearchResults
        {
            Artists = artists.DistinctBy(a => a.RatingKey).ToList(),
            Albums = albums.DistinctBy(a => a.RatingKey).ToList(),
            Tracks = tracks.DistinctBy(a => a.RatingKey).ToList(),
            Playlists = playlists.DistinctBy(a => a.RatingKey).ToList(),
            Collections = collections.DistinctBy(a => a.RatingKey).ToList(),
            Tags = tags.DistinctBy(t => t.RatingKey).ToList(),
        };
    }

    // ---------------------------------------------------------------- user actions & reporting

    /// <summary>The key of Plex's radio station for an artist (its artist mix), or null when there is none.</summary>
    public async Task<string?> GetArtistStationKeyAsync(string artistRatingKey, CancellationToken ct = default) =>
        (await GetArtistStationAsync(artistRatingKey, ct).ConfigureAwait(false))?.Key;

    /// <summary>Plex's radio station for an artist (its artist mix): key and title ("Nicki Minaj Radio").</summary>
    public async Task<(string Key, string Title)?> GetArtistStationAsync(string artistRatingKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{artistRatingKey}", [new("includeStations", "1")], ct, MediumTtl).ConfigureAwait(false);
        return c.Metadata?.FirstOrDefault()?.Stations?.Metadata?.FirstOrDefault() is { Key: { } key } station
            ? (key, PlexMapper.CleanTitle(station.Title) ?? "Radio")
            : null;
    }

    /// <summary>
    /// An artist's full metadata (moods included) together with its radio station, from one request.
    /// </summary>
    public async Task<(Artist Artist, (string Key, string Title)? Station)?> GetArtistWithStationAsync(string artistRatingKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{artistRatingKey}", [new("includeStations", "1")], ct, MediumTtl).ConfigureAwait(false);
        if (c.Metadata?.FirstOrDefault() is not { } m) return null;
        (string, string)? station = m.Stations?.Metadata?.FirstOrDefault() is { Key: { } key } s ? (key, PlexMapper.CleanTitle(s.Title) ?? "Radio") : null;
        return (PlexMapper.ToArtist(m), station);
    }

    /// <summary>
    /// A play queue a Plex app made on the server for this player to play (remote control): its tracks
    /// and which one is selected. <paramref name="containerKey"/> is "/playQueues/{id}", perhaps with
    /// a query. Only read: the queue is the app's own.
    /// </summary>
    public async Task<(IReadOnlyList<Track> Tracks, int SelectedIndex)> GetPlayQueueAsync(string containerKey, CancellationToken ct = default)
    {
        var q = containerKey.IndexOf('?');
        var path = q < 0 ? containerKey : containerKey[..q];
        var query = new List<KeyValuePair<string, string?>>();
        if (q >= 0)
            foreach (var pair in containerKey[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0) query.Add(new(Uri.UnescapeDataString(pair[..eq]), Uri.UnescapeDataString(pair[(eq + 1)..])));
            }
        if (!query.Any(p => p.Key == "window")) query.Add(new("window", "500"));
        var c = await GetAsync(path, query, ct, null).ConfigureAwait(false);
        var items = c.Metadata ?? [];
        var tracks = new List<Track>();
        var selected = 0;
        foreach (var m in items)
        {
            if (PlexMapper.ToMediaItem(m) is not Track t) continue;
            if (c.PlayQueueSelectedItemID is { } sel && m.PlayQueueItemID == sel.ToString(CultureInfo.InvariantCulture)) selected = tracks.Count;
            tracks.Add(t);
        }
        return (tracks, selected);
    }

    /// <summary>
    /// A batch of tracks from a radio station (an artist mix or a library station). Plex serves a
    /// station through a play queue a few tracks at a time; each call returns a fresh batch.
    /// </summary>
    public async Task<IReadOnlyList<Track>> GetStationTracksAsync(string stationKey, CancellationToken ct = default)
    {
        var uri = $"server://{Server.MachineIdentifier}/{LibraryIdentifier}{stationKey}";
        var request = Build("/playQueues", [new("type", "audio"), new("uri", uri), new("repeat", "0"), new("own", "1")]);
        var response = await _http.SendJsonAsync<PmsResponse>(HttpMethod.Post, request, Token, ct).ConfigureAwait(false);
        return (response.MediaContainer?.Metadata ?? []).Select(PlexMapper.ToMediaItem).OfType<Track>().ToList();
    }

    // ---------------------------------------------------------------- stations

    /// <summary>
    /// Plex's stations for a music library: the stations themselves (Library Radio, Deep Cuts Radio,
    /// Time Travel Radio, Random Album Radio) and the station directories (Genre, Style, Mood and
    /// Decade Radio). Empty when the server offers none (e.g. without Plex Pass).
    /// </summary>
    public async Task<(IReadOnlyList<Station> Stations, IReadOnlyList<StationDirectory> Directories)> GetStationsAsync(string sectionKey, CancellationToken ct = default)
    {
        var q = new List<KeyValuePair<string, string?>>
        {
            new("count", "12"), new("includeStations", "1"), new("includeStationDirectories", "1"), new("excludeFields", "summary"),
        };
        q.AddRange(Page(0, 60));
        var c = await GetAsync($"/hubs/sections/{sectionKey}", q, ct, MediumTtl).ConfigureAwait(false);
        var hub = (c.Hub ?? []).FirstOrDefault(h => h.Type == "station" || h.HubIdentifier?.StartsWith("music.stations", StringComparison.Ordinal) == true);
        if (hub is null) return ([], []);
        var stations = (hub.Metadata ?? []).Select(m => StationFrom(m.Key, m.Guid, m.Title)).OfType<Station>().ToList();
        var directories = (hub.Metadata ?? []).Where(m => m.Type == "directory").Select(m => (m.Key, m.Title))
            .Concat((hub.Directory ?? []).Select(d => (d.Key, d.Title)))
            .Select(d => DirectoryFrom(d.Key, d.Title))
            .OfType<StationDirectory>()
            .DistinctBy(d => d.Key)
            .ToList();
        return (stations, directories);
    }

    /// <summary>The stations in one of Plex's station directories (e.g. every genre's radio).</summary>
    public async Task<IReadOnlyList<Station>> GetStationDirectoryAsync(string directoryKey, CancellationToken ct = default)
    {
        var c = await GetAsync(directoryKey, null, ct, MediumTtl).ConfigureAwait(false);
        return (c.Metadata ?? []).Select(m => StationFrom(m.Key, m.Guid, m.Title)).OfType<Station>().ToList();
    }

    /// <summary>A station from Plex's key and guid ("tv.plex://station/library", ".../genre/310327").</summary>
    public static Station? StationFrom(string? key, string? guid, string? title)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(guid) || !guid.StartsWith("tv.plex://station/", StringComparison.Ordinal)) return null;
        var path = guid["tv.plex://station/".Length..].Split('/');
        StationKind? kind = path[0] switch
        {
            "library" => StationKind.Library,
            "deep-cuts" => StationKind.DeepCuts,
            "history" => StationKind.TimeTravel,
            "album" => StationKind.RandomAlbum,
            "genre" => StationKind.Genre,
            "style" => StationKind.Style,
            "mood" => StationKind.Mood,
            "decade" => StationKind.Decade,
            _ => null,
        };
        if (kind is null) return null;
        return new Station
        {
            RatingKey = "station:" + guid,
            Title = PlexMapper.CleanTitle(title) ?? "Radio",
            StationKind = kind.Value,
            Key = key,
            Tag = path.Length > 1 ? path[1] : null,
        };
    }

    private static StationDirectory? DirectoryFrom(string? key, string? title)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(title)) return null;
        StationKind? kind = key.TrimEnd('/').Split('/')[^1] switch
        {
            "genre" => StationKind.Genre,
            "style" => StationKind.Style,
            "mood" => StationKind.Mood,
            "decade" => StationKind.Decade,
            _ => null,
        };
        return kind is null ? null : new StationDirectory(title, kind.Value, key);
    }

    // ---------------------------------------------------------------- artwork

    private static string ArtworkPath(ArtworkKind kind) => kind == ArtworkKind.Poster ? "posters" : "arts";

    /// <summary>The images Plex offers for an item's photo or backdrop (from its agents, or uploaded).</summary>
    public async Task<IReadOnlyList<ArtworkOption>> GetArtworkOptionsAsync(string ratingKey, ArtworkKind kind, CancellationToken ct = default)
    {
        var c = await GetAsync($"/library/metadata/{ratingKey}/{ArtworkPath(kind)}", null, ct).ConfigureAwait(false);
        return (c.Metadata ?? [])
            .Where(m => !string.IsNullOrEmpty(m.RatingKey))
            .Select(m => new ArtworkOption(m.RatingKey!, m.Thumb, m.Selected == true, m.Provider))
            .ToList();
    }

    /// <summary>Uses one of the offered images as the item's photo or backdrop.</summary>
    public async Task SelectArtworkAsync(string ratingKey, ArtworkKind kind, ArtworkOption option, CancellationToken ct = default)
    {
        var endpoint = kind == ArtworkKind.Poster ? "poster" : "art";
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, Build($"/library/metadata/{ratingKey}/{endpoint}", [new("url", option.Key)]), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>Uploads an image and makes it the item's photo or backdrop.</summary>
    public async Task UploadArtworkAsync(string ratingKey, ArtworkKind kind, byte[] image, CancellationToken ct = default)
    {
        using var content = new ByteArrayContent(image);
        await _http.SendAndForgetBodyAsync(HttpMethod.Post, Build($"/library/metadata/{ratingKey}/{ArtworkPath(kind)}"), Token, ct,
            TimeSpan.FromSeconds(60), content).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>
    /// Gives a playlist this picture as its cover: uploads it, then makes sure Plex has it chosen
    /// (a playlist shows an automatic collage of its songs' covers until one is).
    /// </summary>
    public async Task SetPlaylistCoverAsync(string playlistKey, byte[] image, CancellationToken ct = default)
    {
        await UploadArtworkAsync(playlistKey, ArtworkKind.Poster, image, ct).ConfigureAwait(false);
        var options = await GetArtworkOptionsAsync(playlistKey, ArtworkKind.Poster, ct).ConfigureAwait(false);
        var uploads = options.Where(o => o.Key.StartsWith("upload://", StringComparison.Ordinal)).ToList();
        if (uploads.Count == 0 || uploads.Any(o => o.Selected)) return;
        await SelectArtworkAsync(playlistKey, ArtworkKind.Poster, uploads[^1], ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- playlists: create, edit, delete

    /// <summary>The address Plex uses for library items when adding them to playlists and play queues.</summary>
    public string LibraryItemsUri(IEnumerable<string> ratingKeys) =>
        $"server://{Server.MachineIdentifier}/{LibraryIdentifier}/library/metadata/{string.Join(',', ratingKeys)}";

    /// <summary>Creates a (normal) playlist holding the given items (tracks, albums or artists).</summary>
    public async Task<Playlist?> CreatePlaylistAsync(string title, IReadOnlyCollection<string> ratingKeys, CancellationToken ct = default)
    {
        if (ratingKeys.Count == 0) throw new ArgumentException("A new playlist needs at least one item.", nameof(ratingKeys));
        var uri = Build("/playlists", [new("type", "audio"), new("title", title), new("smart", "0"), new("uri", LibraryItemsUri(ratingKeys))]);
        var response = await _http.SendJsonAsync<PmsResponse>(HttpMethod.Post, uri, Token, ct).ConfigureAwait(false);
        InvalidateCache();
        return response.MediaContainer?.Metadata?.Select(PlexMapper.ToPlaylist).FirstOrDefault();
    }

    /// <summary>Adds items (tracks, albums or artists) to the end of a playlist.</summary>
    public async Task AddToPlaylistAsync(string playlistKey, IReadOnlyCollection<string> ratingKeys, CancellationToken ct = default)
    {
        if (ratingKeys.Count == 0) return;
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, Build($"/playlists/{playlistKey}/items", [new("uri", LibraryItemsUri(ratingKeys))]), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>Renames a playlist and/or changes its description.</summary>
    public async Task EditPlaylistAsync(string playlistKey, string title, string? summary, CancellationToken ct = default)
    {
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, Build($"/playlists/{playlistKey}", [new("title", title), new("summary", summary ?? string.Empty)]), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    // ---------------------------------------------------------------- smart playlists

    /// <summary>A smart playlist's rules and the library they draw from, or null when it has none.</summary>
    public async Task<(string SectionKey, Playlists.SmartFilter Filter)?> GetSmartFilterAsync(string playlistKey, CancellationToken ct = default)
    {
        var c = await GetAsync($"/playlists/{playlistKey}", null, ct, null).ConfigureAwait(false);
        if (c.Metadata?.FirstOrDefault()?.Content is not { } content) return null;
        var at = content.IndexOf("/directory/", StringComparison.Ordinal);
        if (at < 0) return null;
        var path = Uri.UnescapeDataString(content[(at + "/directory/".Length)..]);
        var match = System.Text.RegularExpressions.Regex.Match(path, @"^/library/sections/(\d+)/all\?(.*)$");
        return match.Success ? (match.Groups[1].Value, Playlists.SmartFilter.Parse(match.Groups[2].Value)) : null;
    }

    /// <summary>How many songs the rules match now (read only: a preview while editing).</summary>
    public async Task<int> CountSmartAsync(string sectionKey, Playlists.SmartFilter filter, CancellationToken ct = default)
    {
        var count = new Playlists.SmartFilter { MatchAll = filter.MatchAll };
        count.Rules.AddRange(filter.Rules);
        var uri = new Uri(BaseUri, $"library/sections/{sectionKey}/all?{count.ToQuery()}&X-Plex-Container-Start=0&X-Plex-Container-Size=0");
        var response = await _http.SendJsonAsync<PmsResponse>(HttpMethod.Get, uri, Token, ct).ConfigureAwait(false);
        var total = response.MediaContainer?.TotalSize ?? response.MediaContainer?.Size ?? 0;
        return filter.Limit is > 0 ? Math.Min(total, filter.Limit.Value) : total;
    }

    private string SmartUri(string sectionKey, Playlists.SmartFilter filter) =>
        $"server://{Server.MachineIdentifier}/{LibraryIdentifier}/library/sections/{sectionKey}/all?{filter.ToQuery()}";

    /// <summary>Creates a smart playlist: Plex fills it from the rules, and keeps it up to date.</summary>
    public async Task<Playlist?> CreateSmartPlaylistAsync(string title, string sectionKey, Playlists.SmartFilter filter, CancellationToken ct = default)
    {
        var uri = Build("/playlists", [new("type", "audio"), new("title", title), new("smart", "1"), new("uri", SmartUri(sectionKey, filter))]);
        var response = await _http.SendJsonAsync<PmsResponse>(HttpMethod.Post, uri, Token, ct).ConfigureAwait(false);
        InvalidateCache();
        return response.MediaContainer?.Metadata?.Select(PlexMapper.ToPlaylist).FirstOrDefault();
    }

    /// <summary>Gives a smart playlist new rules.</summary>
    public async Task UpdateSmartPlaylistAsync(string playlistKey, string sectionKey, Playlists.SmartFilter filter, CancellationToken ct = default)
    {
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, Build($"/playlists/{playlistKey}/items", [new("uri", SmartUri(sectionKey, filter))]), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>Deletes a playlist from the server (its music stays in the library).</summary>
    public async Task DeletePlaylistAsync(string playlistKey, CancellationToken ct = default)
    {
        await _http.SendAndForgetBodyAsync(HttpMethod.Delete, Build($"/playlists/{playlistKey}"), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>
    /// Moves a track within a (non-smart) playlist so it follows <paramref name="afterPlaylistItemId"/>,
    /// or to the top when that is null. Ids are the playlist's own item ids, not track rating keys.
    /// </summary>
    public async Task MovePlaylistItemAsync(string playlistKey, string playlistItemId, string? afterPlaylistItemId, CancellationToken ct = default)
    {
        var uri = Build($"/playlists/{playlistKey}/items/{playlistItemId}/move",
            afterPlaylistItemId is null ? null : [new("after", afterPlaylistItemId)]);
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, uri, Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>Removes one entry (by its playlist item id) from a (non-smart) playlist.</summary>
    public async Task RemovePlaylistItemAsync(string playlistKey, string playlistItemId, CancellationToken ct = default)
    {
        await _http.SendAndForgetBodyAsync(HttpMethod.Delete, Build($"/playlists/{playlistKey}/items/{playlistItemId}"), Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>
    /// Moves an item within a (non-smart) collection so it follows <paramref name="afterRatingKey"/>, or
    /// to the top when that is null. The collection is switched to custom ordering first, which is what
    /// Plex requires for a hand-arranged order to take effect.
    /// </summary>
    public async Task MoveCollectionItemAsync(string collectionKey, string itemRatingKey, string? afterRatingKey, CancellationToken ct = default)
    {
        var sort = Build($"/library/metadata/{collectionKey}/prefs", [new("collectionSort", "2")]);
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, sort, Token, ct).ConfigureAwait(false);
        var uri = Build($"/library/collections/{collectionKey}/items/{itemRatingKey}/move",
            afterRatingKey is null ? null : [new("after", afterRatingKey)]);
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, uri, Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>Sets the user rating (0-10, where 10 = five stars; -1 clears).</summary>
    public async Task RateAsync(string ratingKey, double rating, CancellationToken ct = default)
    {
        var uri = Build("/:/rate", [new("key", ratingKey), new("identifier", LibraryIdentifier), new("rating", rating.ToString(CultureInfo.InvariantCulture))]);
        await _http.SendAndForgetBodyAsync(HttpMethod.Put, uri, Token, ct).ConfigureAwait(false);
        InvalidateCache();
    }

    /// <summary>
    /// Reports playback progress (Playable.updateTimeline in the Plex API reference). The server uses
    /// these reports for Now Playing, resume points, play counts and listening history.
    /// </summary>
    public async Task ReportTimelineAsync(Track track, TimelineState state, long positionMs, long durationMs, string sessionId, CancellationToken ct = default)
    {
        // Plex rejects reports whose position lies beyond the duration (e.g. pausing on the last frame).
        if (durationMs > 0) positionMs = Math.Min(positionMs, durationMs);
        var q = new List<KeyValuePair<string, string?>>
        {
            new("ratingKey", track.RatingKey),
            new("key", track.Key ?? $"/library/metadata/{track.RatingKey}"),
            new("identifier", LibraryIdentifier),
            new("state", state.ToString().ToLowerInvariant()),
            new("time", Math.Max(0, positionMs).ToString(CultureInfo.InvariantCulture)),
            new("duration", Math.Max(0, durationMs).ToString(CultureInfo.InvariantCulture)),
            new("playbackTime", Math.Max(0, positionMs).ToString(CultureInfo.InvariantCulture)),
            new("hasMDE", "1"),
            new("context", "library"),
            new("X-Plex-Session-Identifier", sessionId),
        };
        await _http.SendAndForgetBodyAsync(HttpMethod.Get, Build("/:/timeline", q), Token, ct, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    }

    /// <summary>Marks an item as played (Playable.markPlayed / scrobble).</summary>
    public async Task ScrobbleAsync(string ratingKey, CancellationToken ct = default)
    {
        await _http.SendAndForgetBodyAsync(HttpMethod.Get, Build("/:/scrobble", [new("key", ratingKey), new("identifier", LibraryIdentifier)]), Token, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- URLs for media and images

    /// <summary>
    /// A server-resized artwork URL (PlexServer.transcodeImage in the Plex API reference).
    /// Requesting the display size avoids downloading full-resolution originals.
    /// </summary>
    /// <summary>Plex's UltraBlur palette for any artwork (the same colours Plex uses behind its pages).</summary>
    public async Task<ArtworkPalette?> GetUltraBlurColorsAsync(string artworkPath, CancellationToken ct = default)
    {
        var c = await GetAsync("/services/ultrablur/colors", [new("url", artworkPath)], ct, MediumTtl).ConfigureAwait(false);
        return c.UltraBlurColors is [var u, ..] ? ArtworkPalette.Parse(u.TopLeft, u.TopRight, u.BottomRight, u.BottomLeft) : null;
    }

    /// <summary>
    /// The server-rendered UltraBlur background (a soft four-corner gradient with film grain) for a
    /// palette, as Plex draws behind its own artist and album pages.
    /// </summary>
    public Uri BuildUltraBlurUri(ArtworkPalette palette, int width, int height)
    {
        var sb = new StringBuilder("services/ultrablur/image?");
        sb.Append("topLeft=").Append(ArtworkPalette.Hex(palette.TopLeft));
        sb.Append("&topRight=").Append(ArtworkPalette.Hex(palette.TopRight));
        sb.Append("&bottomRight=").Append(ArtworkPalette.Hex(palette.BottomRight));
        sb.Append("&bottomLeft=").Append(ArtworkPalette.Hex(palette.BottomLeft));
        sb.Append("&width=").Append(width.ToString(CultureInfo.InvariantCulture));
        sb.Append("&height=").Append(height.ToString(CultureInfo.InvariantCulture));
        sb.Append("&noise=0"); // a clean gradient, without Plex's film grain
        if (!string.IsNullOrEmpty(Token)) sb.Append("&X-Plex-Token=").Append(Uri.EscapeDataString(Token));
        return new Uri(BaseUri, sb.ToString());
    }

    public Uri? BuildArtworkUri(string? path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Both library paths and external (http) artwork go through the server's image transcoder.
        var sb = new StringBuilder("photo/:/transcode?");
        sb.Append("width=").Append(width.ToString(CultureInfo.InvariantCulture));
        sb.Append("&height=").Append(height.ToString(CultureInfo.InvariantCulture));
        sb.Append("&minSize=1&upscale=1&format=jpeg");
        sb.Append("&url=").Append(Uri.EscapeDataString(path));
        if (!string.IsNullOrEmpty(Token)) sb.Append("&X-Plex-Token=").Append(Uri.EscapeDataString(Token));
        return new Uri(BaseUri, sb.ToString());
    }

    /// <summary>Original file stream for direct play.</summary>
    public Uri? BuildDirectPlayUri(Track track)
    {
        var partKey = track.Media?.PartKey;
        if (string.IsNullOrEmpty(partKey)) return null;
        var sep = partKey.Contains('?') ? '&' : '?';
        return new Uri(BaseUri, partKey.TrimStart('/') + sep + _http.Identity.ToQueryString(Token));
    }

    /// <summary>
    /// Plex universal transcoder URL producing MP3 over plain HTTP, used when Windows cannot decode
    /// the original file (or the user asks for reduced bandwidth).
    /// </summary>
    public Uri BuildTranscodeUri(Track track, int bitrateKbps, string sessionId, long offsetMs = 0)
    {
        var q = new List<KeyValuePair<string, string?>>
        {
            new("path", track.Key ?? $"/library/metadata/{track.RatingKey}"),
            new("mediaIndex", "0"),
            new("partIndex", "0"),
            new("protocol", "http"),
            new("directPlay", "0"),
            new("directStream", "0"),
            new("directStreamAudio", "0"),
            new("hasMDE", "1"),
            new("musicBitrate", bitrateKbps.ToString(CultureInfo.InvariantCulture)),
            new("maxAudioBitrate", bitrateKbps.ToString(CultureInfo.InvariantCulture)),
            new("session", sessionId),
            new("X-Plex-Session-Identifier", sessionId),
            new("X-Plex-Client-Profile-Extra",
                "add-transcode-target(type=musicProfile&context=streaming&protocol=http&container=mp3&audioCodec=mp3)"),
        };
        if (offsetMs > 0) q.Add(new("offset", (offsetMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)));
        var uri = Build("/music/:/transcode/universal/start.mp3", q);
        return new Uri(uri.AbsoluteUri + "&" + _http.Identity.ToQueryString(Token));
    }
}
