using System.Collections.Concurrent;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Radio;
using MelodayMusic.Core.Taste;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record RadioPlan(
    IReadOnlyList<Track> Pool,
    IReadOnlySet<string> Tags,
    IReadOnlySet<string> SeedArtists,
    IReadOnlyDictionary<string, double> Affinity,
    bool LeanToHits = false);

public sealed class RadioSession
{
    private const int MaxRemembered = 300;

    private readonly Func<CancellationToken, Task<RadioPlan>> _plan;
    private readonly Func<TasteEngine> _taste;
    private readonly Dislikes _dislikes;
    private readonly HeadlinerHistory? _featured;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _given = [];
    private readonly Queue<Track> _givenTracks = new();
    private Track? _lead;
    private readonly RadioPlaybackStore? _played;
    private readonly string? _scope;
    private List<RotationPlay> _rotation = [];
    private DateTimeOffset _scheduledAt;

    internal RadioSession(Func<CancellationToken, Task<RadioPlan>> plan, Func<TasteEngine> taste, Dislikes dislikes, Track? lead = null, HeadlinerHistory? featured = null, RadioPlaybackStore? played = null, string? scope = null)
    {
        _plan = plan;
        _taste = taste;
        _dislikes = dislikes;
        _lead = lead;
        _featured = featured;
        _played = played;
        _scope = scope;
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
            Remember(lead);
        }
        var context = new RadioContext
        {
            Tags = plan.Tags,
            SeedArtists = plan.SeedArtists,
            Affinity = plan.Affinity,
            Taste = _taste(),
            Exclude = _given.ToHashSet(),
            LeanToHits = plan.LeanToHits,
            Rested = _featured?.Rested(DateTimeOffset.Now) ?? new HashSet<string>(),
        };
        var pool = MelodayText.Dedupe(plan.Pool.Where(t => disliked.Eligible(t) && !Core.Mixes.ListeningProfile.IsPlaceholderArtist(t.DisplayArtist)));
        var wanted = Math.Max(1, count - result.Count);
        var picked = RadioEngine.Pick(pool, context, _played is null ? wanted * 3 : 100,
            maxPerArtist: _played is null ? 3 : Math.Max(2, count));
        if (_played is not null && _scope is not null)
        {
            if (picked.Count == 0)
            {
                context = new RadioContext { Tags = context.Tags, SeedArtists = context.SeedArtists, Affinity = context.Affinity,
                    Taste = context.Taste, LeanToHits = context.LeanToHits, Rested = context.Rested };
                picked = RadioEngine.Pick(pool, context, 100, maxPerArtist: Math.Max(2, count));
            }
            _rotation = _rotation.Concat(_played.Get(_scope)).DistinctBy(p => (p.Key, p.At)).OrderBy(p => p.At).TakeLast(LiveRotation.Remembered).ToList();
            if (_scheduledAt < DateTimeOffset.Now) _scheduledAt = DateTimeOffset.Now;
            if (result.Count > 0)
            {
                var opening = result[0];
                _rotation.Add(new RotationPlay(opening.RatingKey, LiveRotation.ArtistOf(opening), LiveRotation.SongOf(opening), _scheduledAt));
                _scheduledAt = _scheduledAt.AddMilliseconds(opening.DurationMs > 0 ? opening.DurationMs : LiveSchedule.FallbackDurationMs);
            }
            picked = LiveRotation.Pick(picked.Select(t => new BlockPick(t, 0)), _rotation, _scheduledAt, wanted * 3).Select(p => p.Track).ToList();
            picked = Core.Mixes.ArtistVariety.Choose(picked, wanted, [.. _givenTracks, .. result]);
            foreach (var track in picked) _scheduledAt = _scheduledAt.AddMilliseconds(track.DurationMs > 0 ? track.DurationMs : LiveSchedule.FallbackDurationMs);
        }
        else picked = Core.Mixes.ArtistVariety.Choose(picked, wanted, [.. _givenTracks, .. result]);
        var given = new SongSet(_givenTracks);
        var fresh = picked.Where(given.Add).ToList();
        if (fresh.Count > 0 || _played is null) picked = fresh;
        foreach (var t in picked) Remember(t);
        result.AddRange(picked);
        _featured?.Record(picked.Select(t => t.ArtistRatingKey).OfType<string>().Distinct(), DateTimeOffset.Now);
        context.Taste.Exposure?.Shown(result.Select(t => (t.RatingKey, t.ArtistRatingKey)));
        return result;
    }

    public void Played(Track track)
    {
        if (_played is null || _scope is null) return;
        var at = DateTimeOffset.Now;
        _ = Task.Run(() => _played.Record(_scope, track, at));
    }

    private void Remember(Track track)
    {
        if (!_given.Add(track.RatingKey)) return;
        _order.Enqueue(track.RatingKey);
        _givenTracks.Enqueue(track);
        while (_order.Count > MaxRemembered)
        {
            _given.Remove(_order.Dequeue());
            _givenTracks.Dequeue();
        }
    }
}

public sealed class BlockRadio
{
    private const int MaxRemembered = 300, RecentArtists = 10;

    private readonly Func<CancellationToken, Task<HeadlinerPools>> _pools;
    private readonly Func<TasteEngine> _taste;
    private readonly Dislikes _dislikes;
    private readonly HeadlinerHistory _history;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _given = [];
    private readonly Queue<Track> _givenTracks = new();
    private readonly Queue<string> _artistOrder = new();
    private readonly HashSet<string> _recent = [];
    private int _round = Random.Shared.Next(3);

    internal BlockRadio(Func<CancellationToken, Task<HeadlinerPools>> pools, Func<TasteEngine> taste, Dislikes dislikes, HeadlinerHistory history, int? round = null)
    {
        _pools = pools;
        _taste = taste;
        _dislikes = dislikes;
        _history = history;
        if (round is { } nextRound) _round = nextRound;
    }

    public async Task<IReadOnlyList<BlockPick>> NextAsync(IReadOnlySet<string> off, CancellationToken ct = default, bool recordFeatures = true, DateTimeOffset? airtime = null, IReadOnlyList<RotationPlay>? rotation = null, PulseProgramme? forcedProgramme = null)
    {
        var pools = await _pools(ct).ConfigureAwait(false);
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        IReadOnlyList<Track> Allowed(IReadOnlyList<Track> tracks) => MelodayText.Dedupe(tracks.Where(t => disliked.Eligible(t) && !off.Contains(t.RatingKey)));
        var allowed = new HeadlinerPools(Allowed(pools.Played), Allowed(pools.Fresh), Allowed(pools.Trending));
        var programme = forcedProgramme ?? (airtime is { } scheduled ? PulseProgramme.At(scheduled) : null);
        var selected = programme?.Select(allowed, airtime!.Value) ?? allowed;
        var programmeTitle = programme is null ? null : ReferenceEquals(selected, allowed)
            && programme.Format != PulseFormat.Rotation ? "Continuous Pulse" : programme.Title;
        allowed = selected;
        var at = airtime ?? DateTimeOffset.Now;
        if (rotation is { Count: > 0 })
        {
            IReadOnlyList<Track> Rested(IReadOnlyList<Track> tracks) => tracks.Where(t => !LiveRotation.IsResting(t, rotation, at)).ToList();
            var restedPool = new HeadlinerPools(Rested(allowed.Played), Rested(allowed.Fresh), Rested(allowed.Trending));
            if (restedPool.Played.Concat(restedPool.Fresh).Concat(restedPool.Trending).Select(LiveRotation.ArtistOf).Distinct().Count() >= 6) allowed = restedPool;
        }
        double RotationPenalty(Track track) => rotation is null ? 0 : LiveRotation.CandidatePenalty(track, rotation, at);
        var now = DateTimeOffset.Now;
        var rested = _history.Rested(now).Concat(_recent).ToHashSet(StringComparer.Ordinal);
        var picks = programme?.OrderedPicks(allowed) ?? Build(allowed, rested, RotationPenalty);
        if (picks.Count == 0 && rested.Count > 0)
        {
            _artistOrder.Clear();
            _recent.Clear();
            picks = Build(allowed, new HashSet<string>(), RotationPenalty);
        }
        _round++;
        var songs = new SongSet(_givenTracks);
        var unique = picks.Where(p => songs.Add(p.Track)).ToList();
        if (unique.Count > 0) picks = unique;
        foreach (var pick in picks)
        {
            if (_given.Add(pick.Track.RatingKey))
            {
                _order.Enqueue(pick.Track.RatingKey);
                _givenTracks.Enqueue(pick.Track);
            }
            while (_order.Count > MaxRemembered)
            {
                _given.Remove(_order.Dequeue());
                _givenTracks.Dequeue();
            }
            if (pick.Group > 0 && pick.Track.ArtistRatingKey is { } artist && _recent.Add(artist)) _artistOrder.Enqueue(artist);
            while (_artistOrder.Count > RecentArtists) _recent.Remove(_artistOrder.Dequeue());
        }
        if (recordFeatures) _history.Record(picks.Where(p => p.Group > 0 && p.Track.ArtistRatingKey is not null).Select(p => p.Track.ArtistRatingKey!).Distinct(), now);
        if (recordFeatures) _taste().Exposure?.Shown(picks.Select(p => (p.Track.RatingKey, p.Track.ArtistRatingKey)));
        return picks.Select(p => p with { Programme = p.Programme ?? programmeTitle }).ToList();
    }

    private IReadOnlyList<BlockPick> Build(HeadlinerPools pools, IReadOnlySet<string> rested, Func<Track, double>? rotationPenalty = null) =>
        HeadlinerProgram.Build(pools, _round, Random.Shared, _taste(), _given.ToHashSet(), rested, rotationPenalty: rotationPenalty);
}

public sealed class LocalRadio
{
    private const int Batch = 20;
    private const int PoolSize = 200;

    private readonly PlexSession _session;
    private readonly Dislikes _dislikes;
    private readonly Func<TasteEngine> _taste;
    private readonly PlaylistArtistIndex _playlists;
    private readonly HeadlinerHistory _history;
    private readonly TrendingService _trending;
    private readonly RadioPlaybackStore _played;
    private readonly MusicIntelligence _intelligence;
    private readonly ILogger<LocalRadio> _log;
    private readonly ConcurrentDictionary<string, Task<Dictionary<string, double>>> _affinity = new();
    private readonly ConcurrentDictionary<StationKind, HeadlinerHistory> _featured = new();

    private HeadlinerHistory FeaturedBy(StationKind kind) => _featured.GetOrAdd(kind, k =>
        new HeadlinerHistory(Path.Combine(Core.AppPaths.Root, "radio", $"featured-{k}.json".ToLowerInvariant()), TimeSpan.FromHours(18)));

    public LocalRadio(PlexSession session, Dislikes dislikes, TasteService taste, PlaylistArtistIndex playlists, HeadlinerHistory history, TrendingService trending, RadioPlaybackStore played, MusicIntelligence intelligence, ILogger<LocalRadio> log)
    {
        _history = history;
        _trending = trending;
        _played = played;
        _intelligence = intelligence;
        intelligence.Changed += (_, _) => Clear();
        _session = session;
        _dislikes = dislikes;
        _taste = () => taste.Engine;
        _playlists = playlists;
        _log = log;
    }

    public static int BatchSize => Batch;

    public void Clear() => _affinity.Clear();

    public RadioSession ForArtist(Artist seed, Track? lead = null, MediaItem? soundSeed = null)
    {
        Artist? full = null;
        return new RadioSession(async ct =>
        {
            if (_session.Client is not { } client || _session.Section is not { } section) return Empty;
            full ??= (await client.GetItemsAsync([seed.RatingKey], ct).ConfigureAwait(false)).OfType<Artist>().FirstOrDefault() ?? seed;
            if (_intelligence.IsMeloday)
            {
                var sonic = await _intelligence.SimilarAsync<Track>((soundSeed ?? lead ?? (MediaItem)seed).RatingKey, PoolSize, ct: ct).ConfigureAwait(false);
                if (sonic.Count >= 10 && sonic.Select(t => t.ArtistRatingKey ?? t.DisplayArtist).Distinct().Count() >= 3)
                {
                    var ownTracks = await client.GetArtistTracksAsync(seed.RatingKey, ct).ConfigureAwait(false);
                    var affinities = sonic.Where(t => t.ArtistRatingKey is not null).GroupBy(t => t.ArtistRatingKey!)
                        .ToDictionary(g => g.Key, g => Math.Min(1.0, 0.5 + g.Count() / 20.0));
                    return new RadioPlan([.. ownTracks, .. sonic], EmptyTags, new HashSet<string> { seed.RatingKey }, affinities);
                }
            }
            var affinity = await AffinityAsync(full, ct).ConfigureAwait(false);
            var near = affinity.OrderByDescending(a => a.Value).Take(30).Select(a => a.Key).ToList();
            var own = client.GetArtistTracksAsync(seed.RatingKey, ct);
            var others = near.Count == 0
                ? Task.FromResult<IReadOnlyList<Track>>([])
                : TracksAsync(client, section.Key, "artist.id", near, PoolSize, ct);
            await Task.WhenAll(own, others).ConfigureAwait(false);
            var tags = full.Genres.Concat(full.Styles).Concat(full.Moods).Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToHashSet();
            return new RadioPlan([.. own.Result, .. others.Result], tags, new HashSet<string> { seed.RatingKey }, affinity);
        }, _taste, _dislikes, lead, played: _played,
            scope: $"{_session.Account?.Id ?? _session.Account?.Username}|{_session.Server?.MachineIdentifier}|{_session.Section?.Key}|artist:{seed.RatingKey}");
    }

    public RadioSession ForBuilt(BuiltMix mix) => new(async ct =>
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return Empty;
        var filter = mix.AlbumKeys.Count > 0
            ? new KeyValuePair<string, string?>("album.id", string.Join(',', mix.AlbumKeys))
            : new KeyValuePair<string, string?>("artist.id", string.Join(',', mix.ArtistKeys));
        var page = await client.GetTracksAsync(section.Key, 0, PoolSize, "random", ct, filter).ConfigureAwait(false);
        return new RadioPlan(page.Items, new HashSet<string>(), mix.ArtistKeys.ToHashSet(), new Dictionary<string, double>());
    }, _taste, _dislikes, played: _played,
        scope: $"{_session.Account?.Id ?? _session.Account?.Username}|{_session.Server?.MachineIdentifier}|{_session.Section?.Key}|{mix.RatingKey}");

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
    }, _taste, _dislikes, featured: FeaturedBy(station.StationKind));

    public BlockRadio ForBlocks(int? round = null) => new(async ct =>
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return new HeadlinerPools([], [], []);
        var sk = section.Key;
        var played = PlayedPoolAsync(client, sk, ct);
        var fresh = FreshPoolAsync(client, sk, ct);
        var trending = TrendingPoolAsync();
        await Task.WhenAll(played, fresh, trending).ConfigureAwait(false);
        var pool = played.Result;
        if (pool.Count < 40) pool = [.. pool, .. await LibraryPoolAsync(client, sk, ct).ConfigureAwait(false)];
        return new HeadlinerPools(pool, fresh.Result, trending.Result);
    }, _taste, _dislikes, _history, round);

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
            }
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
        return artist is null ? null : ForArtist(artist, lead: leadWithSeed ? seed as Track : null, soundSeed: seed);
    }

    private Task<Dictionary<string, double>> AffinityAsync(Artist artist, CancellationToken ct)
    {
        if (!_affinity.TryGetValue(artist.RatingKey, out var load) || load.IsFaulted || load.IsCanceled)
            _affinity[artist.RatingKey] = load = ComputeAffinityAsync(artist, ct);
        return load;
    }

    private async Task<Dictionary<string, double>> ComputeAffinityAsync(Artist artist, CancellationToken ct)
    {
        var near = new ConcurrentDictionary<string, double>(StringComparer.Ordinal);
        void Add(string key, double weight)
        {
            if (key == artist.RatingKey) return;
            near.AddOrUpdate(key, weight, (_, previous) => Math.Max(previous, weight));
        }
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        var sk = section.Key;
        try
        {
            async Task Similar()
            {
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
        return near.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
    }

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

    private static async Task<IReadOnlyList<Track>> LibraryPoolAsync(PlexServerClient client, string sk, CancellationToken ct)
    {
        var random = client.GetTracksAsync(sk, 0, 150, "random", ct);
        var loved = client.GetTracksAsync(sk, 0, 80, "viewCount:desc", ct, new KeyValuePair<string, string?>("viewCount>>", "0"));
        await Task.WhenAll(random, loved).ConfigureAwait(false);
        return [.. random.Result.Items, .. loved.Result.Items];
    }
}
