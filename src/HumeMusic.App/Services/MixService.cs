using System.Collections.Concurrent;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>An artist in a mix: name and photo.</summary>
public sealed record MixArtist(string Name, string? Thumb);

/// <summary>What a mix contains: its title and its artists, most frequent first.</summary>
/// <param name="LeadIsSubject">An artist mix leads with its own artist, who isn't "featured".</param>
/// <param name="Pictures">What the cover shows in place of the artists' photos (the album covers of a mix that sounds like a song), the first in the middle; null: the artists.</param>
public sealed record MixInfo(string Title, IReadOnlyList<MixArtist> Artists, bool LeadIsSubject = true, IReadOnlyList<MixArtist>? Pictures = null)
{
    /// <summary>"Featuring Doechii, JT, Beyoncé": the (other) artists in the mix.</summary>
    public string Featuring
    {
        get
        {
            var featured = Artists.Skip(LeadIsSubject ? 1 : 0).Take(4).Select(a => a.Name).ToList();
            return featured.Count > 0 ? "Featuring " + string.Join(", ", featured) : string.Empty;
        }
    }
}

/// <summary>
/// Meloday's personalised mixes. A mix is a finite set of about thirty songs picked from what the
/// listener plays; it plays through and ends, and a different one is made as their listening changes.
/// That is what sets it apart from a radio (a station, or "Radio" on a song or artist), which starts
/// from something the listener chooses and never ends. Artist and tag mixes are built from the
/// listener's artists, genres and moods; niche mixes are intersections learned from tags, recent
/// behaviour, listening context, cached audio analysis, artist sessions and Plex sonic relationships.
/// Mixes whose artists mostly overlap are never both shown: each one has to feel like its own.
/// </summary>
public sealed class MixService
{
    private const int ArtistMixCount = 8;
    private const int GenreMixCount = 3;
    private const int MoodMixCount = 3;
    private const int NicheMixCount = 14;
    private const int TagBatch = 60;
    /// <summary>How many songs a mix plays.</summary>
    private const int MixLength = 30;
    /// <summary>Two mixes sharing at least this share of their leading artists count as the same mix.</summary>
    private const double SameMixOverlap = 0.4;
    private const int NicheBatch = 180;
    private const int NicheTrackCount = 30;
    private const int NicheArtistCap = 3;
    private static readonly TimeSpan ListLifetime = TimeSpan.FromMinutes(20);

    private static readonly HashSet<string> NoiseMoods = new(StringComparer.OrdinalIgnoreCase) { "ArtistPopular" };

    private readonly PlexSession _session;
    private readonly Dislikes _dislikes;
    private readonly PlexPass _pass;
    private readonly LocalRadio _local;
    private readonly AudioAnalysisService _audio;
    private readonly ILogger<MixService> _log;
    private readonly ConcurrentDictionary<string, Task<MixInfo?>> _mixes = new();
    private readonly ConcurrentDictionary<string, string> _stations = new();
    private readonly ConcurrentDictionary<string, RadioSession> _radios = new();
    private readonly SemaphoreSlim _gate = new(2, 2);
    // The artists who have a mix of their own: another mix puts them after its other artists, so its cover
    // doesn't picture the same face as theirs.
    private volatile HashSet<string> _subjects = new(StringComparer.OrdinalIgnoreCase);
    private Task<IReadOnlyList<MediaItem>>? _list;
    private DateTime _listAt;
    private IReadOnlyList<HistoryPlay> _history = [];
    private DateTime _historyAt;

    public MixService(PlexSession session, Dislikes dislikes, PlexPass pass, LocalRadio local, AudioAnalysisService audio, ILogger<MixService> log)
    {
        _session = session;
        _dislikes = dislikes;
        _pass = pass;
        _local = local;
        _audio = audio;
        _log = log;
    }

    // ------------------------------------------------------------------ choosing mixes

    /// <summary>The mixes for Home and Made for You, kept briefly so opening pages does not re-query Plex.</summary>
    public Task<IReadOnlyList<MediaItem>> GetMixesAsync()
    {
        if (_list is null || _list.IsFaulted || DateTime.UtcNow - _listAt > ListLifetime)
        {
            _listAt = DateTime.UtcNow;
            _list = BuildMixesAsync();
        }
        return _list;
    }

    public async Task<IReadOnlyList<NicheMix>> GetNicheMixesAsync() =>
        (await GetMixesAsync().ConfigureAwait(false)).OfType<NicheMix>().ToList();

    private async Task<IReadOnlyList<MediaItem>> BuildMixesAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        try
        {
            var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
            var recent = (await client.GetTracksAsync(section.Key, 0, 300, "lastViewedAt:desc", default,
                new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false)).Items;
            var now = DateTimeOffset.Now;
            var recentAllowed = recent.Where(disliked.Eligible).ToList();
            var historyTask = RecentHistoryAsync(client, section.Key, default);
            var energyTask = CachedEnergyAsync(recentAllowed.Take(120));

            // Artist mixes: recent artists that Plex has a station for. The same request also provides
            // artist moods, used to supplement Plex libraries whose individual tracks have sparse moods.
            var candidates = ListeningProfile.Artists(recentAllowed, now, disliked.BlocksArtist)
                .Take(ArtistMixCount + 4).ToList();
            var details = await Task.WhenAll(candidates.Select(async a =>
            {
                try
                {
                    return (Profile: a, Info: await client.GetArtistWithStationAsync(a.RatingKey).ConfigureAwait(false));
                }
                catch (PlexException)
                {
                    return (Profile: a, Info: null);
                }
            })).ConfigureAwait(false);

            var artistMixes = new List<MediaItem>();
            foreach (var (profile, info) in details)
            {
                if (info is not { } found || DislikedSet.IsDisliked(found.Artist.UserRating)) continue;
                if (_pass.IsAvailable)
                {
                    if (found.Station is not { } station) continue;
                    _stations[found.Artist.RatingKey] = station.Key;
                }
                if (artistMixes.Count >= ArtistMixCount) continue;
                var artist = found.Artist;
                artistMixes.Add(new ArtistMix
                {
                    RatingKey = "mix:" + artist.RatingKey,
                    Title = artist.Title + " Mix",
                    Thumb = artist.Thumb ?? profile.Thumb,
                    Art = artist.Art,
                    Artist = artist,
                });
            }

            _subjects = artistMixes.OfType<ArtistMix>().Select(a => a.Artist.Title).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var genres = ListeningProfile.Genres(recentAllowed, now);
            var moods = ListeningProfile.Rank(details
                .Where(d => d.Info is not null)
                .SelectMany(d => d.Info!.Value.Artist.Moods.Where(m => !NoiseMoods.Contains(m)).Select(m => (m, d.Profile.Weight))));
            var artistMoods = details.Where(d => d.Info is not null)
                .ToDictionary(d => d.Profile.RatingKey, d => d.Info!.Value.Artist.Moods, StringComparer.Ordinal);

            var tagCount = _pass.IsAvailable ? 1 : 2;
            var genreMixes = await PickTagMixesAsync(genres, MixTagKind.Genre, GenreMixCount * tagCount).ConfigureAwait(false);
            var moodMixes = await PickTagMixesAsync(moods, MixTagKind.Mood, MoodMixCount * tagCount, exclude: genreMixes.Select(m => m.Title)).ConfigureAwait(false);

            var history = await historyTask.ConfigureAwait(false);
            var energy = await energyTask.ConfigureAwait(false);
            var nicheRecipes = NicheMixes.Generate(recentAllowed, now, NicheMixCount + 12, artistMoods, history, energy).ToList();

            // Plex sonic analysis adds a genuinely different neighbourhood instead of another tag intersection.
            // At most two are attempted so Made for You never turns into a row of sonic seeds.
            if (_pass.IsAvailable)
            {
                foreach (var seed in recentAllowed.Where(t => t.HasSonicAnalysis).Take(2))
                    if (await SonicRecipeAsync(client, seed, disliked).ConfigureAwait(false) is { } sonic)
                        nicheRecipes.Insert(0, sonic);
            }

            var nicheMixes = await PickNicheMixesAsync(nicheRecipes.DistinctBy(r => r.StableKey).ToList(), NicheMixCount + 8).ConfigureAwait(false);

            // Each mix has to be its own: one whose leading artists mostly repeat an earlier mix's is dropped.
            // Artist mixes are subjects in their own right, so they only set the bar for the rest.
            var taken = new List<HashSet<string>>();
            foreach (var info in await Task.WhenAll(artistMixes.Select(GetAsync)).ConfigureAwait(false))
                if (info is not null) taken.Add(LeadArtists(info));
            async Task<List<T>> Distinct<T>(IEnumerable<T> candidates) where T : MediaItem
            {
                var kept = new List<T>();
                foreach (var mix in candidates)
                {
                    if (await GetAsync(mix).ConfigureAwait(false) is not { } info) continue;
                    var lead = LeadArtists(info);
                    if (taken.Any(other => MixOverlap.Between(lead, other) >= SameMixOverlap)) continue;
                    taken.Add(lead);
                    kept.Add(mix);
                }
                return kept;
            }
            genreMixes = await Distinct(genreMixes).ConfigureAwait(false);
            moodMixes = await Distinct(moodMixes).ConfigureAwait(false);
            nicheMixes = (await Distinct(nicheMixes).ConfigureAwait(false)).Take(NicheMixCount).ToList();

            var result = new List<MediaItem>();
            var queues = new[]
            {
                new Queue<MediaItem>(artistMixes),
                new Queue<MediaItem>(genreMixes),
                new Queue<MediaItem>(moodMixes),
                new Queue<MediaItem>(nicheMixes),
            };
            int[] pattern = [0, 3, 0, 1, 3, 0, 2];
            for (var i = 0; queues.Any(q => q.Count > 0); i++)
            {
                var q = queues[pattern[i % pattern.Length]];
                if (q.Count > 0) result.Add(q.Dequeue());
                else if (queues.First(x => x.Count > 0) is { } next) result.Add(next.Dequeue());
            }
            return result;
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Mixes unavailable: {Message}", ex.Message);
            _list = null;
            return [];
        }
    }

    private static HashSet<string> LeadArtists(MixInfo info) =>
        info.Artists.Take(8).Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private async Task<List<TagMix>> PickTagMixesAsync(IReadOnlyList<ProfileTag> tags, MixTagKind kind, int count, IEnumerable<string>? exclude = null)
    {
        var taken = new HashSet<string>(exclude ?? [], StringComparer.OrdinalIgnoreCase);
        var candidates = tags.Take(count + 3)
            .Select(t => new TagMix
            {
                RatingKey = $"mix:{(kind == MixTagKind.Genre ? "genre" : "mood")}:{t.Tag}",
                Title = t.Tag + " Mix",
                TagKind = kind,
                Tag = t.Tag,
            })
            .Where(m => !taken.Contains(m.Title))
            .ToList();
        var infos = await Task.WhenAll(candidates.Select(GetAsync)).ConfigureAwait(false);
        return candidates.Where((m, i) => infos[i] is { Artists.Count: >= 3 }).Take(count).ToList();
    }

    private async Task<List<NicheMix>> PickNicheMixesAsync(IReadOnlyList<NicheMixRecipe> recipes, int count)
    {
        var picked = new List<NicheMix>(count);
        foreach (var batch in recipes.Select(r => r.ToMix()).Chunk(2))
        {
            var infos = await Task.WhenAll(batch.Select(GetAsync)).ConfigureAwait(false);
            for (var i = 0; i < batch.Length && picked.Count < count; i++)
            {
                if (infos[i] is { Artists.Count: >= 3 }) picked.Add(batch[i]);
            }
            if (picked.Count >= count) break;
        }
        return picked;
    }

    private async Task<NicheMixRecipe?> SonicRecipeAsync(PlexServerClient client, Track seed, DislikedSet disliked)
    {
        try
        {
            var similar = (await client.GetSonicallySimilarAsync<Track>(seed.RatingKey, 45, 0.8).ConfigureAwait(false))
                .Where(disliked.Eligible).ToList();
            if (similar.Count < 12) return null;
            var genre = TopTag(similar.SelectMany(t => t.Genres));
            var mood = TopTag(similar.SelectMany(t => t.Moods).Where(m => !NoiseMoods.Contains(m)));
            var decade = similar.Where(t => t.Year is >= 1900 and < 2100).GroupBy(t => t.Year!.Value / 10 * 10)
                .OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
            if (genre is null && mood is null && decade is null) return null;
            return new NicheMixRecipe(genre, mood, decade, 3.0, Focus: NicheFocus.Sonic,
                SeedTrackKey: seed.RatingKey, SeedTrackTitle: seed.Title);
        }
        catch (PlexException)
        {
            return null;
        }
    }

    private static string? TopTag(IEnumerable<string> tags) => tags.Where(t => !string.IsNullOrWhiteSpace(t))
        .GroupBy(t => t.Trim(), StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();

    // ------------------------------------------------------------------ what's in a mix

    /// <summary>A mix's title and artists (for its board), sampled once per session.</summary>
    public Task<MixInfo?> GetAsync(MediaItem mix) => _mixes.GetOrAdd(mix.RatingKey, _ => LoadAsync(mix));

    private async Task<MixInfo?> LoadAsync(MediaItem mix)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tracks = await SampleAsync(mix).ConfigureAwait(false);
            if (tracks is null) return null;
            switch (mix)
            {
                case ArtistMix a:
                {
                    var others = RankArtists(tracks.Where(t => t.ArtistRatingKey != a.Artist.RatingKey
                                                               && !string.Equals(t.ArtistTitle, a.Artist.Title, StringComparison.OrdinalIgnoreCase)));
                    return new MixInfo(a.Title, [new MixArtist(a.Artist.Title, a.Artist.Thumb), .. others]);
                }
                case NicheMix { Focus: NicheFocus.Sonic } sonic when tracks.Count >= 12:
                {
                    // A mix that sounds like a song shows album covers: the song's own album in the middle, then those of the mix.
                    var covers = new List<MixArtist>();
                    if (sonic.SeedTrackKey is { Length: > 0 } seedKey && _session.Client is { } seedClient
                        && (await seedClient.GetItemsAsync([seedKey]).ConfigureAwait(false)).OfType<Track>().FirstOrDefault() is { } seedTrack
                        && seedTrack.DisplayThumb is { } seedThumb)
                        covers.Add(new MixArtist(seedTrack.AlbumTitle ?? seedTrack.Title, seedThumb));
                    covers.AddRange(RankAlbums(tracks.Where(t => t.RatingKey != sonic.SeedTrackKey)));
                    return new MixInfo(sonic.Title, Unclaimed(RankArtists(tracks)), LeadIsSubject: false,
                        Pictures: covers.DistinctBy(c => c.Thumb).Take(11).ToList());
                }
                case TagMix t:
                    return tracks.Count < 12 ? new MixInfo(t.Title, [], LeadIsSubject: false) : new MixInfo(t.Title, Unclaimed(RankArtists(tracks)), LeadIsSubject: false);
                case BuiltMix b:
                    return new MixInfo(b.Title, Unclaimed(RankArtists(tracks)), LeadIsSubject: false);
                default:
                    return null;
            }
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Mix {Mix} unavailable: {Message}", mix.Title, ex.Message);
            _mixes.TryRemove(mix.RatingKey, out _);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The artists with no mix of their own first (each in the order they were heard), then those who have one.</summary>
    private List<MixArtist> Unclaimed(IEnumerable<MixArtist> artists)
    {
        var list = artists.ToList();
        var subjects = _subjects;
        return [.. list.Where(a => !subjects.Contains(a.Name)), .. list.Where(a => subjects.Contains(a.Name))];
    }

    private static IEnumerable<MixArtist> RankArtists(IEnumerable<Track> tracks) =>
        tracks.Where(t => !ListeningProfile.IsPlaceholderArtist(t.ArtistTitle))
            .GroupBy(t => t.ArtistTitle!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new MixArtist(g.Key, g.Select(t => t.ArtistThumb).FirstOrDefault(p => p is not null)));

    /// <summary>The albums of the songs, the one with most songs here first; each named by its title and shown by its cover.</summary>
    private static IEnumerable<MixArtist> RankAlbums(IEnumerable<Track> tracks) =>
        tracks.Where(t => t.DisplayThumb is not null)
            .GroupBy(t => t.AlbumRatingKey ?? t.AlbumTitle ?? t.RatingKey)
            .OrderByDescending(g => g.Count())
            .Select(g => new MixArtist(g.First().AlbumTitle ?? g.First().Title, g.First().DisplayThumb));

    private async Task<IReadOnlyList<Track>?> SampleAsync(MediaItem mix)
    {
        using (PlexServerClient.ForShowing())
            return await FirstTracksAsync(mix, batches: 1, sample: true).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ playing a mix

    public async Task<IReadOnlyList<Track>?> FirstTracksAsync(MediaItem mix, int batches = 3, bool sample = false)
    {
        if (_session.Client is not { } client) return null;
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        switch (mix)
        {
            case ArtistMix a:
            {
                if (_pass.IsAvailable && await StationAsync(client, a.Artist.RatingKey).ConfigureAwait(false) is { } station)
                {
                    var loads = await Task.WhenAll(Enumerable.Range(0, batches).Select(_ => client.GetStationTracksAsync(station))).ConfigureAwait(false);
                    var plex = Freshness.Prefer(loads.SelectMany(b => b).DistinctBy(t => t.RatingKey).Where(disliked.Eligible), t => t.LastViewedAt, DateTimeOffset.Now, MixLength)
                        .Take(sample ? int.MaxValue : MixLength).ToList();
                    if (plex.Count > 0) return plex;
                }
                var radio = sample ? _local.ForArtist(a.Artist) : RadioFor(a);
                var first = await radio.NextAsync(LocalRadio.BatchSize).ConfigureAwait(false);
                if (sample || first.Count >= MixLength) return first;
                var second = await radio.NextAsync(LocalRadio.BatchSize).ConfigureAwait(false);
                return first.Concat(second).DistinctBy(t => t.RatingKey).Take(MixLength).ToList();
            }
            case TagMix t when NicheMixRecipe.TryFrom(t, out var niche):
                return await NicheTracksAsync(client, niche, disliked, default).ConfigureAwait(false);
            case TagMix t:
                return await TagTracksAsync(client, t, disliked, default).ConfigureAwait(false);
            case Station st:
                return await StationTracksAsync(client, st, disliked, batches, default).ConfigureAwait(false);
            case BuiltMix b:
                return await BuiltTracksAsync(client, b, disliked, default).ConfigureAwait(false);
            default:
                return null;
        }
    }

    /// <summary>
    /// The next tracks for a playing radio (a station, or one the listener built), for topping up the
    /// queue. A mix has none: it is a finite set that plays through and ends.
    /// </summary>
    public Func<CancellationToken, Task<IReadOnlyList<Track>>>? MoreTracks(MediaItem mix)
    {
        if (_session.Client is not { } client) return null;
        return mix switch
        {
            Station st => async ct => await StationTracksAsync(client, st, await _dislikes.GetAsync().ConfigureAwait(false), 1, ct).ConfigureAwait(false),
            // A radio the listener built from artists or albums they chose never ends either; Meloday's Discovery Mix does.
            BuiltMix b when b.RatingKey != "mix:discovery" => async ct => await BuiltTracksAsync(client, b, await _dislikes.GetAsync().ConfigureAwait(false), ct).ConfigureAwait(false),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ stations and built mixes

    private async Task<IReadOnlyList<Track>> StationTracksAsync(PlexServerClient client, Station station, DislikedSet disliked, int batches, CancellationToken ct)
    {
        if (!station.IsLocal && station.Key is { } key && _pass.IsAvailable)
        {
            var loads = await Task.WhenAll(Enumerable.Range(0, batches).Select(_ => client.GetStationTracksAsync(key, ct))).ConfigureAwait(false);
            var plex = Freshness.Prefer(loads.SelectMany(b => b).DistinctBy(t => t.RatingKey).Where(disliked.Eligible), t => t.LastViewedAt, DateTimeOffset.Now, MixLength).ToList();
            if (plex.Count > 0) return plex;
        }
        return await RadioFor(station).NextAsync(LocalRadio.BatchSize, ct).ConfigureAwait(false);
    }

    private RadioSession RadioFor(MediaItem mix) => _radios.GetOrAdd(mix.RatingKey, _ => mix switch
    {
        ArtistMix a => _local.ForArtist(a.Artist),
        Station s => _local.ForStation(s),
        _ => throw new ArgumentException("No radio for this mix.", nameof(mix)),
    });

    private async Task<IReadOnlyList<Track>> BuiltTracksAsync(PlexServerClient client, BuiltMix mix, DislikedSet disliked, CancellationToken ct)
    {
        if (_session.Section is not { } section) return [];
        var filter = mix.AlbumKeys.Count > 0
            ? new KeyValuePair<string, string?>("album.id", string.Join(',', mix.AlbumKeys))
            : new KeyValuePair<string, string?>("artist.id", string.Join(',', mix.ArtistKeys));
        var result = await client.GetTracksAsync(section.Key, 0, TagBatch, "random", ct, filter).ConfigureAwait(false);
        return Freshness.Prefer(result.Items.Where(disliked.Eligible), t => t.LastViewedAt, DateTimeOffset.Now, MixLength).Take(MixLength).ToList();
    }

    public async Task<IReadOnlyList<Track>> SampleAsync(BuiltMix mix, int count)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        var filter = mix.AlbumKeys.Count > 0
            ? new KeyValuePair<string, string?>("album.id", string.Join(',', mix.AlbumKeys))
            : new KeyValuePair<string, string?>("artist.id", string.Join(',', mix.ArtistKeys));
        try
        {
            var items = (await client.GetTracksAsync(section.Key, 0, count, "random", default, filter).ConfigureAwait(false)).Items;
            return Freshness.Prefer(items.Where(disliked.Eligible), t => t.LastViewedAt, DateTimeOffset.Now, count).ToList();
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Mix sample unavailable: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<string?> StationAsync(PlexServerClient client, string artistRatingKey)
    {
        if (_stations.TryGetValue(artistRatingKey, out var known)) return known;
        if (await client.GetArtistStationKeyAsync(artistRatingKey).ConfigureAwait(false) is not { } key) return null;
        _stations[artistRatingKey] = key;
        return key;
    }

    private async Task<IReadOnlyList<Track>> TagTracksAsync(PlexServerClient client, TagMix mix, DislikedSet disliked, CancellationToken ct)
    {
        if (_session.Section is not { } section) return [];
        var field = mix.TagKind == MixTagKind.Genre ? "genre" : "mood";
        var result = await client.GetTracksAsync(section.Key, 0, TagBatch, "random", ct, new KeyValuePair<string, string?>(field, mix.Tag)).ConfigureAwait(false);
        var now = DateTimeOffset.Now;
        // Two songs of each artist at most, those not played lately favoured, in a random order.
        return Freshness.WeightedOrder(
                Freshness.Prefer(result.Items.Where(disliked.Eligible), t => t.LastViewedAt, now, MixLength * 2)
                    .GroupBy(t => t.ArtistRatingKey ?? t.RatingKey).SelectMany(g => g.Take(2)),
                t => t.LastViewedAt, now, Random.Shared)
            .Take(MixLength).ToList();
    }

    // ------------------------------------------------------------------ niche playback

    private async Task<IReadOnlyList<Track>> NicheTracksAsync(PlexServerClient client, NicheMixRecipe mix, DislikedSet disliked, CancellationToken ct)
    {
        if (_session.Section is not { } section) return [];
        var filters = NicheFilters(mix);
        var genericTask = client.GetTracksAsync(section.Key, 0, NicheBatch, "random", ct, filters.ToArray());

        IReadOnlyList<Track> sonic = [];
        if (mix.Focus == NicheFocus.Sonic && mix.SeedTrackKey is { Length: > 0 } seed && _pass.IsAvailable)
        {
            try
            {
                sonic = (await client.GetSonicallySimilarAsync<Track>(seed, NicheBatch, 0.9, ct).ConfigureAwait(false))
                    .Where(disliked.Eligible).Where(t => MatchesRecipe(t, mix)).ToList();
            }
            catch (PlexException)
            {
            }
        }

        var generic = (await genericTask.ConfigureAwait(false)).Items.Where(disliked.Eligible);
        var pool = sonic.Concat(generic).DistinctBy(t => t.RatingKey).ToList();
        if (pool.Count == 0) return [];

        var historyTask = mix.Context is null ? Task.FromResult<IReadOnlyList<HistoryPlay>>([]) : RecentHistoryAsync(client, section.Key, ct);
        var energyTask = mix.Energy == NicheEnergy.Any ? Task.FromResult<IReadOnlyDictionary<string, NicheEnergy>>(new Dictionary<string, NicheEnergy>()) : CachedEnergyAsync(pool);
        var history = await historyTask.ConfigureAwait(false);
        var energy = await energyTask.ConfigureAwait(false);
        var contextWeights = ContextWeights(history, mix.Context);
        var sonicKeys = sonic.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        var circle = mix.Artists.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;
        var random = new Random(StableSeed(mix.StableKey, now.Date));

        bool Familiar(Track t) => t.ViewCount > 0 && t.LastViewedAt is { } at && now - at <= TimeSpan.FromDays(45);
        bool Rediscovery(Track t) => t.ViewCount > 0 && !Familiar(t);
        double Score(Track t)
        {
            var score = Math.Log2(Math.Max(0, t.ViewCount) + 1) * 0.32
                + (t.UserRating ?? 0) * 0.08
                - Math.Min(6, Math.Max(0, t.SkipCount)) * 0.28;
            if (t.LastViewedAt is { } at) score += ListeningProfile.Weight(at, now) * 1.35;
            if (t.ViewCount == 0) score += 0.22;
            // ...but not the song just heard (see Freshness): the recent play that shows what is liked also says it was just had.
            score -= Freshness.Penalty(t.LastViewedAt, now) * 1.5;

            score += mix.Focus switch
            {
                NicheFocus.Lately when Familiar(t) => 1.1,
                NicheFocus.Rediscovery when Rediscovery(t) => 1.25,
                NicheFocus.DeepCuts when t.ViewCount == 0 => 1.45,
                NicheFocus.DeepCuts when t.ViewCount <= 2 => 0.9,
                NicheFocus.DeepCuts => -Math.Min(1.0, t.ViewCount * 0.06),
                NicheFocus.Sonic when sonicKeys.Contains(t.RatingKey) => 1.5,
                _ => 0,
            };
            if (contextWeights.TryGetValue(t.RatingKey, out var context)) score += Math.Min(1.5, context * 0.45);
            if (circle.Count >= 2 && circle.Contains(t.ArtistTitle ?? string.Empty)) score += 1.1;
            if (mix.Energy != NicheEnergy.Any && energy.TryGetValue(t.RatingKey, out var band))
                score += band == mix.Energy ? 0.9 : -0.45;
            return score + random.NextDouble() * 0.04;
        }

        var selected = new List<Track>(NicheTrackCount);
        var selectedKeys = new HashSet<string>(StringComparer.Ordinal);
        var artistCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void TakeFrom(IEnumerable<Track> source, int wanted)
        {
            var added = 0;
            foreach (var track in source.OrderByDescending(Score))
            {
                if (added >= wanted || selected.Count >= NicheTrackCount) break;
                if (!selectedKeys.Add(track.RatingKey)) continue;
                var artist = NicheArtistKey(track);
                artistCounts.TryGetValue(artist, out var artistCount);
                if (artistCount >= NicheArtistCap)
                {
                    selectedKeys.Remove(track.RatingKey);
                    continue;
                }
                artistCounts[artist] = artistCount + 1;
                selected.Add(track);
                added++;
            }
        }

        var quotas = mix.Focus switch
        {
            NicheFocus.Lately => (Familiar: 24, Rediscovery: 4, New: 2),
            NicheFocus.Rediscovery => (Familiar: 6, Rediscovery: 20, New: 4),
            NicheFocus.DeepCuts => (Familiar: 4, Rediscovery: 8, New: 18),
            NicheFocus.Sonic => (Familiar: 15, Rediscovery: 8, New: 7),
            _ => (Familiar: 18, Rediscovery: 8, New: 4),
        };
        TakeFrom(pool.Where(Familiar), quotas.Familiar);
        TakeFrom(pool.Where(Rediscovery), quotas.Rediscovery);
        TakeFrom(pool.Where(t => t.ViewCount == 0), quotas.New);
        if (selected.Count < NicheTrackCount) TakeFrom(pool, NicheTrackCount - selected.Count);

        return SpreadNicheArtists(selected, random);
    }

    private static List<KeyValuePair<string, string?>> NicheFilters(NicheMixRecipe mix)
    {
        var filters = new List<KeyValuePair<string, string?>>(4);
        if (mix.Genre is { Length: > 0 } genre) filters.Add(new("genre", genre));
        if (mix.Mood is { Length: > 0 } mood) filters.Add(new("mood", mood));
        if (mix.Decade is { } decade)
        {
            filters.Add(new("album.year>>", (decade - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            filters.Add(new("album.year<<", (decade + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        return filters;
    }

    private static bool MatchesRecipe(Track track, NicheMixRecipe mix)
    {
        if (mix.Decade is { } decade && track.Year is { } year && year / 10 * 10 != decade) return false;
        if (mix.Genre is { Length: > 0 } genre && track.Genres.Count > 0 && !track.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase)) return false;
        // Mood metadata is often absent at track level, so absence is unknown rather than a mismatch.
        if (mix.Mood is { Length: > 0 } mood && track.Moods.Count > 0 && !track.Moods.Contains(mood, StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static Dictionary<string, int> ContextWeights(IReadOnlyList<HistoryPlay> history, string? context)
    {
        if (context is null) return [];
        var period = MelodayPeriods.Named(context);
        bool Match(HistoryPlay p) => period is not null
            ? period.Hours.Contains(p.ViewedAt.LocalDateTime.Hour)
            : context == "Weekend"
                ? p.ViewedAt.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
                : context == "Weekday" && p.ViewedAt.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
        return history.Where(Match).GroupBy(p => p.RatingKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
    }

    private async Task<IReadOnlyList<HistoryPlay>> RecentHistoryAsync(PlexServerClient client, string section, CancellationToken ct)
    {
        if (_history.Count > 0 && DateTime.UtcNow - _historyAt < ListLifetime) return _history;
        try
        {
            var now = DateTimeOffset.Now;
            var account = _session.Server?.Owned == true ? "1" : null;
            _history = await client.GetHistoryAsync(section, now.AddDays(-MelodayProfile.LookbackDays), account, ct: ct).ConfigureAwait(false);
            _historyAt = DateTime.UtcNow;
            return _history;
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Listening context unavailable: {Message}", ex.Message);
            return [];
        }
    }

    private async Task<IReadOnlyDictionary<string, NicheEnergy>> CachedEnergyAsync(IEnumerable<Track> tracks)
    {
        var results = await Task.WhenAll(tracks.Take(180).Select(async track =>
        {
            var analysis = await _audio.CachedHeadAsync(track).ConfigureAwait(false);
            return (track.RatingKey, Energy: EnergyOf(analysis));
        })).ConfigureAwait(false);
        return results.Where(x => x.Energy != NicheEnergy.Any).ToDictionary(x => x.RatingKey, x => x.Energy, StringComparer.Ordinal);
    }

    private static NicheEnergy EnergyOf(HumeMusic.Core.Audio.AudioAnalysis? analysis)
    {
        if (analysis is not { HasBeat: true }) return NicheEnergy.Any;
        return analysis.Bpm switch
        {
            < 96 => NicheEnergy.Low,
            < 124 => NicheEnergy.Medium,
            _ => NicheEnergy.High,
        };
    }

    private static IReadOnlyList<Track> SpreadNicheArtists(IReadOnlyList<Track> tracks, Random random)
    {
        var remaining = tracks.OrderBy(_ => random.Next()).ToList();
        var result = new List<Track>(remaining.Count);
        string? previous = null;
        while (remaining.Count > 0)
        {
            var index = remaining.FindIndex(t => !string.Equals(NicheArtistKey(t), previous, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = 0;
            var track = remaining[index];
            remaining.RemoveAt(index);
            result.Add(track);
            previous = NicheArtistKey(track);
        }
        return result;
    }

    private static int StableSeed(string text, DateTime date)
    {
        unchecked
        {
            var hash = 17;
            foreach (var c in text) hash = hash * 31 + c;
            hash = hash * 31 + date.Year;
            hash = hash * 31 + date.DayOfYear;
            return hash;
        }
    }

    private static string NicheArtistKey(Track track) => track.ArtistRatingKey ?? track.ArtistTitle ?? track.RatingKey;

    /// <summary>Forgets every mix when the library changes or it is time to follow newer listening.</summary>
    public void Clear()
    {
        _mixes.Clear();
        _radios.Clear();
        _local.Clear();
        _list = null;
        _history = [];
        _historyAt = default;
    }
}
