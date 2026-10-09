using System.Collections.Concurrent;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Radio;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>What a radio draws from this time: the songs to choose among, and what they should sound like.</summary>
public sealed record RadioPlan(
    IReadOnlyList<Track> Pool,
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> SeedArtists,
    IReadOnlyDictionary<string, double> Affinity,
    bool LeanToHits = false);

/// <summary>
/// One radio as it plays: each call brings the next batch (never repeating what it already gave, and
/// leaving out what the listener skips or dislikes). The first batch can lead with a chosen song.
/// </summary>
public sealed class RadioSession
{
    private const int MaxRemembered = 300;

    private readonly Func<CancellationToken, Task<RadioPlan>> _plan;
    private readonly SkipStore _skips;
    private readonly Dislikes _dislikes;
    private readonly HeadlinerHistory? _featured;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _given = [];
    private Track? _lead;

    /// <param name="featured">
    /// For a station: the artists it has played lately, kept between runs of the app, so it doesn't open on the same artists each
    /// time (its songs wait while others are likelier). Null for a radio from a chosen artist, whose own music leads.
    /// </param>
    internal RadioSession(Func<CancellationToken, Task<RadioPlan>> plan, SkipStore skips, Dislikes dislikes, Track? lead = null, HeadlinerHistory? featured = null)
    {
        _plan = plan;
        _skips = skips;
        _dislikes = dislikes;
        _lead = lead;
        _featured = featured;
    }

    public async Task<IReadOnlyList<Track>> NextAsync(int count, CancellationToken ct = default)
    {
        var plan = await _plan(ct).ConfigureAwait(false);
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        var result = new List<Track>();
        if (_lead is { } lead)
        {
            _lead = null;
            result.Add(lead);
            Remember(lead.RatingKey);
        }
        var context = new RadioContext
        {
            Tags = plan.Tags,
            SeedArtists = plan.SeedArtists,
            Affinity = plan.Affinity,
            Skip = t => _skips.Penalty(t.RatingKey, t.ArtistRatingKey),
            Exclude = _given.ToHashSet(),
            LeanToHits = plan.LeanToHits,
            Rested = _featured?.Rested(DateTimeOffset.Now) ?? new HashSet<string>(),
        };
        // Compilation placeholders ("Various Artists") are no one's music to follow.
        var picked = RadioEngine.Pick(plan.Pool.Where(t => disliked.Eligible(t) && !Core.Mixes.ListeningProfile.IsPlaceholderArtist(t.DisplayArtist)),
            context, Math.Max(1, count - result.Count));
        foreach (var t in picked) Remember(t.RatingKey);
        result.AddRange(picked);
        _featured?.Record(picked.Select(t => t.ArtistRatingKey).OfType<string>().Distinct(), DateTimeOffset.Now);
        return result;
    }

    private void Remember(string key)
    {
        if (!_given.Add(key)) return;
        _order.Enqueue(key);
        while (_order.Count > MaxRemembered) _given.Remove(_order.Dequeue());
    }
}

/// <summary>
/// Headliners as it plays (see <see cref="HeadlinerProgram"/>): each call brings the next stretch of the programme, a
/// mix of artist blocks (songs of one artist back to back), runs of new and trending songs and genre blocks. It never
/// repeats a song it already gave nor an artist it featured lately (remembered between runs of the app, see
/// <see cref="HeadlinerHistory"/>), and leaves out what the listener skips or dislikes. <c>Groups</c> says which artist
/// block each song belongs to (1, 2, 3...; 0 for a run that features no artist).
/// </summary>
public sealed class BlockRadio
{
    private const int MaxRemembered = 300, RecentArtists = 10;

    private readonly Func<CancellationToken, Task<HeadlinerPools>> _pools;
    private readonly SkipStore _skips;
    private readonly Dislikes _dislikes;
    private readonly HeadlinerHistory _history;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _given = [];
    private readonly Queue<string> _artistOrder = new();
    private readonly HashSet<string> _recent = [];
    // Which turn of the programme is next: it begins somewhere different each time.
    private int _round = Random.Shared.Next(3);

    internal BlockRadio(Func<CancellationToken, Task<HeadlinerPools>> pools, SkipStore skips, Dislikes dislikes, HeadlinerHistory history)
    {
        _pools = pools;
        _skips = skips;
        _dislikes = dislikes;
        _history = history;
    }

    public async Task<(IReadOnlyList<Track> Tracks, IReadOnlyList<int> Groups)> NextAsync(IReadOnlySet<string> off, CancellationToken ct = default)
    {
        var pools = await _pools(ct).ConfigureAwait(false);
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        // Songs the listener took off this station (see StationFeedbackStore) are never played on it.
        IReadOnlyList<Track> Allowed(IReadOnlyList<Track> tracks) => tracks.Where(t => disliked.Eligible(t) && !off.Contains(t.RatingKey)).ToList();
        var allowed = new HeadlinerPools(Allowed(pools.Played), Allowed(pools.Fresh), Allowed(pools.Trending));
        var now = DateTimeOffset.Now;
        var rested = _history.Rested(now).Concat(_recent).ToHashSet(StringComparer.Ordinal);
        var picks = Build(allowed, rested);
        // A narrow library has featured everyone it has: start the rounds again.
        if (picks.Count == 0 && rested.Count > 0)
        {
            _artistOrder.Clear();
            _recent.Clear();
            picks = Build(allowed, new HashSet<string>());
        }
        _round++;
        foreach (var pick in picks)
        {
            if (_given.Add(pick.Track.RatingKey)) _order.Enqueue(pick.Track.RatingKey);
            while (_order.Count > MaxRemembered) _given.Remove(_order.Dequeue());
            if (pick.Group > 0 && pick.Track.ArtistRatingKey is { } artist && _recent.Add(artist)) _artistOrder.Enqueue(artist);
            while (_artistOrder.Count > RecentArtists) _recent.Remove(_artistOrder.Dequeue());
        }
        _history.Record(picks.Where(p => p.Group > 0 && p.Track.ArtistRatingKey is not null).Select(p => p.Track.ArtistRatingKey!).Distinct(), now);
        return (picks.Select(p => p.Track).ToList(), picks.Select(p => p.Group).ToList());
    }

    private IReadOnlyList<BlockPick> Build(HeadlinerPools pools, IReadOnlySet<string> rested) =>
        HeadlinerProgram.Build(pools, _round, Random.Shared, t => _skips.Penalty(t.RatingKey, t.ArtistRatingKey), _given.ToHashSet(), rested);
}

/// <summary>
/// Meloday's own radio (see <see cref="RadioEngine"/>), for stations, artist mixes and "Radio" on a
/// song, album or artist when Plex has none to offer (no Plex Pass, or its sonic analysis hasn't
/// run). Candidates come from ordinary library queries, so it needs nothing from Plex Pass: the artist's
/// own music, artists Plex lists as similar, artists sharing its playlists, and artists sharing its
/// styles, moods and genres.
/// </summary>
public sealed class LocalRadio
{
    private const int Batch = 20;
    private const int PoolSize = 200;

    private readonly PlexSession _session;
    private readonly Dislikes _dislikes;
    private readonly SkipStore _skips;
    private readonly PlaylistArtistIndex _playlists;
    private readonly HeadlinerHistory _history;
    private readonly TrendingService _trending;
    private readonly ILogger<LocalRadio> _log;
    private readonly ConcurrentDictionary<string, Task<Dictionary<string, double>>> _affinity = new();
    private readonly ConcurrentDictionary<StationKind, HeadlinerHistory> _featured = new();

    /// <summary>The artists a kind of station has played lately, kept in a file of its own so a new run of the app doesn't open on the same ones.</summary>
    private HeadlinerHistory FeaturedBy(StationKind kind) => _featured.GetOrAdd(kind, k =>
        new HeadlinerHistory(Path.Combine(Core.AppPaths.Root, "radio", $"featured-{k}.json".ToLowerInvariant()), TimeSpan.FromHours(18)));

    public LocalRadio(PlexSession session, Dislikes dislikes, SkipStore skips, PlaylistArtistIndex playlists, HeadlinerHistory history, TrendingService trending, ILogger<LocalRadio> log)
    {
        _history = history;
        _trending = trending;
        _session = session;
        _dislikes = dislikes;
        _skips = skips;
        _playlists = playlists;
        _log = log;
    }

    public static int BatchSize => Batch;

    /// <summary>Forgets what was worked out (the library changed).</summary>
    public void Clear() => _affinity.Clear();

    // ------------------------------------------------------------------ radios

    /// <summary>An artist's radio: their music with music from artists near them.</summary>
    public RadioSession ForArtist(Artist seed, Track? lead = null)
    {
        Artist? full = null;
        return new RadioSession(async ct =>
        {
            if (_session.Client is not { } client || _session.Section is not { } section) return Empty;
            full ??= (await client.GetItemsAsync([seed.RatingKey], ct).ConfigureAwait(false)).OfType<Artist>().FirstOrDefault() ?? seed;
            var affinity = await AffinityAsync(full, ct).ConfigureAwait(false);
            var near = affinity.OrderByDescending(a => a.Value).Take(30).Select(a => a.Key).ToList();
            var own = client.GetArtistTracksAsync(seed.RatingKey, ct);
            var others = near.Count == 0
                ? Task.FromResult<IReadOnlyList<Track>>([])
                : TracksAsync(client, section.Key, "artist.id", near, PoolSize, ct);
            await Task.WhenAll(own, others).ConfigureAwait(false);
            var tags = full.Genres.Concat(full.Styles).Concat(full.Moods).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToHashSet();
            return new RadioPlan([.. own.Result, .. others.Result], tags, new HashSet<string> { seed.RatingKey }, affinity);
        }, _skips, _dislikes, lead);
    }

    /// <summary>A station made from the library: a tag's music, the listener's favourites, or what they've left behind.</summary>
    public RadioSession ForStation(Station station) => new(async ct =>
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return Empty;
        var sk = section.Key;
        IReadOnlyList<Track> pool;
        switch (station.StationKind)
        {
            case StationKind.Genre when station.Tag is { } id:
                pool = await RandomTracksAsync(client, sk, new("genre", id), ct).ConfigureAwait(false);
                break;
            case StationKind.Style or StationKind.Mood when station.Tag is { } id:
            {
                var field = station.StationKind == StationKind.Style ? "style" : "mood";
                var artists = await client.GetArtistsAsync(sk, 0, 40, "random", ct, new KeyValuePair<string, string?>(field, id)).ConfigureAwait(false);
                pool = artists.Items.Count == 0 ? [] : await TracksAsync(client, sk, "artist.id", artists.Items.Select(a => a.RatingKey), PoolSize, ct).ConfigureAwait(false);
                break;
            }
            case StationKind.Decade when station.Tag is { } id:
            {
                var albums = await client.GetAlbumsAsync(sk, 0, 40, "random", ct, new KeyValuePair<string, string?>("decade", id)).ConfigureAwait(false);
                pool = albums.Items.Count == 0 ? [] : await TracksAsync(client, sk, "album.id", albums.Items.Select(a => a.RatingKey), PoolSize, ct).ConfigureAwait(false);
                break;
            }
            case StationKind.Hits:
            {
                // Albums of the '80s, '90s and 2000s, the songs most widely known first (see RadioContext.LeanToHits).
                var albums = await Task.WhenAll(StationVibes.HitsDecades.Select(decade =>
                    client.GetAlbumsAsync(sk, 0, 25, "random", ct, new KeyValuePair<string, string?>("decade", decade)))).ConfigureAwait(false);
                var ids = albums.SelectMany(a => a.Items).Select(a => a.RatingKey).Distinct().ToList();
                pool = ids.Count == 0 ? [] : await TracksAsync(client, sk, "album.id", ids, 300, ct).ConfigureAwait(false);
                return new RadioPlan(pool, EmptyTags, EmptyTags, EmptyAffinity, LeanToHits: true);
            }
            case StationKind.Club or StationKind.Chill:
                pool = await VibeTracksAsync(client, sk, station.StationKind, ct).ConfigureAwait(false);
                break;
            case StationKind.DeepCuts:
            {
                // Songs rarely played, from the artists played most.
                // Of the ninety most played artists, thirty chosen by chance: not the same thirty each time.
                var top = await client.GetArtistsAsync(sk, 0, 90, "viewCount:desc", ct, new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false);
                var chosen = top.Items.OrderBy(_ => Random.Shared.Next()).Take(30).ToList();
                pool = chosen.Count == 0 ? [] : await TracksAsync(client, sk, "artist.id", chosen.Select(a => a.RatingKey), PoolSize, ct,
                    new KeyValuePair<string, string?>("viewCount<<", "1")).ConfigureAwait(false);
                if (pool.Count == 0) pool = await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false);
                break;
            }
            default:
                pool = await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false);
                break;
        }
        return new RadioPlan(pool, EmptyTags, EmptyTags, EmptyAffinity);
    }, _skips, _dislikes, featured: FeaturedBy(station.StationKind));

    /// <summary>
    /// Headliners (see <see cref="HeadlinerProgram"/>): what it programmes from is the songs of artists the listener plays (a
    /// fresh sample each time, of their hundred and twenty most played and the thirty played most lately), the songs of albums
    /// released in the last year and a half, and the songs trending on Last.fm that the library has (when that is switched on).
    /// </summary>
    public BlockRadio ForBlocks() => new(async ct =>
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return new HeadlinerPools([], [], []);
        var sk = section.Key;
        var played = PlayedPoolAsync(client, sk, ct);
        var fresh = FreshPoolAsync(client, sk, ct);
        var trending = TrendingPoolAsync();
        await Task.WhenAll(played, fresh, trending).ConfigureAwait(false);
        var pool = played.Result;
        // A library with little played yet is filled out from the rest, where the songs known far and wide still lead.
        if (pool.Count < 40) pool = [.. pool, .. await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false)];
        return new HeadlinerPools(pool, fresh.Result, trending.Result);
    }, _skips, _dislikes, _history);

    private async Task<IReadOnlyList<Track>> PlayedPoolAsync(PlexServerClient client, string sk, CancellationToken ct)
    {
        try
        {
            var played = new KeyValuePair<string, string?>("viewCount>>", "0");
            var top = client.GetArtistsAsync(sk, 0, 120, "viewCount:desc", ct, played);
            var lately = client.GetArtistsAsync(sk, 0, 30, "lastViewedAt:desc", ct, played);
            try
            {
                await Task.WhenAll(top, lately).ConfigureAwait(false);
            }
            catch (PlexException)
            {
                // The artists played lately are a bonus: the most played alone will do.
            }
            // A sample leaning to the most played, then a few of those played lately: a different line-up each time.
            var ranked = top.IsCompletedSuccessfully ? top.Result.Items.Select((a, i) => (a.RatingKey, Rank: i + Random.Shared.NextDouble() * 60)).OrderBy(x => x.Rank).Take(18).Select(x => x.RatingKey) : [];
            var recent = lately.IsCompletedSuccessfully ? lately.Result.Items.OrderBy(_ => Random.Shared.Next()).Take(6).Select(a => a.RatingKey) : [];
            var ids = ranked.Concat(recent).Distinct().ToList();
            if (ids.Count == 0) return [];
            return (await client.GetTracksAsync(sk, 0, 400, "viewCount:desc", ct, new KeyValuePair<string, string?>("artist.id", string.Join(',', ids))).ConfigureAwait(false)).Items;
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Headliners' played artists unavailable: {Message}", ex.Message);
            return [];
        }
    }

    /// <summary>The songs of albums released in the last year and a half, newest first among the library's albums.</summary>
    private async Task<IReadOnlyList<Track>> FreshPoolAsync(PlexServerClient client, string sk, CancellationToken ct)
    {
        try
        {
            var albums = await client.GetAlbumsAsync(sk, 0, 40, "originallyAvailableAt:desc", ct).ConfigureAwait(false);
            var cutoff = DateTimeOffset.Now.AddDays(-548);
            var recent = albums.Items.Where(a => a.ReleaseDate is { } date ? date >= cutoff : a.Year is { } year && year >= DateTime.Now.Year - 1).Take(14).Select(a => a.RatingKey).ToList();
            return recent.Count == 0 ? [] : await TracksAsync(client, sk, "album.id", recent, 160, ct).ConfigureAwait(false);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Headliners' new releases unavailable: {Message}", ex.Message);
            return [];
        }
    }

    /// <summary>The songs of Last.fm's chart that the library holds (none unless the listener has turned trending on).</summary>
    private async Task<IReadOnlyList<Track>> TrendingPoolAsync()
    {
        try
        {
            return (await _trending.GetAsync().ConfigureAwait(false))?.InLibrary ?? [];
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>
    /// The music of a genre-led station: tracks of the library's genres that belong to it (see
    /// <see cref="StationVibes"/>) and, for Chill, of the artists whose moods are quiet ones.
    /// </summary>
    private static async Task<IReadOnlyList<Track>> VibeTracksAsync(PlexServerClient client, string sk, StationKind kind, CancellationToken ct)
    {
        var genreTags = client.GetTagsAsync(sk, TagKind.Genre, PlexTypes.Track, ct);
        var moodTags = kind == StationKind.Chill ? client.GetTagsAsync(sk, TagKind.Mood, PlexTypes.Artist, ct) : Task.FromResult<IReadOnlyList<TagItem>>([]);
        await Task.WhenAll(genreTags, moodTags).ConfigureAwait(false);
        var genres = genreTags.Result.Where(t => StationVibes.IsGenreOf(kind, t.Title) && !string.IsNullOrWhiteSpace(t.FilterValue)).Select(t => t.FilterValue).Distinct().ToList();
        var moods = moodTags.Result.Where(t => StationVibes.IsChillMood(t.Title) && !string.IsNullOrWhiteSpace(t.FilterValue)).Select(t => t.FilterValue).Distinct().ToList();

        var byGenre = genres.Count == 0
            ? Task.FromResult<IReadOnlyList<Track>>([])
            : RandomTracksAsync(client, sk, new("genre", string.Join(',', genres)), ct);
        var byMood = moods.Count == 0 ? Task.FromResult<IReadOnlyList<Track>>([]) : TracksOfMoodsAsync(client, sk, moods, ct);
        await Task.WhenAll(byGenre, byMood).ConfigureAwait(false);
        return [.. byGenre.Result, .. byMood.Result];
    }

    private static async Task<IReadOnlyList<Track>> TracksOfMoodsAsync(PlexServerClient client, string sk, IReadOnlyList<string> moods, CancellationToken ct)
    {
        var artists = await client.GetArtistsAsync(sk, 0, 40, "random", ct, new KeyValuePair<string, string?>("mood", string.Join(',', moods))).ConfigureAwait(false);
        return artists.Items.Count == 0 ? [] : await TracksAsync(client, sk, "artist.id", artists.Items.Select(a => a.RatingKey), PoolSize, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A radio from a song, album or artist (null for anything else): the song first when it's a song,
    /// then music near its artist.
    /// </summary>
    public async Task<RadioSession?> ForSeedAsync(MediaItem seed, bool leadWithSeed = true, CancellationToken ct = default)
    {
        if (_session.Client is not { } client) return null;
        var artistKey = seed switch
        {
            Track t => t.ArtistRatingKey,
            Album a => a.ArtistRatingKey,
            Artist a => a.RatingKey,
            _ => null,
        };
        if (artistKey is null) return null;
        var artist = seed as Artist ?? (await client.GetItemsAsync([artistKey], ct).ConfigureAwait(false)).OfType<Artist>().FirstOrDefault();
        return artist is null ? null : ForArtist(artist, lead: leadWithSeed ? seed as Track : null);
    }

    // ------------------------------------------------------------------ who is near an artist

    /// <summary>Artists near this one and how near (0-1): similar by Plex's own list, sharing playlists, sharing tags.</summary>
    private Task<Dictionary<string, double>> AffinityAsync(Artist artist, CancellationToken ct)
    {
        if (!_affinity.TryGetValue(artist.RatingKey, out var load) || load.IsFaulted)
            _affinity[artist.RatingKey] = load = ComputeAffinityAsync(artist, ct);
        return load;
    }

    private async Task<Dictionary<string, double>> ComputeAffinityAsync(Artist artist, CancellationToken ct)
    {
        var near = new Dictionary<string, double>();
        void Add(string key, double weight)
        {
            if (key == artist.RatingKey) return;
            near[key] = Math.Max(near.GetValueOrDefault(key), weight);
        }
        if (_session.Client is not { } client || _session.Section is not { } section) return near;
        var sk = section.Key;
        try
        {
            async Task Similar()
            {
                // Plex lists similar artists by name; the ones the library holds are found by name.
                var names = artist.SimilarArtists.Take(6).ToList();
                var found = await Task.WhenAll(names.Select(async (name, i) =>
                {
                    var hits = await client.GetArtistsAsync(sk, 0, 5, "titleSort", ct, new KeyValuePair<string, string?>("title", name)).ConfigureAwait(false);
                    var hit = hits.Items.FirstOrDefault(a => string.Equals(a.Title, name, StringComparison.OrdinalIgnoreCase));
                    return (Hit: hit, Weight: 0.9 - 0.03 * i);
                })).ConfigureAwait(false);
                foreach (var (hit, weight) in found)
                    if (hit is not null) Add(hit.RatingKey, weight);
            }

            async Task Tagged(string field, TagRef? tag, double weight)
            {
                if (tag is null) return;
                var artists = await client.GetArtistsAsync(sk, 0, 25, "random", ct, new KeyValuePair<string, string?>(field, tag.Id)).ConfigureAwait(false);
                foreach (var a in artists.Items) Add(a.RatingKey, weight);
            }

            async Task CoPlay()
            {
                var shared = await _playlists.CoPlayAsync(artist.RatingKey, ct).ConfigureAwait(false);
                if (shared.Count == 0) return;
                var top = shared.Values.Max();
                foreach (var (key, count) in shared) Add(key, 0.35 + 0.5 * count / top);
            }

            await Task.WhenAll(
                Similar(),
                CoPlay(),
                Tagged("style", artist.StyleRefs.FirstOrDefault(), 0.5),
                Tagged("style", artist.StyleRefs.Skip(1).FirstOrDefault(), 0.4),
                Tagged("mood", artist.MoodRefs.FirstOrDefault(), 0.35),
                Tagged("genre", artist.GenreRefs.FirstOrDefault(), 0.25)).ConfigureAwait(false);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Artists near {Artist} unavailable: {Message}", artist.Title, ex.Message);
        }
        return near;
    }

    // ------------------------------------------------------------------ candidates

    private static readonly IReadOnlySet<string> EmptyTags = new HashSet<string>();
    private static readonly IReadOnlyDictionary<string, double> EmptyAffinity = new Dictionary<string, double>();
    private static readonly RadioPlan Empty = new([], EmptyTags, EmptyTags, EmptyAffinity);

    private static async Task<IReadOnlyList<Track>> RandomTracksAsync(PlexServerClient client, string sk, KeyValuePair<string, string?> filter, CancellationToken ct)
        => (await client.GetTracksAsync(sk, 0, PoolSize, "random", ct, filter).ConfigureAwait(false)).Items;

    private static async Task<IReadOnlyList<Track>> TracksAsync(PlexServerClient client, string sk, string field, IEnumerable<string> ids, int size, CancellationToken ct,
        params KeyValuePair<string, string?>[] more)
    {
        var filters = new[] { new KeyValuePair<string, string?>(field, string.Join(',', ids)) }.Concat(more).ToArray();
        return (await client.GetTracksAsync(sk, 0, size, "random", ct, filters).ConfigureAwait(false)).Items;
    }

    /// <summary>The library at large, leaning to what's played: some at random, some from the most played.</summary>
    private static async Task<IReadOnlyList<Track>> LibraryPoolAsync(PlexServerClient client, string sk, CancellationToken ct)
    {
        var random = client.GetTracksAsync(sk, 0, 150, "random", ct);
        var loved = client.GetTracksAsync(sk, 0, 80, "viewCount:desc", ct, new KeyValuePair<string, string?>("viewCount>>", "0"));
        await Task.WhenAll(random, loved).ConfigureAwait(false);
        return [.. random.Result.Items, .. loved.Result.Items];
    }
}
