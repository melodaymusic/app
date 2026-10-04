using System.Collections.Concurrent;
using System.Text.Json;
using HumeMusic.Core;
using HumeMusic.Core.Meloday;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Security;
using HumeMusic.Core.Services;
using HumeMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>A Meloday: the mix for one part of one day, with its name, description and cover.</summary>
public sealed record MelodayMix(string Title, string Description, MelodayPeriod Period, DateTime PeriodStart, DateTime MadeAt,
    IReadOnlyList<Track> Tracks, string? CoverPath);

/// <summary>
/// Meloday, built into the app: for each part of the day it makes a 50-track mix from what the
/// listener plays at that time (the last 60 days, weighted to this hour and weekday), the tracks
/// sonically closest to those, and a themed filler, then orders it as a sonic gradient and names it.
/// A mix lasts until its part of the day ends. Everything the script did, with the app's own
/// dislikes (one star or less) and without writing to Plex unless the listener asks for a playlist.
/// </summary>
public sealed class MelodayService
{
    /// <summary>The station card and page stand for Meloday itself; its title is the current mix's.</summary>
    public static Station Station { get; } = new()
    {
        RatingKey = "meloday",
        Title = "Meloday",
        StationKind = StationKind.Meloday,
        Colour = "Persimmon",
        Summary = "A mix for this part of your day, made from what you play at this time.",
    };

    // Tracks played this recently are left out; Meloday's own picks rest for UsedRestDays.
    private const int ExcludePlayedDays = 9;
    private const int UsedRestDays = 12;
    private const int UsedKeepDays = 30;
    private const int SonicLimit = 10;
    private const double SonicDistance = 1.0;
    private const int FillerPoolSize = 1000;
    // An invisible character at the front of the Plex playlist's title sorts it to the top (as the script does).
    private const string InvisiblePrefix = "ㅤ";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly PlexSession _session;
    private readonly PlexPass _pass;
    private readonly Dislikes _dislikes;
    private readonly SettingsStore _settings;
    private readonly ISecretStore _aiKey;
    private readonly OpenAiClient _openAi;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, AlbumTags> _albums = new(StringComparer.Ordinal);
    private readonly SonicCache _sonic;
    private Timer? _timer;
    private readonly object _gate = new();
    private Task<MelodayMix?>? _making;
    private Dictionary<string, string[]>? _moodMap;
    private Dictionary<string, string[]>? _eraMap;

    public MelodayService(PlexSession session, PlexPass pass, Dislikes dislikes, SettingsStore settings, ISecretStore aiKey, ILogger<MelodayService> log)
    {
        _session = session;
        _pass = pass;
        _dislikes = dislikes;
        _settings = settings;
        _aiKey = aiKey;
        _log = log;
        // A client of its own for OpenAI: the key never shares a client (or a log line) with Plex.
        _openAi = new OpenAiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _sonic = new SonicCache(Path.Combine(AppPaths.Meloday, "sonic.json"), log);
        // As soon as the library is ready (Home's own first requests go a moment ahead), so Meloday is
        // ready to play by the time anyone opens it.
        _session.StateChanged += (_, _) => Schedule(StartDelay);
        Schedule(StartDelay);
    }

    // ------------------------------------------------------------------ ready ahead of time

    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Meloday is made ahead of time, as the script ran on a schedule: as the app starts, once the
    /// library is ready (unless this part of the day already has one) and again as each part of the day begins,
    /// so opening or playing it never waits.
    /// </summary>
    private void Schedule(TimeSpan delay)
    {
        _timer?.Dispose();
        _timer = null;
        if (!_session.IsReady) return;
        _timer = new Timer(_ => Prepare(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private async void Prepare()
    {
        try
        {
            await GetAsync().ConfigureAwait(false);
        }
        // In the background, nothing may escape: a failed attempt waits for the next part of the day.
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Meloday: couldn't prepare the mix ahead of time");
        }
        var now = DateTime.Now;
        // A little after the hour, so the new part of the day's plays are counted.
        Schedule(MelodayPeriods.At(now).NextChange(now) - now + TimeSpan.FromMinutes(1));
    }

    /// <summary>A new mix is ready (or making one failed).</summary>
    public event EventHandler? Changed;

    /// <summary>Whether keeping Meloday on Plex was switched on or off, or an update to the playlist began or ended.</summary>
    public event EventHandler? PlexSyncChanged;

    private bool _plexSyncing;
    private string? _plexError;

    /// <summary>Whether Meloday is kept on the Plex server as one playlist, updated with each new mix.</summary>
    public bool KeepOnPlex => _settings.Current.MelodayPlexPlaylist;

    /// <summary>Switches keeping Meloday on Plex on or off; on, the Meloday of now goes straight to the server.</summary>
    public async Task SetKeepOnPlexAsync(bool on)
    {
        if (_settings.Current.MelodayPlexPlaylist == on) return;
        _settings.Current.MelodayPlexPlaylist = on;
        _settings.Save();
        PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        if (on) await KeepCurrentOnPlexAsync().ConfigureAwait(false);
    }

    /// <summary>A line about the Plex playlist for the Meloday page: empty when it isn't kept there.</summary>
    public string PlexStatus
    {
        get
        {
            if (!KeepOnPlex) return string.Empty;
            if (_plexSyncing) return "Updating your Plex playlist…";
            if (_plexError is { } error) return $"Couldn't update your Plex playlist ({error}). It tries again with the next Meloday.";
            var state = LoadState();
            return state.PlexSyncedAt is { } at
                ? $"Kept on Plex as one playlist, with its cover and description · updated {at:t}. It updates itself with each new Meloday while Meloday Music is open."
                : "Kept on Plex as one playlist, with its cover and description. It updates itself with each new Meloday while Meloday Music is open.";
        }
    }

    public MelodayMix? Current { get; private set; }

    public bool IsMaking => _making is { IsCompleted: false };

    public bool HasAiKey => _aiKey.HasSecret;

    // ------------------------------------------------------------------ the current mix

    /// <summary>
    /// The mix for this part of the day: the one already made, the one saved from earlier today, or
    /// a new one. <paramref name="fresh"/> makes a new one regardless.
    /// </summary>
    public Task<MelodayMix?> GetAsync(bool fresh = false)
    {
        lock (_gate)
        {
            if (_making is { IsCompleted: false } making) return making;
            var now = DateTime.Now;
            if (!fresh && Current is { } current && IsCurrent(current.PeriodStart, now)) return Task.FromResult<MelodayMix?>(current);
            _making = Task.Run(() => fresh ? MakeAsync() : LoadOrMakeAsync());
            return _making;
        }
    }

    private bool IsCurrent(DateTime periodStart, DateTime now) => MelodayPeriods.At(now).StartOf(now) == periodStart;

    private async Task<MelodayMix?> LoadOrMakeAsync()
    {
        var state = LoadState();
        if (state.Mix is { } saved && _session.Server?.MachineIdentifier == saved.Server && _session.Section?.Key == saved.Section
            && IsCurrent(saved.PeriodStart, DateTime.Now) && MelodayPeriods.Named(saved.Period) is { } period && _session.Client is { } client)
        {
            try
            {
                var tracks = await TracksAsync(client, saved.Keys, default).ConfigureAwait(false);
                if (tracks.Count > 0)
                {
                    var cover = saved.Cover is { } path && File.Exists(path) ? path : null;
                    var mix = new MelodayMix(saved.Title, saved.Description, period, saved.PeriodStart, saved.MadeAt, tracks, cover);
                    // Kept on Plex, but this mix never reached the server (the app was closed, or it failed): now.
                    if (_settings.Current.MelodayPlexPlaylist && state.PlexSyncedFor != saved.PeriodStart) _ = KeepOnPlexAsync(client, mix, default);
                    return Publish(mix);
                }
            }
            catch (PlexException ex)
            {
                _log.LogInformation("Saved Meloday unavailable: {Message}", ex.Message);
            }
        }
        return await MakeAsync().ConfigureAwait(false);
    }

    private MelodayMix? Publish(MelodayMix? mix)
    {
        if (mix is not null) Current = mix;
        Changed?.Invoke(this, EventArgs.Empty);
        return mix;
    }

    // ------------------------------------------------------------------ making a mix

    private async Task<MelodayMix?> MakeAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return Publish(null);
        try
        {
            var mix = await BuildAsync(client, section, CancellationToken.None).ConfigureAwait(false);
            return Publish(mix);
        }
        catch (Exception ex) when (ex is PlexException or IOException or JsonException or HttpRequestException)
        {
            _log.LogWarning(ex, "Meloday couldn't make a mix");
            return Publish(null);
        }
    }

    private sealed class Run
    {
        public required PlexServerClient Client { get; init; }
        public required string Section { get; init; }
        public required DislikedSet Disliked { get; init; }
        public required IReadOnlyCollection<string> Skipped { get; init; }
        public required HashSet<string> Excluded { get; init; }
        public required HashSet<string> Used { get; init; }
        public required DateTimeOffset ExcludeSince { get; init; }
        /// <summary>Each track's sonic neighbours (rating keys, nearest first), from the sonic cache or Plex.</summary>
        public ConcurrentDictionary<string, IReadOnlyList<string>> Similar { get; } = new(StringComparer.Ordinal);
        /// <summary>Tracks already fetched in this run, by rating key.</summary>
        public ConcurrentDictionary<string, Track> Known { get; } = new(StringComparer.Ordinal);
        public List<Track>? FillerPool { get; set; }
    }

    private async Task<MelodayMix?> BuildAsync(PlexServerClient client, MusicSection section, CancellationToken ct)
    {
        var now = DateTime.Now;
        var period = MelodayPeriods.At(now);
        var random = Random.Shared;
        _log.LogInformation("Meloday: making the {Period} mix", period.Name);

        var usedLog = LoadUsed(now);
        var run = new Run
        {
            Client = client,
            Section = section.Key,
            Disliked = await _dislikes.GetAsync().ConfigureAwait(false),
            Skipped = _settings.Current.MelodaySkipGenres.Where(g => !string.IsNullOrWhiteSpace(g)).Select(g => g.Trim()).ToList(),
            Used = usedLog.Where(e => e.Date > now.AddDays(-UsedRestDays)).Select(e => e.RatingKey).ToHashSet(StringComparer.Ordinal),
            Excluded = new HashSet<string>(StringComparer.Ordinal),
            ExcludeSince = new DateTimeOffset(now.AddDays(-ExcludePlayedDays)),
        };

        // The listener's own plays (the owner's, on their own server), newest first.
        var account = _session.Server?.Owned == true ? "1" : null;
        var plays = await client.GetHistoryAsync(section.Key, new DateTimeOffset(now.AddDays(-MelodayProfile.LookbackDays)), account, ct: ct).ConfigureAwait(false);
        foreach (var p in plays.Where(p => p.ViewedAt >= run.ExcludeSince)) run.Excluded.Add(p.RatingKey);
        await LoadAlbumsAsync(client, plays.Select(p => p.AlbumKey), ct).ConfigureAwait(false);

        var profile = MelodayProfile.Build(period, plays, run.Excluded, run.Used, Album, now);
        _log.LogInformation("Meloday: {Scored} tracks scored, {Defining} defining; top genre {Genre}, mood {Mood}",
            profile.TrackAffinity.Count, profile.DefiningKeys.Count, profile.DominantGenre, profile.DominantMood);

        var historical = await HistoricalAsync(run, plays, profile, random, ct).ConfigureAwait(false);

        // Seeds: the defining tracks, topped up to 30% of the mix from the rest of the history pick.
        var defining = profile.DefiningKeys.ToHashSet(StringComparer.Ordinal);
        var seeds = historical.Where(t => defining.Contains(t.RatingKey)).ToList();
        var others = historical.Where(t => !defining.Contains(t.RatingKey)).OrderBy(_ => random.Next()).ToList();
        seeds.AddRange(others.Take(Math.Max(0, (int)(MelodayCurator.MaxTracks * MelodayCurator.HistoricalRatio) - seeds.Count)));

        var similar = await SimilarAsync(run, seeds, ct).ConfigureAwait(false);
        var final = Theme(run, MelodayText.Dedupe(seeds.Concat(similar)), profile);

        // Top up from more of the history and from the neighbours of what is already in.
        for (var attempt = 0; attempt < 3 && final.Count < MelodayCurator.MaxTracks; attempt++)
        {
            var before = final.Count;
            var leftover = MelodayCurator.MaxTracks - final.Count;
            var moreHistory = (await HistoricalAsync(run, plays, profile, random, ct).ConfigureAwait(false)).OrderBy(_ => random.Next()).Take(leftover);
            var moreSimilar = await SimilarAsync(run, final, ct).ConfigureAwait(false);
            var have = final.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
            var extra = Theme(run, MelodayText.Dedupe(moreHistory.Concat(moreSimilar).Where(t => !have.Contains(t.RatingKey))), profile);
            final = MelodayText.Dedupe(final.Concat(extra.Take(leftover)));
            if (final.Count == before) break;
        }
        final = final.Where(t => !run.Used.Contains(t.RatingKey)).ToList();

        var ordered = await PathfindAsync(run, final.Take(MelodayCurator.MaxTracks).ToList(), ct).ConfigureAwait(false);

        var name = MelodayNaming.Name(period, ordered, Album, MoodMap(), EraMap(), random, now, ListenerName());
        var title = await AiTitleAsync(name, period, now, ct).ConfigureAwait(false) ?? name.Title;

        // Fill to 50 with library tracks in the theme, then more loosely.
        foreach (var (genre, mood) in new (string?, string?)[] { (name.DominantGenre, name.DominantMood), (name.DominantGenre, null), (null, null) })
        {
            if (ordered.Count >= MelodayCurator.MaxTracks) break;
            var need = MelodayCurator.MaxTracks - ordered.Count;
            var fillers = await FillerAsync(run, need * 2, ordered.Select(t => t.RatingKey), genre, mood, random, ct).ConfigureAwait(false);
            ordered.AddRange(fillers.Take(need));
        }
        ordered = MelodayText.Dedupe(ordered);

        // No more than three tracks per artist, topped up again if that leaves gaps.
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var capped = MelodayCurator.CapArtists(ordered, MelodayCurator.ArtistLimit, counts);
        if (capped.Count < MelodayCurator.MaxTracks)
        {
            var need = MelodayCurator.MaxTracks - capped.Count;
            var fillers = await FillerAsync(run, need * 2, capped.Select(t => t.RatingKey), name.DominantGenre, name.DominantMood, random, ct).ConfigureAwait(false);
            capped.AddRange(MelodayCurator.CapArtists(fillers, MelodayCurator.ArtistLimit, counts).Take(need));
        }
        ordered = MelodayText.Dedupe(capped).Take(MelodayCurator.MaxTracks).ToList();
        ordered = await PathfindAsync(run, ordered, ct).ConfigureAwait(false);
        ordered = MelodayCurator.SpreadArtists(ordered);
        // The last word: nothing rated one star or less, however it got in.
        ordered = ordered.Where(run.Disliked.Allows).ToList();
        if (ordered.Count == 0) return null;

        var cover = await MelodayCover.RenderAsync(period, title, now, _log).ConfigureAwait(false);
        var mix = new MelodayMix(title, name.Description, period, period.StartOf(now), now, ordered, cover);
        SaveUsed(usedLog, mix, now);
        _sonic.Save();
        var state = LoadState();
        state.Mix = new SavedMix
        {
            Server = client.Server.MachineIdentifier,
            Section = section.Key,
            Period = period.Name,
            PeriodStart = mix.PeriodStart,
            MadeAt = now,
            Title = title,
            Description = name.Description,
            Keys = ordered.Select(t => t.RatingKey).ToList(),
            Cover = cover,
        };
        SaveState(state);
        _log.LogInformation("Meloday: \"{Title}\" with {Count} tracks", title, ordered.Count);

        if (_settings.Current.MelodayPlexPlaylist) await KeepOnPlexAsync(client, mix, ct).ConfigureAwait(false);
        return mix;
    }

    /// <summary>
    /// History tracks for the period: a weighted pick of the tracks with positive affinity (or of
    /// every scored track, when none qualify), falling back to recently added music, then any.
    /// </summary>
    private async Task<List<Track>> HistoricalAsync(Run run, IReadOnlyList<HistoryPlay> plays, PeriodProfile profile, Random random, CancellationToken ct)
    {
        bool Open(string key) => !run.Excluded.Contains(key) && !run.Used.Contains(key);
        var affinity = profile.TrackAffinity.Where(a => a.Value > 0 && Open(a.Key)).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);
        if (affinity.Count == 0)
            affinity = profile.TrackAffinity.Where(a => Open(a.Key)).ToDictionary(a => a.Key, a => Math.Max(a.Value, 0.01), StringComparer.Ordinal);
        var candidates = plays.Select(p => p.RatingKey).Where(affinity.ContainsKey).Distinct().ToList();
        var playCounts = plays.GroupBy(p => p.RatingKey).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        List<Track> tracks;
        if (candidates.Count > 0)
        {
            var keys = MelodayCurator.SampleHistory(candidates, profile, playCounts, affinity, random);
            tracks = await TracksAsync(run.Client, keys, ct).ConfigureAwait(false);
        }
        else
        {
            var recent = await run.Client.GetTracksAsync(run.Section, 0, MelodayCurator.MaxTracks * 2, "addedAt:desc", ct).ConfigureAwait(false);
            tracks = recent.Items.Where(t => !run.Used.Contains(t.RatingKey)).ToList();
            if (tracks.Count == 0)
                tracks = (await run.Client.GetTracksAsync(run.Section, 0, MelodayCurator.MaxTracks, "random", ct).ConfigureAwait(false)).Items.ToList();
        }
        await LoadAlbumsAsync(run.Client, tracks.Select(t => t.AlbumRatingKey), ct).ConfigureAwait(false);
        tracks = tracks.Where(t => Allowed(run, t)).ToList();
        return MelodayCurator.CapGenre(tracks, t => Album(t)?.Genres ?? (IReadOnlyCollection<string>)t.Genres.ToList());
    }

    /// <summary>The tracks sonically nearest the given ones (Plex Pass), not played lately and not used lately.</summary>
    private async Task<List<Track>> SimilarAsync(Run run, IReadOnlyList<Track> references, CancellationToken ct)
    {
        if (!_pass.IsAvailable || references.Count == 0) return [];
        await NearestAsync(run, references, ct).ConfigureAwait(false);
        var seen = references.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var reference in references)
            foreach (var key in run.Similar.GetValueOrDefault(reference.RatingKey) ?? [])
                if (seen.Add(key) && !run.Excluded.Contains(key) && !run.Used.Contains(key)) keys.Add(key);

        // Neighbours known from the sonic cache arrive as keys: their tracks are fetched together.
        var missing = keys.Where(k => !run.Known.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            foreach (var t in await TracksAsync(run.Client, missing, ct).ConfigureAwait(false)) run.Known[t.RatingKey] = t;
        var found = keys.Select(k => run.Known.GetValueOrDefault(k)).OfType<Track>()
            .Where(t => t.LastViewedAt is not { } played || played < run.ExcludeSince)
            .ToList();
        await LoadAlbumsAsync(run.Client, found.Select(t => t.AlbumRatingKey), ct).ConfigureAwait(false);
        return MelodayText.Dedupe(found).Where(t => Allowed(run, t)).ToList();
    }

    /// <summary>
    /// Sonic neighbours for tracks not yet known: from the sonic cache (a week, as in the script) or
    /// from Plex, eight at a time. Plex's sonic searches are the slow part of making a mix.
    /// </summary>
    private async Task NearestAsync(Run run, IEnumerable<Track> tracks, CancellationToken ct)
    {
        var server = run.Client.Server.MachineIdentifier;
        var missing = new List<string>();
        foreach (var key in tracks.Select(t => t.RatingKey).Distinct().Where(k => !run.Similar.ContainsKey(k)))
        {
            if (_sonic.TryGet(server, key) is { } cached) run.Similar[key] = cached;
            else missing.Add(key);
        }
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (key, token) =>
        {
            try
            {
                var near = await run.Client.GetSonicallySimilarAsync<Track>(key, SonicLimit, SonicDistance, token).ConfigureAwait(false);
                foreach (var t in near) run.Known[t.RatingKey] = t;
                var keys = near.Select(t => t.RatingKey).ToList();
                run.Similar[key] = keys;
                _sonic.Set(server, key, keys);
            }
            catch (PlexException)
            {
                run.Similar[key] = [];
            }
        }).ConfigureAwait(false);
    }

    /// <summary>Orders the tracks into a sonic gradient (Plex Pass); without sonic analysis they keep their order.</summary>
    private async Task<List<Track>> PathfindAsync(Run run, List<Track> tracks, CancellationToken ct)
    {
        if (!_pass.IsAvailable || tracks.Count < 2) return tracks;
        await NearestAsync(run, tracks, ct).ConfigureAwait(false);
        var similar = tracks.DistinctBy(t => t.RatingKey).ToDictionary(t => t.RatingKey,
            t => run.Similar.GetValueOrDefault(t.RatingKey) ?? [], StringComparer.Ordinal);
        return MelodayCurator.Pathfind(tracks, similar);
    }

    /// <summary>Tracks from across the library, in the mix's genre and mood when given (the script's three-sort filler pool).</summary>
    private async Task<List<Track>> FillerAsync(Run run, int limit, IEnumerable<string> have, string? genre, string? mood, Random random, CancellationToken ct)
    {
        if (run.FillerPool is null)
        {
            var pool = new List<Track>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var perSort = Math.Max(FillerPoolSize / 3, MelodayCurator.MaxTracks * 3);
            foreach (var sort in new[] { "addedAt:desc", "lastViewedAt:desc", "titleSort" })
            {
                var batch = await run.Client.GetTracksAsync(run.Section, 0, perSort, sort, ct).ConfigureAwait(false);
                pool.AddRange(batch.Items.Where(t => seen.Add(t.RatingKey)));
                if (pool.Count >= FillerPoolSize) break;
            }
            await LoadAlbumsAsync(run.Client, pool.Select(t => t.AlbumRatingKey), ct).ConfigureAwait(false);
            run.FillerPool = MelodayText.Dedupe(pool).Where(t => Allowed(run, t)).ToList();
        }
        var taken = have.ToHashSet(StringComparer.Ordinal);
        var candidates = run.FillerPool.Where(t => !taken.Contains(t.RatingKey) && !run.Excluded.Contains(t.RatingKey) && !run.Used.Contains(t.RatingKey)).ToList();
        if (genre is not null && candidates.Where(t => Tags(t).Genres.Any(g => g.Equals(genre, StringComparison.OrdinalIgnoreCase))).ToList() is { Count: > 0 } byGenre)
            candidates = byGenre;
        if (mood is not null && candidates.Where(t => Tags(t).Moods.Any(m => m.Equals(mood, StringComparison.OrdinalIgnoreCase))).ToList() is { Count: > 0 } byMood)
            candidates = byMood;
        return candidates.OrderBy(_ => random.Next()).Take(limit).ToList();
    }

    private List<Track> Theme(Run run, IEnumerable<Track> tracks, PeriodProfile profile) =>
        MelodayCurator.BiasToTheme(tracks.Where(t => Allowed(run, t)), profile.DominantGenre, profile.DominantMood, Album);

    /// <summary>Not disliked (track, album or artist at one star or less) and not in a skipped genre.</summary>
    private bool Allowed(Run run, Track t) =>
        run.Disliked.Allows(t) && !MelodayCurator.IsSkipped(t.Genres.Concat(Album(t)?.Genres ?? (IEnumerable<string>)[]), run.Skipped);

    private AlbumTags? Album(Track t) => t.AlbumRatingKey is { } key ? Album(key) : null;

    private AlbumTags? Album(string key) => _albums.GetValueOrDefault(key);

    private AlbumTags Tags(Track t) => Album(t) ?? new AlbumTags(t.Genres.ToHashSet(), t.Moods.ToHashSet(), t.Year);

    /// <summary>Genres, moods and year for albums not yet known (fetched together, a hundred at a time).</summary>
    private async Task LoadAlbumsAsync(PlexServerClient client, IEnumerable<string?> keys, CancellationToken ct)
    {
        var missing = keys.OfType<string>().Distinct().Where(k => !_albums.ContainsKey(k)).ToList();
        await Parallel.ForEachAsync(missing.Chunk(100), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (chunk, token) =>
        {
            try
            {
                foreach (var album in (await client.GetItemsAsync(chunk, token).ConfigureAwait(false)).OfType<Album>())
                    _albums[album.RatingKey] = new AlbumTags(album.Genres.ToHashSet(StringComparer.Ordinal), album.Moods.ToHashSet(StringComparer.Ordinal), album.Year);
            }
            catch (PlexException ex)
            {
                _log.LogDebug("Album tags unavailable: {Message}", ex.Message);
            }
            foreach (var key in chunk) _albums.TryAdd(key, AlbumTags.Empty);
        }).ConfigureAwait(false);
    }

    /// <summary>Tracks for rating keys, in the keys' order (fetched together, a hundred at a time).</summary>
    private static async Task<List<Track>> TracksAsync(PlexServerClient client, IReadOnlyList<string> keys, CancellationToken ct)
    {
        var found = new ConcurrentDictionary<string, Track>(StringComparer.Ordinal);
        await Parallel.ForEachAsync(keys.Distinct().Chunk(100), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (chunk, token) =>
        {
            foreach (var t in (await client.GetItemsAsync(chunk, token).ConfigureAwait(false)).OfType<Track>()) found[t.RatingKey] = t;
        }).ConfigureAwait(false);
        return keys.Distinct().Where(found.ContainsKey).Select(k => found[k]).ToList();
    }

    private string ListenerName()
    {
        var account = _session.Account;
        var name = !string.IsNullOrWhiteSpace(account?.DisplayName) ? account!.DisplayName! : account?.Username;
        return string.IsNullOrWhiteSpace(name) ? "you" : name.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
    }

    // ------------------------------------------------------------------ AI titles (optional)

    private async Task<string?> AiTitleAsync(MelodayName name, MelodayPeriod period, DateTime now, CancellationToken ct)
    {
        if (!_settings.Current.MelodayAiTitles || _aiKey.Load() is not { Length: > 0 } key) return null;
        var avoid = _settings.Current.MelodayAvoidWords.Where(w => !string.IsNullOrWhiteSpace(w)).Select(w => w.Trim()).ToList();
        var (result, text) = await _openAi.AnswerAsync(key, MelodayNaming.AiPrompt(name, period, now, avoid), ct).ConfigureAwait(false);
        if (result != OpenAiResult.Ok)
        {
            _log.LogWarning("Meloday: the AI title wasn't available ({Result}); using Meloday's own", result);
            return null;
        }
        return MelodayNaming.FromAi(text, period, now, avoid);
    }

    /// <summary>Checks the key with OpenAI, then keeps it encrypted.</summary>
    public async Task<OpenAiResult> SaveAiKeyAsync(string key)
    {
        key = key.Trim();
        var result = await _openAi.CheckKeyAsync(key).ConfigureAwait(false);
        if (result == OpenAiResult.Ok) _aiKey.Save(key);
        return result;
    }

    public void RemoveAiKey() => _aiKey.Clear();

    // ------------------------------------------------------------------ the Plex playlist (optional)

    /// <summary>
    /// Keeps the mix as a playlist on the server (only with the setting on): the same playlist each
    /// time, its tracks replaced (new ones added before old ones go, so it is never empty), renamed,
    /// described and given the mix's cover.
    /// </summary>
    private async Task KeepOnPlexAsync(PlexServerClient client, MelodayMix mix, CancellationToken ct)
    {
        _plexSyncing = true;
        _plexError = null;
        PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            var state = LoadState();
            var title = InvisiblePrefix + mix.Title;
            var keys = mix.Tracks.Select(t => t.RatingKey).ToList();
            Playlist? playlist = state.PlexPlaylistKey is { } saved ? await client.GetPlaylistAsync(saved, ct).ConfigureAwait(false) : null;
            if (playlist is null)
            {
                playlist = await client.CreatePlaylistAsync(title, keys, ct).ConfigureAwait(false);
                if (playlist is null) return;
            }
            else
            {
                var current = new List<Track>();
                for (var start = 0; ; start += 200)
                {
                    var page = await client.GetPlaylistItemsAsync(playlist.RatingKey, start, 200, ct).ConfigureAwait(false);
                    current.AddRange(page.Items);
                    if (page.Items.Count < 200) break;
                }
                var have = current.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
                var wanted = keys.ToHashSet(StringComparer.Ordinal);
                var add = keys.Where(k => !have.Contains(k)).ToList();
                if (add.Count > 0) await client.AddToPlaylistAsync(playlist.RatingKey, add, ct).ConfigureAwait(false);
                foreach (var t in current.Where(t => !wanted.Contains(t.RatingKey) && t.PlaylistItemId is not null))
                    await client.RemovePlaylistItemAsync(playlist.RatingKey, t.PlaylistItemId!, ct).ConfigureAwait(false);
            }
            // Remembered at once: whatever goes wrong next, the next one updates this playlist, never a second.
            state.PlexPlaylistKey = playlist.RatingKey;
            SaveState(state);
            await client.EditPlaylistAsync(playlist.RatingKey, title, mix.Description, ct).ConfigureAwait(false);
            await SetCoverAsync(client, playlist.RatingKey, mix.CoverPath, ct).ConfigureAwait(false);
            state = LoadState();
            state.PlexSyncedFor = mix.PeriodStart;
            state.PlexSyncedAt = DateTime.Now;
            SaveState(state);
        }
        catch (Exception ex) when (ex is PlexException or IOException)
        {
            _log.LogWarning("Meloday: the Plex playlist wasn't updated: {Message}", ex.Message);
            _plexError = ex is PlexException ? "Plex couldn't be reached or refused it" : ex.Message;
        }
        finally
        {
            _plexSyncing = false;
            PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The mix's cover becomes the playlist's own (so it shows in Plex and its apps, not a collage).</summary>
    private async Task SetCoverAsync(PlexServerClient client, string playlistKey, string? cover, CancellationToken ct)
    {
        if (cover is null || !File.Exists(cover))
        {
            _log.LogInformation("Meloday: no cover to give the Plex playlist");
            return;
        }
        try
        {
            await client.SetPlaylistCoverAsync(playlistKey, await File.ReadAllBytesAsync(cover, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            _log.LogInformation("Meloday: the Plex playlist has its cover");
        }
        catch (Exception ex) when (ex is PlexException or IOException)
        {
            _log.LogWarning("Meloday: the Plex playlist's cover wasn't set: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Keeps the Meloday for now on Plex as a playlist with its cover (the setting was just turned on, so
    /// it doesn't wait for the next part of the day).
    /// </summary>
    public async Task KeepCurrentOnPlexAsync()
    {
        var mix = await GetAsync().ConfigureAwait(false);
        if (mix is null || _session.Client is not { } client || !_settings.Current.MelodayPlexPlaylist) return;
        await KeepOnPlexAsync(client, mix, default).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ files

    private sealed class SavedMix
    {
        public string Server { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty;
        public string Period { get; set; } = string.Empty;
        public DateTime PeriodStart { get; set; }
        public DateTime MadeAt { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Keys { get; set; } = [];
        public string? Cover { get; set; }
    }

    private sealed class SavedState
    {
        public SavedMix? Mix { get; set; }
        public string? PlexPlaylistKey { get; set; }
        /// <summary>The part of the day whose mix is on Plex now, and when it got there.</summary>
        public DateTime? PlexSyncedFor { get; set; }
        public DateTime? PlexSyncedAt { get; set; }
    }

    /// <summary>A track Meloday used, and when (the script's playlist log).</summary>
    private sealed class UsedTrack
    {
        public DateTime Date { get; set; }
        public string Playlist { get; set; } = string.Empty;
        public string RatingKey { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
    }

    /// <summary>
    /// Sonic neighbours kept for a week (the script's sonic cache): Plex's sonic searches take up to
    /// seconds each, and the tracks that define a part of the day come back day after day.
    /// </summary>
    private sealed class SonicCache(string path, ILogger log)
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
        private readonly object _gate = new();
        private Dictionary<string, Entry>? _entries;
        private bool _dirty;

        public sealed class Entry
        {
            public DateTime At { get; set; }
            public List<string> Keys { get; set; } = [];
        }

        private Dictionary<string, Entry> Entries()
        {
            if (_entries is not null) return _entries;
            try
            {
                _entries = File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                log.LogInformation("Meloday: sonic cache unreadable, starting afresh: {Message}", ex.Message);
            }
            _entries ??= new Dictionary<string, Entry>(StringComparer.Ordinal);
            return _entries;
        }

        public IReadOnlyList<string>? TryGet(string server, string key)
        {
            lock (_gate)
                return Entries().TryGetValue($"{server}|{key}", out var e) && DateTime.Now - e.At < Lifetime ? e.Keys : null;
        }

        public void Set(string server, string key, List<string> keys)
        {
            lock (_gate)
            {
                Entries()[$"{server}|{key}"] = new Entry { At = DateTime.Now, Keys = keys };
                _dirty = true;
            }
        }

        public void Save()
        {
            lock (_gate)
            {
                if (!_dirty || _entries is null) return;
                foreach (var old in _entries.Where(e => DateTime.Now - e.Value.At >= Lifetime).Select(e => e.Key).ToList()) _entries.Remove(old);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    var temp = path + ".tmp";
                    File.WriteAllText(temp, JsonSerializer.Serialize(_entries));
                    File.Move(temp, path, overwrite: true);
                    _dirty = false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.LogInformation("Meloday: sonic cache not saved: {Message}", ex.Message);
                }
            }
        }
    }

    private static string StatePath => Path.Combine(AppPaths.Meloday, "state.json");
    private static string UsedPath => Path.Combine(AppPaths.Meloday, "used.json");

    private SavedState LoadState() => Read<SavedState>(StatePath) ?? new SavedState();

    private void SaveState(SavedState state) => Write(StatePath, state);

    private List<UsedTrack> LoadUsed(DateTime now) =>
        (Read<List<UsedTrack>>(UsedPath) ?? []).Where(e => e.Date > now.AddDays(-UsedKeepDays)).ToList();

    private void SaveUsed(List<UsedTrack> log, MelodayMix mix, DateTime now)
    {
        log.AddRange(mix.Tracks.Select(t => new UsedTrack { Date = now, Playlist = mix.Title, RatingKey = t.RatingKey, Title = t.Title, Artist = t.ArtistTitle }));
        Write(UsedPath, log);
    }

    private T? Read<T>(string path) where T : class
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning("Meloday: {File} unreadable, starting afresh: {Message}", Path.GetFileName(path), ex.Message);
            return null;
        }
    }

    private void Write<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("Meloday: couldn't save {File}: {Message}", Path.GetFileName(path), ex.Message);
        }
    }

    private Dictionary<string, string[]> MoodMap() => _moodMap ??= Map("moodmap.json");

    private Dictionary<string, string[]> EraMap() => _eraMap ??= Map("eramap.json");

    private Dictionary<string, string[]> Map(string file)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Meloday", file);
            return JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.LogWarning("Meloday: {File} unavailable: {Message}", file, ex.Message);
            return [];
        }
    }
}
