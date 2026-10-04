using System.Collections.Concurrent;
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
    IReadOnlyDictionary<string, double> Affinity);

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
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _given = [];
    private Track? _lead;

    internal RadioSession(Func<CancellationToken, Task<RadioPlan>> plan, SkipStore skips, Dislikes dislikes, Track? lead = null)
    {
        _plan = plan;
        _skips = skips;
        _dislikes = dislikes;
        _lead = lead;
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
        };
        // Compilation placeholders ("Various Artists") are no one's music to follow.
        var picked = RadioEngine.Pick(plan.Pool.Where(t => disliked.Allows(t) && !Core.Mixes.ListeningProfile.IsPlaceholderArtist(t.DisplayArtist)),
            context, Math.Max(1, count - result.Count));
        foreach (var t in picked) Remember(t.RatingKey);
        result.AddRange(picked);
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
    private readonly ILogger<LocalRadio> _log;
    private readonly ConcurrentDictionary<string, Task<Dictionary<string, double>>> _affinity = new();

    public LocalRadio(PlexSession session, Dislikes dislikes, SkipStore skips, PlaylistArtistIndex playlists, ILogger<LocalRadio> log)
    {
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
            case StationKind.DeepCuts:
            {
                // Songs rarely played, from the artists played most.
                var top = await client.GetArtistsAsync(sk, 0, 30, "viewCount:desc", ct, new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false);
                pool = top.Items.Count == 0 ? [] : await TracksAsync(client, sk, "artist.id", top.Items.Select(a => a.RatingKey), PoolSize, ct,
                    new KeyValuePair<string, string?>("viewCount<<", "1")).ConfigureAwait(false);
                if (pool.Count == 0) pool = await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false);
                break;
            }
            default:
                pool = await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false);
                break;
        }
        return new RadioPlan(pool, EmptyTags, EmptyTags, EmptyAffinity);
    }, _skips, _dislikes);

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
