using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Taste;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class MadePlaylistService : IDisposable
{
    private const int Minimum = 300;
    private const int RecipesVersion = 4;
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMinutes(30);

    private sealed class State
    {
        public Dictionary<string, Edition> Editions { get; set; } = [];
        public Dictionary<string, string> PlexKeys { get; set; } = [];
        public HashSet<string> KeepOnPlex { get; set; } = [];
    }

    private sealed class Edition
    {
        public string Week { get; set; } = string.Empty;
        public int Recipes { get; set; }
        public DateTimeOffset BuiltAt { get; set; }
        public Dictionary<string, List<string>> Tracks { get; set; } = [];
    }

    private readonly PlexSession _session;
    private readonly MelodayEngine _engine;
    private readonly TasteService _taste;
    private readonly Dislikes _dislikes;
    private readonly BackgroundAudioAnalysis _analysis;
    private readonly ILogger<MadePlaylistService> _log;
    private readonly string _path = Path.Combine(AppPaths.Root, "made", "playlists.json");
    private readonly SemaphoreSlim _building = new(1, 1);
    private readonly SemaphoreSlim _syncing = new(1, 1);
    private readonly Timer _timer;
    private readonly Dictionary<string, IReadOnlyList<Track>> _resolved = new(StringComparer.Ordinal);
    private State _state;
    private IReadOnlyList<Playlist> _playlists = [];

    public MadePlaylistService(PlexSession session, MelodayEngine engine, TasteService taste, Dislikes dislikes, BackgroundAudioAnalysis analysis,
        ILogger<MadePlaylistService> log)
    {
        _analysis = analysis;
        _session = session;
        _engine = engine;
        _taste = taste;
        _dislikes = dislikes;
        _log = log;
        _state = Load();
        LoadGenres();
        _session.StateChanged += (_, _) => _ = EnsureAsync();
        var known = analysis.InventoryKnown;
        analysis.Changed += (_, _) =>
        {
            if (known || !analysis.InventoryKnown) return;
            known = true;
            _ = EnsureAsync();
        };
        _timer = new Timer(_ => _ = EnsureAsync(), null, TimeSpan.FromMinutes(1), CheckEvery);
        _ = EnsureAsync();
    }

    private void LoadGenres()
    {
        if (GenreEssentials.Catalogue.Count > 0) return;
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Genres", "genres.json");
            if (File.Exists(file)) GenreEssentials.Load(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.LogWarning(ex, "Genre Essentials: the genre covers couldn't be read");
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Playlist> Playlists => _playlists;

    public IReadOnlyList<Playlist> On(PlaylistShelf shelf) =>
        _playlists.Where(p => MadePlaylists.RecipeOf(p.RatingKey)?.Shelf == shelf).ToList();

    public string Status { get; private set; } = "Made by Meloday playlists appear once the Meloday Engine has analysed some of your library.";

    public bool Has(PlaylistRecipe recipe)
    {
        lock (_state) return _state.Editions.TryGetValue(Scope, out var edition) && edition.Tracks.ContainsKey(recipe.Id);
    }

    public static bool IsMade(MediaItem? item) => item is Playlist p && MadePlaylists.IsKey(p.RatingKey);

    private string Scope => $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}";

    public IReadOnlySet<string> ManagedPlexKeys
    {
        get
        {
            lock (_state) return _state.PlexKeys.Where(k => k.Key.StartsWith(Scope + "|", StringComparison.Ordinal)).Select(k => k.Value).ToHashSet();
        }
    }

    public IReadOnlyList<Playlist> WithoutCopies(IEnumerable<Playlist> playlists)
    {
        var managed = ManagedPlexKeys;
        return managed.Count == 0 ? playlists.ToList() : playlists.Where(p => !managed.Contains(p.RatingKey)).ToList();
    }

    public bool KeepsOnPlex(Playlist playlist)
    {
        lock (_state) return MadePlaylists.RecipeOf(playlist.RatingKey) is { } r && _state.KeepOnPlex.Contains(r.Id);
    }

    public async Task SetKeepOnPlexAsync(Playlist playlist, bool on)
    {
        if (MadePlaylists.RecipeOf(playlist.RatingKey) is not { } recipe) return;
        lock (_state)
        {
            if (on) _state.KeepOnPlex.Add(recipe.Id);
            else _state.KeepOnPlex.Remove(recipe.Id);
        }
        Save();
        if (on) await SyncAsync(recipe).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<Track>> TracksAsync(Playlist playlist, CancellationToken ct = default)
    {
        if (MadePlaylists.RecipeOf(playlist.RatingKey) is not { } recipe || _session.Client is not { } client) return [];
        List<string> keys;
        lock (_state)
        {
            if (!_state.Editions.TryGetValue(Scope, out var edition) || !edition.Tracks.TryGetValue(recipe.Id, out var found)) return [];
            keys = found;
        }
        var cacheKey = Scope + "|" + recipe.Id + "|" + string.Join(",", keys);
        lock (_resolved) if (_resolved.TryGetValue(cacheKey, out var known)) return known;
        var items = (await client.GetItemsAsync(keys, ct).ConfigureAwait(false)).OfType<Track>().ToDictionary(t => t.RatingKey, StringComparer.Ordinal);
        IReadOnlyList<Track> tracks = new SongSet().AddNew(keys.Select(k => items.GetValueOrDefault(k)).OfType<Track>());
        lock (_resolved) _resolved[cacheKey] = tracks;
        return tracks;
    }

    public async Task EnsureAsync(bool force = false)
    {
        if (!_session.IsReady || _session.Client is not { } client) return;
        if (!await _building.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            var now = DateTimeOffset.Now;
            var week = MadePlaylists.WeekOf(now).ToString("yyyy-MM-dd");
            var scope = Scope;
            Edition? edition;
            lock (_state) _state.Editions.TryGetValue(scope, out edition);
            if (force || edition is null || edition.Week != week || edition.Tracks.Count == 0 || edition.Recipes != RecipesVersion)
            {
                var made = await BuildAsync(client, now).ConfigureAwait(false);
                if (made is null)
                {
                    await ShowAsync(edition).ConfigureAwait(false);
                    return;
                }
                edition = made;
                lock (_state) _state.Editions[scope] = edition;
                Save();
                lock (_resolved) _resolved.Clear();
                _log.LogInformation("Made by Meloday: {Count} playlists for the week of {Week}", edition.Tracks.Count, week);
                await ShowAsync(edition).ConfigureAwait(false);
                List<string> kept;
                lock (_state) kept = [.. _state.KeepOnPlex];
                foreach (var id in kept)
                    if (MadePlaylists.RecipeOf(MadePlaylists.KeyPrefix + id) is { } recipe) await SyncAsync(recipe).ConfigureAwait(false);
                return;
            }
            if (_playlists.Count == 0) await ShowAsync(edition).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogInformation("Made by Meloday: not made this time ({Message})", ex.Message);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Made by Meloday: not made this time; tried again later");
        }
        finally
        {
            _building.Release();
        }
    }

    private async Task<Edition?> BuildAsync(PlexServerClient client, DateTimeOffset now)
    {
        if (_engine.Vocabulary is not { } vocabulary)
        {
            Status = "Made by Meloday playlists need the Meloday Engine's words, which couldn't be loaded.";
            return null;
        }
        if (!_analysis.InventoryKnown)
        {
            Status = "Made by Meloday playlists are being made once the Meloday Engine has read your library.";
            return null;
        }
        var snapshot = _engine.Snapshot();
        if (snapshot.Count < Minimum)
        {
            Status = _analysis.Learning
                ? $"Made by Meloday playlists appear once the Meloday Engine has analysed {Minimum:N0} of your songs ({snapshot.Count:N0} so far)."
                : $"Made by Meloday playlists need the Meloday Engine to know {Minimum:N0} of your songs ({snapshot.Count:N0} so far). Turn on learning under Settings > Audio Analysis.";
            return null;
        }
        var disliked = await _dislikes.GetAsync().ConfigureAwait(false);
        var calibration = await _engine.CalibrationAsync().ConfigureAwait(false);
        var taste = _taste.Engine;
        var library = await Task.Run(() =>
        {
            var vectors = snapshot.Values.ToDictionary(p => p.Track.RatingKey, p => p.Vector, StringComparer.Ordinal);
            return MelodayText.Dedupe(snapshot.Values.Select(p => p.Track).Where(disliked.Eligible))
                .Select(t => new PlaylistCandidate<Track>(t, t.RatingKey, t.ArtistTitle ?? t.DisplayArtist, vectors[t.RatingKey]))
                .ToList();
        }).ConfigureAwait(false);
        var week = MadePlaylists.WeekOf(now);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var edition = new Edition { Week = week.ToString("yyyy-MM-dd"), BuiltAt = now, Recipes = RecipesVersion };
        foreach (var recipe in MadePlaylists.Recipes)
        {
            var pool = await Task.Run(() => MadePlaylists.Pool(recipe, library, vocabulary, calibration, used)).ConfigureAwait(false);
            if (pool.Count < MadePlaylists.Smallest) continue;
            var fresh = (await client.GetItemsAsync(pool.Select(p => p.Candidate.Key)).ConfigureAwait(false))
                .OfType<Track>().ToDictionary(t => t.RatingKey, StringComparer.Ordinal);
            var current = pool
                .Select(p => (Candidate: p.Candidate with { Item = fresh.GetValueOrDefault(p.Candidate.Key) ?? p.Candidate.Item }, p.Fit))
                .Where(p => fresh.ContainsKey(p.Candidate.Key) && disliked.Eligible(p.Candidate.Item))
                .ToList();
            Func<Track, double> score = recipe.Hour is int hour
                ? t => taste.Score(t, new DateTimeOffset(now.Date.AddHours(hour), now.Offset))
                : t => taste.Like(t) + 1.2 * taste.Adventure * TasteEngine.Novelty(t, now) - Freshness.Penalty(t.LastViewedAt, now);
            var picked = MadePlaylists.Pick(current, score, MadePlaylists.Seed(recipe, week));
            if (picked.Count == 0) continue;
            edition.Tracks[recipe.Id] = picked.Select(t => t.RatingKey).ToList();
            used.UnionWith(edition.Tracks[recipe.Id]);
            _taste.Shown(picked);
        }
        await AddGenresAsync(client, edition, disliked.Eligible, now).ConfigureAwait(false);
        if (edition.Tracks.Count == 0)
        {
            Status = _analysis.Learning
                ? "The Meloday Engine hasn't found enough songs for any moment yet. It keeps learning in the background."
                : "The Meloday Engine hasn't found enough songs for any moment yet, and learning is turned off under Settings > Audio Analysis.";
            return null;
        }
        return edition;
    }

    private async Task AddGenresAsync(PlexServerClient client, Edition edition, Func<Track, bool> eligible, DateTimeOffset now)
    {
        if (GenreEssentials.Catalogue.Count == 0 || _session.Section is not { } section) return;
        try
        {
            var tags = await client.GetTagsAsync(section.Key, TagKind.Genre, PlexTypes.Track).ConfigureAwait(false);
            var matched = GenreEssentials.Match(GenreEssentials.Catalogue, tags);
            if (matched.Count == 0) return;
            var recent = (await client.GetTracksAsync(section.Key, 0, 500, "lastViewedAt:desc", default,
                new KeyValuePair<string, string?>("viewCount>>", "0")).ConfigureAwait(false)).Items;
            var played = ListeningProfile.Genres(recent, now).GroupBy(t => GenreEssentials.Normalize(t.Tag))
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Weight));
            var order = matched.OrderByDescending(m => played.GetValueOrDefault(GenreEssentials.Normalize(m.Tag.Title)))
                .ThenByDescending(m => m.Tag.Count ?? 0).ThenBy(m => m.Cover.Genre, StringComparer.Ordinal).ToList();
            var made = 0;
            foreach (var (cover, tag) in order)
            {
                if (made >= GenreEssentials.Count) break;
                var ranked = await client.GetTracksAsync(section.Key, 0, 100, "ratingCount:desc", default,
                    new KeyValuePair<string, string?>("genre", tag.FilterValue), new KeyValuePair<string, string?>("ratingCount>>", "0")).ConfigureAwait(false);
                var picked = GenreEssentials.Pick(ranked.Items.Where(eligible));
                if (picked.Count == 0) continue;
                edition.Tracks[GenreEssentials.IdPrefix + cover.Slug] = picked.Select(t => t.RatingKey).ToList();
                made++;
            }
            _log.LogInformation("Genre Essentials: {Made} of {Matched} genres with covers", made, matched.Count);
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation("Genre Essentials: not made this time ({Message})", ex.Message);
        }
    }

    private async Task<string?> CoverAsync(PlaylistRecipe recipe, IReadOnlyList<string> keys, HashSet<string> shown)
    {
        if (GenreEssentials.CoverOf(recipe.Id) is { } genre) return await GenreCoverArt.RenderAsync(genre, _log).ConfigureAwait(false);
        return await MadePlaylistCover.RenderAsync(recipe, await ArtistPhotoAsync(keys, shown).ConfigureAwait(false), _log).ConfigureAwait(false);
    }

    /// <summary>
    /// The photo for a playlist's cover: the first artist in it, in playlist order, who has a photo and isn't already on another
    /// playlist's cover this week (several playlists often start with the same favourite); the first with a photo if every one is.
    /// </summary>
    private async Task<CoverPhoto?> ArtistPhotoAsync(IReadOnlyList<string> keys, HashSet<string> shown)
    {
        if (_session.Client is not { } client || keys.Count == 0) return null;
        try
        {
            var first = keys.Take(30).ToList();
            var tracks = (await client.GetItemsAsync(first).ConfigureAwait(false)).OfType<Track>().ToDictionary(t => t.RatingKey, StringComparer.Ordinal);
            var ordered = first.Select(k => tracks.GetValueOrDefault(k)).OfType<Track>()
                .Where(t => t.ArtistThumb is { Length: > 0 } && !string.Equals(t.ArtistTitle, "Various Artists", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(t => t.ArtistRatingKey ?? t.ArtistThumb)
                .ToList();
            var artwork = App.Services.GetRequiredService<ArtworkService>();
            foreach (var track in ordered.Where(t => !shown.Contains(t.ArtistRatingKey ?? t.ArtistThumb!)).Concat(ordered))
            {
                if (await artwork.GetLocalPathAsync(track.ArtistThumb, 1000).ConfigureAwait(false) is not { } path) continue;
                var artist = track.ArtistRatingKey ?? track.ArtistThumb!;
                shown.Add(artist);
                return new CoverPhoto(artist, path);
            }
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogInformation("Made by Meloday: no artist photo for a cover this time ({Message})", ex.Message);
        }
        return null;
    }

    private async Task ShowAsync(Edition? edition)
    {
        if (edition is null) return;
        var playlists = new List<Playlist>();
        var recipes = MadePlaylists.Recipes.Concat(edition.Tracks.Keys.Select(GenreEssentials.RecipeOf).OfType<PlaylistRecipe>());
        var shown = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recipe in recipes)
        {
            if (!edition.Tracks.TryGetValue(recipe.Id, out var keys) || keys.Count == 0) continue;
            var cover = await CoverAsync(recipe, keys, shown).ConfigureAwait(false);
            playlists.Add(Describe(recipe, keys.Count, cover, edition.BuiltAt));
        }
        _playlists = playlists;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static Playlist Describe(PlaylistRecipe recipe, int count, string? cover, DateTimeOffset builtAt) => new()
    {
        RatingKey = MadePlaylists.KeyFor(recipe),
        Title = recipe.Title,
        Summary = recipe.Shelf == PlaylistShelf.Genre
            ? $"{recipe.Description} Most popular first, two songs per artist at most. New every Monday."
            : $"{recipe.Description} Made by Meloday from how your music sounds and what you love. New every Monday.",
        TrackCount = count,
        Thumb = cover,
        UpdatedAt = builtAt,
        SubtitleOverride = recipe.Shelf == PlaylistShelf.Genre ? "Genre Essentials" : "Made by Meloday",
    };

    private async Task SyncAsync(PlaylistRecipe recipe)
    {
        if (_session.Client is not { } client) return;
        await _syncing.WaitAsync().ConfigureAwait(false);
        try
        {
            var playlist = _playlists.FirstOrDefault(p => p.RatingKey == MadePlaylists.KeyFor(recipe));
            if (playlist is null) return;
            var tracks = await TracksAsync(playlist).ConfigureAwait(false);
            if (tracks.Count == 0) return;
            var plexKey = Scope + "|" + recipe.Id;
            string? saved;
            lock (_state) _state.PlexKeys.TryGetValue(plexKey, out saved);
            var key = await PlexPlaylistSync.SyncAsync(client, saved, recipe.Title, playlist.Summary ?? recipe.Description, tracks,
                playlist.Thumb ?? await CoverAsync(recipe, tracks.Select(t => t.RatingKey).ToList(), []).ConfigureAwait(false)).ConfigureAwait(false);
            if (key is not null && key != saved)
            {
                lock (_state) _state.PlexKeys[plexKey] = key;
                Save();
            }
        }
        catch (Exception ex) when (ex is PlexException or IOException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Made by Meloday: {Playlist} couldn't be updated on Plex", recipe.Title);
        }
        finally
        {
            _syncing.Release();
        }
    }

    private State Load()
    {
        try
        {
            return File.Exists(_path) ? JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State() : new State();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new State();
        }
    }

    private void Save()
    {
        string json;
        lock (_state) json = JsonSerializer.Serialize(_state);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Made by Meloday: couldn't save the playlists");
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _building.Dispose();
        _syncing.Dispose();
    }
}
