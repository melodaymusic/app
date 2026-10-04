using System.Collections.Concurrent;
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
public sealed record MixInfo(string Title, IReadOnlyList<MixArtist> Artists, bool LeadIsSubject = true)
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
/// Meloday's mixes: artist mixes (Plex's artist radio stations), genre and mood mixes, and finite
/// niche mixes built from combinations that actually occur in the listener's recent taste. Disliked
/// music (one star or less) never plays in a mix, and disliked artists never get one. Mix contents
/// are sampled for the boards on Home, a couple at a time.
/// </summary>
public sealed class MixService
{
    private const int ArtistMixCount = 8;
    private const int GenreMixCount = 3;
    private const int MoodMixCount = 3;
    private const int NicheMixCount = 6;
    private const int TagBatch = 40;
    private const int NicheBatch = 100;
    private const int NicheTrackCount = 30;
    private const int NicheArtistCap = 3;
    private static readonly TimeSpan ListLifetime = TimeSpan.FromMinutes(20);

    // Plex tags that describe popularity rather than a mood.
    private static readonly HashSet<string> NoiseMoods = new(StringComparer.OrdinalIgnoreCase) { "ArtistPopular" };

    private readonly PlexSession _session;
    private readonly Dislikes _dislikes;
    private readonly ILogger<MixService> _log;
    private readonly ConcurrentDictionary<string, Task<MixInfo?>> _mixes = new();
    private readonly ConcurrentDictionary<string, string> _stations = new();
    private readonly SemaphoreSlim _gate = new(2, 2);
    private Task<IReadOnlyList<MediaItem>>? _list;
    private DateTime _listAt;

    private readonly PlexPass _pass;
    private readonly LocalRadio _local;
    // Meloday's own radios as they play (see LocalRadio), so a mix keeps going without repeating itself.
    private readonly ConcurrentDictionary<string, RadioSession> _radios = new();

    public MixService(PlexSession session, Dislikes dislikes, PlexPass pass, LocalRadio local, ILogger<MixService> log)
    {
        _session = session;
        _dislikes = dislikes;
        _pass = pass;
        _local = local;
        _log = log;
    }

    // ------------------------------------------------------------------ choosing mixes

    /// <summary>The mixes for Home, built from recent listening (kept for a while, then rebuilt).</summary>
    public Task<IReadOnlyList<MediaItem>> GetMixesAsync()
    {
        if (_list is null || _list.IsFaulted || DateTime.UtcNow - _listAt > ListLifetime)
        {
            _listAt = DateTime.UtcNow;
            _list = BuildMixesAsync();
        }
        return _list;
    }

    private async Task<IReadOnlyList<MediaItem>> BuildMixesAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        try
        {
            var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
            var recent = (await client.GetTracksAsync(section.Key, 0, 300, "lastViewedAt:desc", default,
                new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false)).Items;
            var now = DateTimeOffset.Now;
            var recentAllowed = recent.Where(disliked.Allows).ToList();

            // Artist mixes: recent artists that Plex has a station for. The same request brings the
            // artist's moods, which (weighted by how much the artist was played) choose the mood mixes.
            var candidates = ListeningProfile.Artists(recentAllowed, now, disliked.Artists.Contains)
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
            // Artist mixes are Plex's artist radio (Plex Pass); without it Meloday's own radio plays them.
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

            var genres = ListeningProfile.Genres(recentAllowed, now);
            var moods = ListeningProfile.Rank(details
                .Where(d => d.Info is not null)
                .SelectMany(d => d.Info!.Value.Artist.Moods.Where(m => !NoiseMoods.Contains(m)).Select(m => (m, d.Profile.Weight))));

            // A tag mix needs enough music to be worth playing; sampling it also fills in its board.
            var tagCount = _pass.IsAvailable ? 1 : 2;
            var genreMixes = await PickTagMixesAsync(genres, MixTagKind.Genre, GenreMixCount * tagCount).ConfigureAwait(false);
            var moodMixes = await PickTagMixesAsync(moods, MixTagKind.Mood, MoodMixCount * tagCount, exclude: genreMixes.Select(m => m.Title)).ConfigureAwait(false);

            // Niche concepts come from real co-occurrences in recent listening. We validate them in
            // small batches and stop as soon as the row is full, avoiding unnecessary Plex requests.
            var nicheRecipes = NicheMixes.Generate(recentAllowed, now, NicheMixCount + 4);
            var nicheMixes = await PickNicheMixesAsync(nicheRecipes, NicheMixCount).ConfigureAwait(false);

            // Keep the underlying result interleaved so callers that do not split the rows still get
            // a useful order. Home gives first-class niche mixes their own row.
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
                    // The mix's own artist leads; the others follow by how often they come up.
                    var others = RankArtists(tracks.Where(t => t.ArtistRatingKey != a.Artist.RatingKey
                                                               && !string.Equals(t.ArtistTitle, a.Artist.Title, StringComparison.OrdinalIgnoreCase)));
                    return new MixInfo(a.Title, [new MixArtist(a.Artist.Title, a.Artist.Thumb), .. others]);
                }
                case TagMix t when NicheMixRecipe.TryFrom(t, out _):
                    return tracks.Count < 12 ? new MixInfo(t.Title, [], LeadIsSubject: false) : new MixInfo(t.Title, RankArtists(tracks).ToList(), LeadIsSubject: false);
                case TagMix t:
                    return tracks.Count < 12 ? new MixInfo(t.Title, [], LeadIsSubject: false) : new MixInfo(t.Title, RankArtists(tracks).ToList(), LeadIsSubject: false);
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

    private static IEnumerable<MixArtist> RankArtists(IEnumerable<Track> tracks) =>
        tracks.Where(t => !ListeningProfile.IsPlaceholderArtist(t.ArtistTitle))
            .GroupBy(t => t.ArtistTitle!, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => new MixArtist(g.Key, g.Select(t => t.ArtistThumb).FirstOrDefault(p => p is not null)));

    private async Task<IReadOnlyList<Track>?> SampleAsync(MediaItem mix)
    {
        // Only for the board: one station batch is enough to name the artists, and random mix samples
        // are reused from last time while fresh ones are fetched.
        using (PlexServerClient.ForShowing())
            return await FirstTracksAsync(mix, batches: 1, sample: true).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ playing a mix

    /// <summary>
    /// The opening tracks of a mix (null when there is no mix to play): station batches for an artist
    /// mix, a random selection for a genre or mood mix, or a finite scored set for a niche mix.
    /// Disliked music is left out.
    /// </summary>
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
                    var plex = loads.SelectMany(b => b).DistinctBy(t => t.RatingKey).Where(disliked.Allows).ToList();
                    if (plex.Count > 0) return plex;
                }
                // A board's sample is its own radio, so showing the board doesn't use up the music to play.
                var radio = sample ? _local.ForArtist(a.Artist) : RadioFor(a);
                return await radio.NextAsync(LocalRadio.BatchSize).ConfigureAwait(false);
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

    /// <summary>The next tracks for a playing mix, for topping up the queue.</summary>
    public Func<CancellationToken, Task<IReadOnlyList<Track>>>? MoreTracks(MediaItem mix)
    {
        if (_session.Client is not { } client) return null;
        return mix switch
        {
            ArtistMix a when _pass.IsAvailable && _stations.TryGetValue(a.Artist.RatingKey, out var station) => async ct =>
            {
                var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
                var plex = (await client.GetStationTracksAsync(station, ct).ConfigureAwait(false)).Where(disliked.Allows).ToList();
                return plex.Count > 0 ? plex : await RadioFor(a).NextAsync(LocalRadio.BatchSize, ct).ConfigureAwait(false);
            },
            ArtistMix a => ct => RadioFor(a).NextAsync(LocalRadio.BatchSize, ct),
            TagMix t when NicheMixRecipe.TryFrom(t, out _) => null,
            TagMix t => async ct => await TagTracksAsync(client, t, await _dislikes.GetAsync().ConfigureAwait(false), ct).ConfigureAwait(false),
            Station st => async ct => await StationTracksAsync(client, st, await _dislikes.GetAsync().ConfigureAwait(false), 1, ct).ConfigureAwait(false),
            BuiltMix b => async ct => await BuiltTracksAsync(client, b, await _dislikes.GetAsync().ConfigureAwait(false), ct).ConfigureAwait(false),
            _ => null,
        };
    }

    // ------------------------------------------------------------------ stations and built mixes

    /// <summary>Tracks from one of Plex's stations (a Plex Pass feature; without it there are none).</summary>
    private async Task<IReadOnlyList<Track>> StationTracksAsync(PlexServerClient client, Station station, DislikedSet disliked, int batches, CancellationToken ct)
    {
        if (!station.IsLocal && station.Key is { } key && _pass.IsAvailable)
        {
            var loads = await Task.WhenAll(Enumerable.Range(0, batches).Select(_ => client.GetStationTracksAsync(key, ct))).ConfigureAwait(false);
            var plex = loads.SelectMany(b => b).DistinctBy(t => t.RatingKey).Where(disliked.Allows).ToList();
            if (plex.Count > 0) return plex;
        }
        // No Plex station (no Plex Pass, or Plex has nothing for it): Meloday's own radio plays it.
        return await RadioFor(station).NextAsync(LocalRadio.BatchSize, ct).ConfigureAwait(false);
    }

    /// <summary>Meloday's own radio for an artist mix or a station, kept while it plays so it never repeats itself.</summary>
    private RadioSession RadioFor(MediaItem mix) => _radios.GetOrAdd(mix.RatingKey, _ => mix switch
    {
        ArtistMix a => _local.ForArtist(a.Artist),
        Station s => _local.ForStation(s),
        _ => throw new ArgumentException("No radio for this mix.", nameof(mix)),
    });

    /// <summary>A built mix: the chosen artists' or albums' music, shuffled, a batch at a time.</summary>
    private async Task<IReadOnlyList<Track>> BuiltTracksAsync(PlexServerClient client, BuiltMix mix, DislikedSet disliked, CancellationToken ct)
    {
        if (_session.Section is not { } section) return [];
        var filter = mix.AlbumKeys.Count > 0
            ? new KeyValuePair<string, string?>("album.id", string.Join(',', mix.AlbumKeys))
            : new KeyValuePair<string, string?>("artist.id", string.Join(',', mix.ArtistKeys));
        var result = await client.GetTracksAsync(section.Key, 0, TagBatch, "random", ct, filter).ConfigureAwait(false);
        return result.Items.Where(disliked.Allows).Take(20).ToList();
    }

    /// <summary>A larger shuffled helping of a built mix (for keeping it as a playlist).</summary>
    public async Task<IReadOnlyList<Track>> SampleAsync(BuiltMix mix, int count)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        var filter = mix.AlbumKeys.Count > 0
            ? new KeyValuePair<string, string?>("album.id", string.Join(',', mix.AlbumKeys))
            : new KeyValuePair<string, string?>("artist.id", string.Join(',', mix.ArtistKeys));
        try
        {
            return (await client.GetTracksAsync(section.Key, 0, count, "random", default, filter).ConfigureAwait(false)).Items.Where(disliked.Allows).ToList();
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
        // No more than two tracks from one artist in a batch keeps a genre mix from turning into one artist.
        return result.Items.Where(disliked.Allows)
            .GroupBy(t => t.ArtistRatingKey ?? t.RatingKey).SelectMany(g => g.Take(2))
            .OrderBy(_ => Random.Shared.Next())
            .Take(20)
            .ToList();
    }

    /// <summary>
    /// A niche mix is an exact intersection of its dimensions. Within that boundary, it balances
    /// familiar music, older favourites and underplayed library tracks instead of drifting outward.
    /// </summary>
    private async Task<IReadOnlyList<Track>> NicheTracksAsync(PlexServerClient client, NicheMixRecipe mix, DislikedSet disliked, CancellationToken ct)
    {
        if (_session.Section is not { } section) return [];
        var filters = new List<KeyValuePair<string, string?>>(4);
        if (mix.Genre is { Length: > 0 } genre) filters.Add(new("genre", genre));
        if (mix.Mood is { Length: > 0 } mood) filters.Add(new("mood", mood));
        if (mix.Decade is { } decade)
        {
            // Plex's numeric comparisons are strict, so these bounds mean decade through decade+9.
            filters.Add(new("album.year>>", (decade - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)));
            filters.Add(new("album.year<<", (decade + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var result = await client.GetTracksAsync(section.Key, 0, NicheBatch, "random", ct, filters.ToArray()).ConfigureAwait(false);
        var pool = result.Items.Where(disliked.Allows).DistinctBy(t => t.RatingKey).ToList();
        if (pool.Count == 0) return [];

        var now = DateTimeOffset.Now;
        bool Familiar(Track t) => t.ViewCount > 0 && t.LastViewedAt is { } at && now - at <= TimeSpan.FromDays(45);
        bool Rediscovery(Track t) => t.ViewCount > 0 && !Familiar(t);
        double Score(Track t)
        {
            var score = Math.Log2(Math.Max(0, t.ViewCount) + 1) * 0.32
                + (t.UserRating ?? 0) * 0.08
                - Math.Min(6, Math.Max(0, t.SkipCount)) * 0.28;
            if (t.LastViewedAt is { } at) score += ListeningProfile.Weight(at, now) * 1.4;
            if (t.ViewCount == 0) score += 0.25;
            return score + Random.Shared.NextDouble() * 0.05;
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

        // Approximately 60% familiar, 27% rediscovery and 13% exploration when the pool supports it.
        TakeFrom(pool.Where(Familiar), 18);
        TakeFrom(pool.Where(Rediscovery), 8);
        TakeFrom(pool.Where(t => t.ViewCount == 0), 4);
        if (selected.Count < NicheTrackCount) TakeFrom(pool, NicheTrackCount - selected.Count);

        return SpreadNicheArtists(selected);
    }

    private static IReadOnlyList<Track> SpreadNicheArtists(IReadOnlyList<Track> tracks)
    {
        var remaining = tracks.OrderBy(_ => Random.Shared.Next()).ToList();
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

    private static string NicheArtistKey(Track track) => track.ArtistRatingKey ?? track.ArtistTitle ?? track.RatingKey;

    /// <summary>Forgets every mix (the library changed, or it's time to follow newer listening).</summary>
    public void Clear()
    {
        _mixes.Clear();
        _radios.Clear();
        _local.Clear();
        _list = null;
    }
}
