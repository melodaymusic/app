using HumeMusic.Core.Mixes;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Radio;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>Plex's stations for the library, and its groups of tag stations (genres, styles, moods, decades).</summary>
public sealed record StationCatalog(IReadOnlyList<Station> Stations, IReadOnlyList<StationDirectory> Directories)
{
    public static StationCatalog Empty { get; } = new([], []);
}

/// <summary>Who a station pictures (the artist played most in it) and who it features.</summary>

/// <summary>The listener's own favourite tag stations: their top genres, styles, moods and decades.</summary>
public sealed record StationPicks(IReadOnlyList<Station> Genres, IReadOnlyList<Station> Styles, IReadOnlyList<Station> Moods, IReadOnlyList<Station> Decades)
{
    public static StationPicks Empty { get; } = new([], [], [], []);

    public IReadOnlyList<Station> For(StationKind kind) => kind switch
    {
        StationKind.Genre => Genres,
        StationKind.Style => Styles,
        StationKind.Mood => Moods,
        StationKind.Decade => Decades,
        _ => [],
    };
}

/// <summary>
/// The live stations (Headliners, Hits, Club and Chill, made by Meloday from the library, then Library
/// Radio and Deep Cuts, which Plex Pass serves from Plex and Meloday otherwise makes), and Plex's
/// groups of tag stations (every genre, style, mood and decade's radio, a Plex Pass feature: without
/// it, or where Plex has none, Meloday's own stand in, see <see cref="LocalRadio"/>). Also
/// the listener's top four of each kind of tag station, from what they play most, each led by the
/// artists they play most in it. Meloday's two mix builders need no Plex Pass.
/// </summary>
public sealed class StationService
{
    private const int PicksPerKind = 6;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
    private static readonly HashSet<string> NoiseMoods = new(StringComparer.OrdinalIgnoreCase) { "ArtistPopular" };

    /// <summary>Meloday's mix builders (shown as the Radio page's buttons).</summary>
    public static IReadOnlyList<Station> Builders { get; } =
    [
        new Station
        {
            RatingKey = "builder:artists", Title = "Artist Radio Builder", StationKind = StationKind.ArtistMixBuilder,
            Summary = "Pick up to ten artists and hear them together.",
        },
        new Station
        {
            RatingKey = "builder:albums", Title = "Album Radio Builder", StationKind = StationKind.AlbumMixBuilder,
            Summary = "Pick up to ten albums and hear them together.",
        },
    ];

    private readonly PlexSession _session;
    private readonly PlexPass _pass;
    private readonly ILogger _log;
    private Task<StationCatalog>? _catalog;
    private Task<StationPicks>? _picks;
    private DateTime _loadedAt;
    private string? _section;
    private readonly Dictionary<string, Task<IReadOnlyList<Station>>> _directories = [];
    private readonly Dictionary<string, Task<string?>> _covers = [];
    // Artists already on a board, per kind of station, so a row doesn't picture one artist twice.
    private readonly Dictionary<StationKind, HashSet<string>> _pictured = [];
    private readonly SemaphoreSlim _coverGate = new(4, 4);

    public StationService(PlexSession session, PlexPass pass, ILogger<StationService> log)
    {
        _session = session;
        _pass = pass;
        _log = log;
    }

    /// <summary>Whether the listener has Plex's stations at all (Plex Pass).</summary>
    public bool IsAvailable => _pass.IsAvailable;

    public Task<StationCatalog> GetCatalogAsync()
    {
        EnsureFresh();
        return _catalog ??= LoadCatalogAsync();
    }

    public Task<StationPicks> GetPicksAsync()
    {
        EnsureFresh();
        return _picks ??= LoadPicksAsync();
    }

    private void EnsureFresh()
    {
        var section = _session.Section?.Key;
        if (section == _section && DateTime.UtcNow - _loadedAt < Lifetime && _catalog is not { IsFaulted: true } && _picks is not { IsFaulted: true }) return;
        _section = section;
        _loadedAt = DateTime.UtcNow;
        _catalog = null;
        _picks = null;
        _directories.Clear();
    }

    /// <summary>
    /// The live stations: Meloday's own that the library can fill (One, Hits, Club, Chill), then Library Radio and
    /// Deep Cuts (Plex's with Plex Pass), and Plex's station groups. Meloday is a playlist, not a station: it lives
    /// on Made for You.
    /// </summary>
    private async Task<StationCatalog> LoadCatalogAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return StationCatalog.Empty;
        var available = await AvailableAsync(client, section.Key).ConfigureAwait(false);
        var ours = StationBrand.Ours.Where(b => available.Contains(b.Kind)).Select(b => b.ToStation()).ToList();
        if (!_pass.IsAvailable) return Local(ours);
        try
        {
            var (stations, directories) = await client.GetStationsAsync(section.Key).ConfigureAwait(false);
            // Plex offered none (its stations need analysis it hasn't done): Meloday's own stand in.
            if (stations.Count == 0 && directories.Count == 0) return Local(ours);
            // Plex's Time Travel and Random Album give way to Hits and the album-based radios.
            var plex = stations.Where(s => s.StationKind is StationKind.Library or StationKind.DeepCuts).Select(Describe);
            return new StationCatalog(ours.Concat(plex).ToList(), directories);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Stations unavailable: {Message}", ex.Message);
            return new StationCatalog(ours, []);
        }
    }

    private const string LocalDirectoryPrefix = "local:";

    /// <summary>The live stations this library can fill (all of them when it can't be asked).</summary>
    private async Task<IReadOnlySet<StationKind>> AvailableAsync(PlexServerClient client, string sectionKey)
    {
        try
        {
            var genres = client.GetTagsAsync(sectionKey, TagKind.Genre, PlexTypes.Track);
            var moods = client.GetTagsAsync(sectionKey, TagKind.Mood, PlexTypes.Artist);
            var decades = client.GetTagsAsync(sectionKey, TagKind.Decade, PlexTypes.Album);
            await Task.WhenAll(genres, moods, decades).ConfigureAwait(false);
            return StationVibes.Available(genres.Result.Select(t => t.Title), moods.Result.Select(t => t.Title), decades.Result.Select(t => t.FilterValue));
        }
        catch (PlexException ex)
        {
            _log.LogInformation("The library's genres couldn't be read for the live stations: {Message}", ex.Message);
            return StationVibes.Everything;
        }
    }

    /// <summary>
    /// Meloday's own stations, for when Plex has none: Library Radio and Deep Cuts after the live four, and every genre,
    /// style, mood and decade to browse, all made from the library by <see cref="LocalRadio"/>.
    /// </summary>
    private static StationCatalog Local(IReadOnlyList<Station> ours) => new(
        ours.Concat(
        [
            StationBrand.For(StationKind.Library)!.ToStation(),
            StationBrand.For(StationKind.DeepCuts)!.ToStation(),
        ]).ToList(),
        [
            new StationDirectory("Genre Radio", StationKind.Genre, LocalDirectoryPrefix + "genre"),
            new StationDirectory("Style Radio", StationKind.Style, LocalDirectoryPrefix + "style"),
            new StationDirectory("Mood Radio", StationKind.Mood, LocalDirectoryPrefix + "mood"),
            new StationDirectory("Decade Radio", StationKind.Decade, LocalDirectoryPrefix + "decade"),
        ]);

    private static Station Describe(Station s) => s.Summary is not null ? s : new Station
    {
        RatingKey = s.RatingKey,
        Title = s.Title,
        StationKind = s.StationKind,
        Key = s.Key,
        Tag = s.Tag,
        Thumb = s.Thumb,
        Summary = s.StationKind switch
        {
            StationKind.Library => "Your whole library as one endless station.",
            StationKind.DeepCuts => "The songs you rarely reach for, from artists you love.",
            _ => null,
        },
    };

    /// <summary>Every station in a group (e.g. all of Genre Radio), for its full list.</summary>
    public Task<IReadOnlyList<Station>> GetDirectoryAsync(StationDirectory directory)
    {
        EnsureFresh();
        if (!_directories.TryGetValue(directory.Key, out var load) || load.IsFaulted)
            _directories[directory.Key] = load = LoadDirectoryAsync(directory);
        return load;
    }

    private async Task<IReadOnlyList<Station>> LoadDirectoryAsync(StationDirectory directory)
    {
        if (directory.Key.StartsWith(LocalDirectoryPrefix, StringComparison.Ordinal)) return await LoadLocalDirectoryAsync(directory).ConfigureAwait(false);
        if (!_pass.IsAvailable || _session.Client is not { } client) return [];
        try
        {
            return await client.GetStationDirectoryAsync(directory.Key).ConfigureAwait(false);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("{Directory} unavailable: {Message}", directory.Title, ex.Message);
            _directories.Remove(directory.Key);
            return [];
        }
    }

    /// <summary>Every genre, style, mood or decade the library has, most music first, as a Meloday station.</summary>
    private async Task<IReadOnlyList<Station>> LoadLocalDirectoryAsync(StationDirectory directory)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        try
        {
            var (kind, type) = directory.Kind switch
            {
                StationKind.Genre => (TagKind.Genre, PlexTypes.Track),
                StationKind.Style => (TagKind.Style, PlexTypes.Artist),
                StationKind.Mood => (TagKind.Mood, PlexTypes.Artist),
                _ => (TagKind.Decade, PlexTypes.Album),
            };
            var tags = await client.GetTagsAsync(section.Key, kind, type).ConfigureAwait(false);
            var families = Core.Artwork.MelodayPalette.Families;
            var made = 0;
            return tags
                .Where(t => !NoiseMoods.Contains(t.Title) && !string.IsNullOrWhiteSpace(t.FilterValue))
                .OrderByDescending(t => t.Count ?? 0)
                .Take(120)
                .Select(t => new Station
                {
                    RatingKey = $"station:local:{directory.Kind.ToString().ToLowerInvariant()}/{t.FilterValue}",
                    Title = t.Title + " Radio",
                    StationKind = directory.Kind,
                    Tag = t.FilterValue,
                    IsLocal = true,
                    Colour = families[(made++ * 7 + 3) % families.Count].Name,
                })
                .ToList();
        }
        catch (PlexException ex)
        {
            _log.LogInformation("{Directory} unavailable: {Message}", directory.Title, ex.Message);
            _directories.Remove(directory.Key);
            return [];
        }
    }

    // ------------------------------------------------------------------ the listener's picks

    /// <summary>
    /// Top genres from the tracks played most (Plex's genre stations follow track genres); top
    /// styles and moods from the artists played most; top decades from the albums played most.
    /// </summary>
    private async Task<StationPicks> LoadPicksAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return StationPicks.Empty;
        var sk = section.Key;
        // Without Plex Pass these play from Meloday's own radio; with it, from Plex's.
        var local = !_pass.IsAvailable;
        KeyValuePair<string, string?> Played() => new("viewCount>>", "0");
        try
        {
            var tracksTask = client.GetTracksAsync(sk, 0, 400, "viewCount:desc", default, Played());
            var artistsTask = client.GetArtistsAsync(sk, 0, 60, "viewCount:desc", default, Played());
            var albumsTask = client.GetAlbumsAsync(sk, 0, 300, "viewCount:desc", default, Played());
            var genreIdsTask = client.GetTagsAsync(sk, TagKind.Genre, PlexTypes.Track);
            await Task.WhenAll(tracksTask, artistsTask, albumsTask, genreIdsTask).ConfigureAwait(false);

            // Artists' styles and moods come with their full metadata, fetched together.
            var artistKeys = artistsTask.Result.Items.Select(a => a.RatingKey).ToList();
            var full = (await client.GetItemsAsync(artistKeys).ConfigureAwait(false)).OfType<Artist>()
                .ToDictionary(a => a.RatingKey);
            var artists = artistsTask.Result.Items.Select(a => full.GetValueOrDefault(a.RatingKey) ?? a).ToList();
            var plays = artistsTask.Result.Items.ToDictionary(a => a.RatingKey, a => a.ViewCount);

            var genreIds = genreIdsTask.Result
                .GroupBy(t => t.Title, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().FilterValue, StringComparer.OrdinalIgnoreCase);
            var genres = TasteRanking.Rank(tracksTask.Result.Items,
                t => t.Genres.Where(genreIds.ContainsKey).Select(g => (genreIds[g], g)),
                t => Math.Max(1, t.ViewCount), PicksPerKind);
            var styles = TasteRanking.Rank(artists, a => a.StyleRefs.Select(r => (r.Id, r.Title)), a => plays.GetValueOrDefault(a.RatingKey), PicksPerKind);
            var moods = TasteRanking.Rank(artists, a => a.MoodRefs.Select(r => (r.Id, r.Title)), a => plays.GetValueOrDefault(a.RatingKey), PicksPerKind,
                ignore: NoiseMoods.Contains);
            var decades = TasteRanking.Rank(albumsTask.Result.Items,
                a => a.Year is int y && y > 1900 ? new[] { ((y / 10 * 10).ToString(), $"{y / 10 * 10}s") } : Array.Empty<(string, string)>(),
                a => Math.Max(1, a.ViewCount), PicksPerKind);

            // Every pick its own colour, spread across the palette, so no row repeats one. The picture,
            // an artist from the station, is chosen when the board is shown (see GetCoverAsync).
            var families = Core.Artwork.MelodayPalette.Families;
            var made = 0;

            Station Make(StationKind kind, int number, string id, string title)
            {
                var colour = families[(made++ * 7 + 3) % families.Count].Name;
                var guidKind = kind.ToString().ToLowerInvariant();
                return new Station
                {
                    RatingKey = local ? $"station:local:{guidKind}/{id}" : $"station:tv.plex://station/{guidKind}/{id}",
                    Title = title + " Radio",
                    StationKind = kind,
                    Key = local ? null : $"/library/sections/{sk}/stations/{number}/{id}",
                    Tag = id,
                    Colour = colour,
                    IsLocal = local,
                };
            }

            return new StationPicks(
                genres.Select(g => Make(StationKind.Genre, 5, g.Id, g.Title)).ToList(),
                styles.Select(st => Make(StationKind.Style, 7, st.Id, st.Title)).ToList(),
                moods.Select(m => Make(StationKind.Mood, 7, m.Id, m.Title)).ToList(),
                decades.Select(d => Make(StationKind.Decade, 6, d.Id, d.Title)).ToList());
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Station picks unavailable: {Message}", ex.Message);
            _picks = null;
            return StationPicks.Empty;
        }
    }

    /// <summary>
    /// The picture for a station: a random artist from it (never "Various Artists" or another
    /// placeholder), preferring one not already pictured on another station of its kind.
    /// </summary>
    public Task<string?> GetCoverAsync(Station station)
    {
        if (station.Tag is not { } tag || station.Key is null && !station.IsLocal) return Task.FromResult<string?>(null);
        var key = station.RatingKey;
        if (!_covers.TryGetValue(key, out var load) || load.IsFaulted) _covers[key] = load = LoadCoverAsync(station.StationKind, tag);
        return load;
    }

    private async Task<string?> LoadCoverAsync(StationKind kind, string tag)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return null;
        await _coverGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var sk = section.Key;
            using var showing = PlexServerClient.ForShowing();
            KeyValuePair<string, string?> F(string field) => new(field, tag);
            // Genre and decade stations are made of tracks and albums: their artists are the ones credited.
            IEnumerable<(string Name, string? Thumb)> people = kind switch
            {
                StationKind.Genre => (await client.GetTracksAsync(sk, 0, 24, "random", default, F("genre")).ConfigureAwait(false)).Items
                    .Select(t => (t.ArtistTitle ?? string.Empty, t.ArtistThumb)),
                StationKind.Style => (await client.GetArtistsAsync(sk, 0, 12, "random", default, F("style")).ConfigureAwait(false)).Items
                    .Select(a => (a.Title, a.Thumb)),
                StationKind.Mood => (await client.GetArtistsAsync(sk, 0, 12, "random", default, F("mood")).ConfigureAwait(false)).Items
                    .Select(a => (a.Title, a.Thumb)),
                StationKind.Decade => (await client.GetAlbumsAsync(sk, 0, 24, "random", default, F("decade")).ConfigureAwait(false)).Items
                    .Select(a => (a.ArtistTitle ?? string.Empty, a.ArtistThumb)),
                _ => [],
            };
            var candidates = people
                .Where(p => p.Thumb is not null && !string.IsNullOrWhiteSpace(p.Name) && !ListeningProfile.IsPlaceholderArtist(p.Name))
                .DistinctBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (candidates.Count == 0) return null;
            lock (_pictured)
            {
                if (!_pictured.TryGetValue(kind, out var pictured)) _pictured[kind] = pictured = new(StringComparer.OrdinalIgnoreCase);
                var fresh = candidates.Where(c => !pictured.Contains(c.Name)).ToList();
                var pool = fresh.Count > 0 ? fresh : candidates;
                var pick = pool[Random.Shared.Next(pool.Count)];
                pictured.Add(pick.Name);
                return pick.Thumb;
            }
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Station artist unavailable: {Message}", ex.Message);
            return null;
        }
        finally
        {
            _coverGate.Release();
        }
    }

    /// <summary>Forgets the stations (the library changed).</summary>
    public void Clear()
    {
        _covers.Clear();
        lock (_pictured) _pictured.Clear();
        _catalog = null;
        _picks = null;
        _directories.Clear();
    }
}
