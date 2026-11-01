using System.Collections.Concurrent;
using System.Text.Json;
using MelodayMusic.Core;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Mixes;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Security;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using MelodayMusic.Core.Taste;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record MelodayMix(string Title, string Description, MelodayPeriod Period, DateTime PeriodStart, DateTime MadeAt,
    IReadOnlyList<Track> Tracks, string? CoverPath);

public sealed class MelodayService
{
    public static Station Station { get; } = new()
    {
        RatingKey = "meloday",
        Title = "Meloday",
        StationKind = StationKind.Meloday,
        Colour = "Persimmon",
        Summary = "A mix for this part of your day, made from what you play at this time.",
    };

    private const int ExcludePlayedDays = 9;
    private const int UsedRestDays = 12;
    private const int UsedKeepDays = 30;
    private const int SonicLimit = 10;
    private const double SonicDistance = 1.0;
    private const int FillerPoolSize = 1000;
    private const string InvisiblePrefix = "ㅤ";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly PlexSession _session;
    private readonly MusicIntelligence _intelligence;
    private readonly Dislikes _dislikes;
    private readonly SettingsStore _settings;
    private readonly DupeStore _dupes;
    private readonly ISecretStore _aiKey;
    private readonly OpenAiClient _openAi;
    private readonly ILogger _log;
    private readonly ConcurrentDictionary<string, AlbumTags> _albums = new(StringComparer.Ordinal);
    private readonly SonicCache _sonic;
    private Timer? _timer;
    private int _preparing;
    public void CatchUp() => Schedule(TimeSpan.FromSeconds(2));
    private void ResetEngine()
    {
        lock (_gate)
        {
            _scopeLifetime.Cancel(); _scopeLifetime = new();
            _currentScope = Scope; Current = null; _making = null;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        CatchUp();
    }
    private readonly object _gate = new();
    private readonly SemaphoreSlim _plexSyncGate = new(1, 1);
    private Task<MelodayMix?>? _making;
    private string? _currentScope;
    private readonly AsyncLocal<string?> _storageScope = new();
    private CancellationTokenSource _scopeLifetime = new();
    private string AccountLibraryScope => $"{_session.Account?.Id ?? _session.Account?.Username}|{_session.Server?.MachineIdentifier}|{_session.Section?.Key}";
    private string Scope => $"{AccountLibraryScope}|{_intelligence.Kind}";
    private readonly Core.Mutes.MuteStore _mutes;
    private Dictionary<string, string[]>? _moodMap;
    private Dictionary<string, string[]>? _eraMap;

    private readonly TasteService _taste;

    public MelodayService(PlexSession session, MusicIntelligence intelligence, Dislikes dislikes, SettingsStore settings, DupeStore dupes, Core.Mutes.MuteStore mutes, ISecretStore aiKey,
        TasteService taste, ILogger<MelodayService> log)
    {
        _taste = taste;
        _dupes = dupes;
        _mutes = mutes;
        _session = session;
        _intelligence = intelligence;
        intelligence.Changed += (_, _) => ResetEngine();
        _dislikes = dislikes;
        _settings = settings;
        _aiKey = aiKey;
        _log = log;
        _openAi = new OpenAiClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) });
        _sonic = new SonicCache(Path.Combine(AppPaths.Meloday, "sonic.json"), log);
        _session.StateChanged += (_, _) =>
        {
            lock (_gate)
            {
                if (_session.State == SessionState.SignedOut || _session.IsReady && _currentScope != Scope)
                {
                    _scopeLifetime.Cancel(); _scopeLifetime = new();
                    _currentScope = _session.IsReady ? Scope : null;
                    Current = null; _making = null; _albums.Clear();
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            Schedule(StartDelay);
        };
        Schedule(StartDelay);
    }

    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(2);

    private void Schedule(TimeSpan delay)
    {
        _timer?.Dispose();
        _timer = null;
        if (!_session.IsReady) return;
        _timer = new Timer(_ => Prepare(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private async void Prepare()
    {
        if (Interlocked.Exchange(ref _preparing, 1) != 0) { Schedule(TimeSpan.FromMinutes(1)); return; }
        var retry = false;
        try
        {
            var made = await GetAsync().ConfigureAwait(false);
            retry = made is null || !IsCurrent(made.PeriodStart, DateTime.Now);
            var state = LoadState();
            if (_settings.Current.MelodayPlexPlaylist && Current is { } current && _session.Client is { } client
                && (state.PlexSyncedFor != current.PeriodStart || state.PlexCoverVersion != PlexCoverVersion || _plexError is not null))
                await KeepOnPlexAsync(client, current, default).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Meloday: couldn't prepare the mix ahead of time");
        }
        var now = DateTime.Now;
        var delay = MelodayPeriods.At(now).NextChange(now) - now + TimeSpan.FromMinutes(1);
        Interlocked.Exchange(ref _preparing, 0);
        retry |= Current is null || !IsCurrent(Current.PeriodStart, now);
        Schedule((retry || _plexError is not null) && delay > TimeSpan.FromMinutes(5) ? TimeSpan.FromMinutes(5) : delay);
    }

    public event EventHandler? Changed;

    public event EventHandler? PlexSyncChanged;

    private bool _plexSyncing;
    private string? _plexError;

    public bool KeepOnPlex => _settings.Current.MelodayPlexPlaylist;

    public async Task SetKeepOnPlexAsync(bool on)
    {
        if (_settings.Current.MelodayPlexPlaylist == on) return;
        _settings.Current.MelodayPlexPlaylist = on;
        _settings.Save();
        PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        if (on) await KeepCurrentOnPlexAsync().ConfigureAwait(false);
    }

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

    public Task<MelodayMix?> GetAsync(bool fresh = false)
    {
        lock (_gate)
        {
            if (!_session.IsReady) return Task.FromResult<MelodayMix?>(null);
            if (_making is { IsCompleted: false } making) return making;
            var now = DateTime.Now;
            if (!fresh && Current is { } current && IsCurrent(current.PeriodStart, now)) return Task.FromResult<MelodayMix?>(current);
            var scope = Scope;
            var ct = _scopeLifetime.Token;
            _making = Task.Run(async () =>
            {
                _storageScope.Value = scope;
                try { return fresh ? await MakeAsync(ct).ConfigureAwait(false) : await LoadOrMakeAsync(ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
            });
            return _making;
        }
    }

    private bool IsCurrent(DateTime periodStart, DateTime now) => MelodayPeriods.At(now).StartOf(now) == periodStart;

    private async Task<MelodayMix?> LoadOrMakeAsync(CancellationToken ct)
    {
        var state = LoadState();
        if (state.Mix is { } saved && _session.Server?.MachineIdentifier == saved.Server && _session.Section?.Key == saved.Section
            && IsCurrent(saved.PeriodStart, DateTime.Now) && MelodayPeriods.Named(saved.Period) is { } period && _session.Client is { } client)
        {
            try
            {
                var tracks = (await TracksAsync(client, saved.Keys, ct).ConfigureAwait(false))
                    .Where(t => !_mutes.IsMuted(saved.Server, t.RatingKey, t.ArtistRatingKey)).ToList();
                if (tracks.Count > 0)
                {
                    var cover = saved.Cover is { } path && Path.GetFileName(path).StartsWith(MelodayCover.FilePrefix, StringComparison.Ordinal) && File.Exists(path) ? path : await MelodayCover.RenderAsync(period, saved.Title, saved.MadeAt, _log).ConfigureAwait(false);
                    if (saved.Cover != cover) { saved.Cover = cover; SaveState(state); }
                    var mix = new MelodayMix(saved.Title, saved.Description, period, saved.PeriodStart, saved.MadeAt, tracks, cover);
                    ct.ThrowIfCancellationRequested();
                    if (_storageScope.Value != Scope) return null;
                    return Publish(mix);
                }
            }
            catch (PlexException ex)
            {
                _log.LogInformation("Saved Meloday unavailable: {Message}", ex.Message);
            }
        }
        return await MakeAsync(ct).ConfigureAwait(false);
    }

    private async Task<List<Track>> ApplyDupesAsync(PlexServerClient client, List<Track> tracks, CancellationToken ct)
    {
        var map = _dupes.Map(client.Server.MachineIdentifier);
        if (map.Count == 0 || !tracks.Any(t => map.ContainsKey(t.RatingKey))) return tracks;
        var keys = Dupes.Collapse(tracks.Select(t => t.RatingKey), map);
        var have = tracks.ToDictionary(t => t.RatingKey, StringComparer.Ordinal);
        var missing = keys.Where(k => !have.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            foreach (var t in (await client.GetItemsAsync(missing, ct).ConfigureAwait(false)).OfType<Track>()) have[t.RatingKey] = t;
        return keys.Where(have.ContainsKey).Select(k => have[k]).ToList();
    }

    public async Task ApplyDupesToCurrentAsync()
    {
        if (Current is not { } mix || _session.Client is not { } client) return;
        var tracks = await ApplyDupesAsync(client, mix.Tracks.ToList(), default).ConfigureAwait(false);
        Current = mix with { Tracks = tracks };
        var state = LoadState();
        if (state.Mix is { } saved && saved.PeriodStart == mix.PeriodStart)
        {
            saved.Keys = tracks.Select(t => t.RatingKey).ToList();
            SaveState(state);
        }
    }

    public int PruneMuted()
    {
        if (Current is not { } mix || _session.Server?.MachineIdentifier is not { } server) return 0;
        var kept = mix.Tracks.Where(t => !_mutes.IsMuted(server, t.RatingKey, t.ArtistRatingKey)).ToList();
        if (kept.Count == mix.Tracks.Count || kept.Count == 0) return 0;
        Current = mix with { Tracks = kept };
        var state = LoadState();
        if (state.Mix is { } saved && saved.PeriodStart == mix.PeriodStart)
        {
            saved.Keys = kept.Select(t => t.RatingKey).ToList();
            SaveState(state);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return mix.Tracks.Count - kept.Count;
    }

    private MelodayMix? Publish(MelodayMix? mix)
    {
        if (mix is not null) Current = mix;
        Changed?.Invoke(this, EventArgs.Empty);
        return mix;
    }

    private async Task<MelodayMix?> MakeAsync(CancellationToken ct)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return Publish(null);
        try
        {
            var mix = await BuildAsync(client, section, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (_storageScope.Value != Scope) return null;
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
        public ConcurrentDictionary<string, IReadOnlyList<string>> Similar { get; } = new(StringComparer.Ordinal);
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

        var account = _session.Server?.Owned == true ? "1" : null;
        var plays = await client.GetHistoryAsync(section.Key, new DateTimeOffset(now.AddDays(-MelodayProfile.LookbackDays)), account, ct: ct).ConfigureAwait(false);
        foreach (var p in plays.Where(p => p.ViewedAt >= run.ExcludeSince)) run.Excluded.Add(p.RatingKey);
        await LoadAlbumsAsync(client, plays.Select(p => p.AlbumKey), ct).ConfigureAwait(false);

        var taste = _taste.Engine;
        double Completion(string key)
        {
            var seen = taste.Memory?.Track(key) ?? default;
            return seen.IsEmpty ? 1.0 : Math.Clamp(1.0 - 0.8 * seen.SkipRisk + 0.2 * seen.Warmth, 0.2, 1.2);
        }
        var profile = MelodayProfile.Build(period, plays, run.Excluded, run.Used, Album, now, Completion);
        _log.LogInformation("Meloday: {Scored} tracks scored, {Defining} defining; top genre {Genre}, mood {Mood}",
            profile.TrackAffinity.Count, profile.DefiningKeys.Count, profile.DominantGenre, profile.DominantMood);

        var historical = await HistoricalAsync(run, plays, profile, random, ct).ConfigureAwait(false);

        var defining = profile.DefiningKeys.ToHashSet(StringComparer.Ordinal);
        var seeds = historical.Where(t => defining.Contains(t.RatingKey)).ToList();
        var others = Freshness.WeightedOrder(historical.Where(t => !defining.Contains(t.RatingKey)), t => t.LastViewedAt, now, random, taste.Like).ToList();
        seeds.AddRange(others.Take(Math.Max(0, (int)(MelodayCurator.MaxTracks * MelodayCurator.HistoricalRatio) - seeds.Count)));

        var similar = await SimilarAsync(run, seeds, ct).ConfigureAwait(false);
        var final = Theme(run, MelodayText.Dedupe(seeds.Concat(similar)), profile);

        for (var attempt = 0; attempt < 3 && final.Count < MelodayCurator.MaxTracks; attempt++)
        {
            var before = final.Count;
            var leftover = MelodayCurator.MaxTracks - final.Count;
            var moreHistory = Freshness.WeightedOrder(await HistoricalAsync(run, plays, profile, random, ct).ConfigureAwait(false), t => t.LastViewedAt, now, random, taste.Like).Take(leftover);
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

        foreach (var (genre, mood) in new (string?, string?)[] { (name.DominantGenre, name.DominantMood), (name.DominantGenre, null), (null, null) })
        {
            if (ordered.Count >= MelodayCurator.MaxTracks) break;
            var need = MelodayCurator.MaxTracks - ordered.Count;
            var fillers = await FillerAsync(run, need * 2, ordered.Select(t => t.RatingKey), genre, mood, random, ct).ConfigureAwait(false);
            ordered.AddRange(fillers.Take(need));
        }
        ordered = MelodayText.Dedupe(ordered);

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
        ordered = await ApplyDupesAsync(client, ordered, ct).ConfigureAwait(false);
        ordered = ordered.Where(run.Disliked.Allows).ToList();
        if (ordered.Count == 0) return null;

        var cover = await MelodayCover.RenderAsync(period, title, now, _log).ConfigureAwait(false);
        var mix = new MelodayMix(title, name.Description, period, period.StartOf(now), now, ordered, cover);
        ct.ThrowIfCancellationRequested();
        if (_storageScope.Value != Scope || _session.Client != client) return null;
        SaveUsed(usedLog, mix, now);
        _taste.Shown(ordered);
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

    private async Task<List<Track>> SimilarAsync(Run run, IReadOnlyList<Track> references, CancellationToken ct)
    {
        if (!_intelligence.IsAvailable || references.Count == 0) return [];
        await NearestAsync(run, references, ct).ConfigureAwait(false);
        var seen = references.Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var reference in references)
            foreach (var key in run.Similar.GetValueOrDefault(reference.RatingKey) ?? [])
                if (seen.Add(key) && !run.Excluded.Contains(key) && !run.Used.Contains(key)) keys.Add(key);

        var missing = keys.Where(k => !run.Known.ContainsKey(k)).ToList();
        if (missing.Count > 0)
            foreach (var t in await TracksAsync(run.Client, missing, ct).ConfigureAwait(false)) run.Known[t.RatingKey] = t;
        var found = keys.Select(k => run.Known.GetValueOrDefault(k)).OfType<Track>()
            .Where(t => t.LastViewedAt is not { } played || played < run.ExcludeSince)
            .ToList();
        await LoadAlbumsAsync(run.Client, found.Select(t => t.AlbumRatingKey), ct).ConfigureAwait(false);
        return MelodayText.Dedupe(found).Where(t => Allowed(run, t)).ToList();
    }

    private async Task NearestAsync(Run run, IEnumerable<Track> tracks, CancellationToken ct)
    {
        var kind = _intelligence.Kind;
        var server = run.Client.Server.MachineIdentifier;
        var missing = new List<string>();
        foreach (var key in tracks.Select(t => t.RatingKey).Distinct().Where(k => !run.Similar.ContainsKey(k)))
        {
            if (kind == Core.Audio.MusicEngineKind.Plex && _sonic.TryGet(server, key) is { } cached) run.Similar[key] = cached;
            else missing.Add(key);
        }
        await Parallel.ForEachAsync(missing, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = ct }, async (key, token) =>
        {
            try
            {
                var near = await _intelligence.SimilarAsync<Track>(key, SonicLimit, SonicDistance, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (kind != _intelligence.Kind) return;
                foreach (var t in near) run.Known[t.RatingKey] = t;
                var keys = near.Select(t => t.RatingKey).ToList();
                run.Similar[key] = keys;
                if (kind == Core.Audio.MusicEngineKind.Plex) _sonic.Set(server, key, keys);
            }
            catch (PlexException)
            {
                run.Similar[key] = [];
            }
        }).ConfigureAwait(false);
    }

    private async Task<List<Track>> PathfindAsync(Run run, List<Track> tracks, CancellationToken ct)
    {
        if (!_intelligence.IsAvailable || tracks.Count < 2) return tracks;
        await NearestAsync(run, tracks, ct).ConfigureAwait(false);
        var similar = tracks.DistinctBy(t => t.RatingKey).ToDictionary(t => t.RatingKey,
            t => run.Similar.GetValueOrDefault(t.RatingKey) ?? [], StringComparer.Ordinal);
        return MelodayCurator.Pathfind(tracks, similar);
    }

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
        var taste = _taste.Engine;
        var now = DateTimeOffset.Now;
        return Freshness.WeightedOrder(candidates, t => t.LastViewedAt, now, random, t => taste.Like(t) + taste.Adventure * TasteEngine.Novelty(t, now))
            .Take(limit).ToList();
    }

    private List<Track> Theme(Run run, IEnumerable<Track> tracks, PeriodProfile profile) =>
        MelodayCurator.BiasToTheme(tracks.Where(t => Allowed(run, t)), profile.DominantGenre, profile.DominantMood, Album);

    private bool Allowed(Run run, Track t) =>
        run.Disliked.Allows(t) && !MelodayCurator.IsSkipped(t.Genres.Concat(Album(t)?.Genres ?? (IEnumerable<string>)[]), run.Skipped);

    private AlbumTags? Album(Track t) => t.AlbumRatingKey is { } key ? Album(key) : null;

    private AlbumTags? Album(string key) => _albums.GetValueOrDefault(key);

    private AlbumTags Tags(Track t) => Album(t) ?? new AlbumTags(t.Genres.ToHashSet(), t.Moods.ToHashSet(), t.Year);

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

    public async Task<OpenAiResult> SaveAiKeyAsync(string key)
    {
        key = key.Trim();
        var result = await _openAi.CheckKeyAsync(key).ConfigureAwait(false);
        if (result == OpenAiResult.Ok) _aiKey.Save(key);
        return result;
    }

    public void RemoveAiKey() => _aiKey.Clear();

    private async Task KeepOnPlexAsync(PlexServerClient client, MelodayMix mix, CancellationToken ct)
    {
        var syncScope = StorageScope;
        var sharedSyncPath = SharedSyncPath;
        await _plexSyncGate.WaitAsync(ct).ConfigureAwait(false);
        _plexSyncing = true;
        _plexError = null;
        PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            if (_session.Client != client || syncScope != Scope) return;
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
            state.PlexPlaylistKey = playlist.RatingKey;
            Write(sharedSyncPath, playlist.RatingKey);
            SaveState(state);
            await client.EditPlaylistAsync(playlist.RatingKey, title, mix.Description, ct).ConfigureAwait(false);
            var cover = mix.CoverPath;
            if (cover is null || !File.Exists(cover))
                cover = await MelodayCover.RenderAsync(mix.Period, mix.Title, mix.MadeAt, _log).ConfigureAwait(false);
            await SetCoverAsync(client, playlist.RatingKey, cover, ct).ConfigureAwait(false);
            if (syncScope != Scope) return;
            state = LoadState();
            state.PlexSyncedFor = mix.PeriodStart;
            state.PlexSyncedAt = DateTime.Now;
            state.PlexCoverVersion = PlexCoverVersion;
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
            _plexSyncGate.Release();
            PlexSyncChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task SetCoverAsync(PlexServerClient client, string playlistKey, string? cover, CancellationToken ct)
    {
        if (cover is null || !File.Exists(cover))
        {
            throw new IOException("The Meloday cover is unavailable; Plex sync will retry.");
        }
        await client.SetPlaylistCoverAsync(playlistKey, await File.ReadAllBytesAsync(cover, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        _log.LogInformation("Meloday: the Plex playlist has its cover");
    }

    public async Task KeepCurrentOnPlexAsync()
    {
        var mix = await GetAsync().ConfigureAwait(false);
        if (mix is null || _session.Client is not { } client || !_settings.Current.MelodayPlexPlaylist) return;
        await KeepOnPlexAsync(client, mix, default).ConfigureAwait(false);
    }

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
        public string? Scope { get; set; }
        public SavedMix? Mix { get; set; }
        public string? PlexPlaylistKey { get; set; }
        public DateTime? PlexSyncedFor { get; set; }
        public DateTime? PlexSyncedAt { get; set; }
        public int PlexCoverVersion { get; set; }
    }

    private sealed class UsedTrack
    {
        public DateTime Date { get; set; }
        public string Playlist { get; set; } = string.Empty;
        public string RatingKey { get; set; } = string.Empty;
        public string? Title { get; set; }
        public string? Artist { get; set; }
    }

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

    private const int PlexCoverVersion = 9;

    private string StorageScope => _storageScope.Value ?? Scope;
    private string ScopedPath(string name) => Path.Combine(AppPaths.Meloday, name + "-" +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(StorageScope)))[..16] + ".json");
    private string StatePath => ScopedPath("state");
    private string UsedPath => ScopedPath("used");
    private string SharedSyncPath => Path.Combine(AppPaths.Meloday, "plex-sync-" +
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AccountLibraryScope)))[..16] + ".json");

    private SavedState LoadState()
    {
        var saved = Read<SavedState>(StatePath);
        if (saved is not null)
        {
            saved.PlexPlaylistKey = Read<string>(SharedSyncPath) ?? saved.PlexPlaylistKey;
            return saved;
        }
        var legacyPath = Path.Combine(AppPaths.Meloday, "state.json");
        var legacy = Read<SavedState>(legacyPath);
        if (!_intelligence.IsMeloday && legacy is not null && (legacy.Scope is null || legacy.Scope == StorageScope)
            && legacy.Mix?.Server == _session.Server?.MachineIdentifier && legacy.Mix?.Section == _session.Section?.Key)
        {
            legacy.Scope = StorageScope;
            Write(legacyPath, legacy);
            Write(StatePath, legacy);
            var used = Read<List<UsedTrack>>(Path.Combine(AppPaths.Meloday, "used.json"));
            if (used is not null && !File.Exists(UsedPath)) Write(UsedPath, used);
            return legacy;
        }
        var shared = Read<string>(SharedSyncPath);
        if (shared is null)
        {
            var oldPath = Path.Combine(AppPaths.Meloday, "state-" + Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AccountLibraryScope)))[..16] + ".json");
            var old = Read<SavedState>(oldPath);
            if (old?.Scope == AccountLibraryScope) shared = old.PlexPlaylistKey;
        }
        return new SavedState { Scope = StorageScope, PlexPlaylistKey = shared };
    }

    private void SaveState(SavedState state)
    {
        if (state.Scope is not null && state.Scope != StorageScope) return;
        state.Scope = StorageScope;
        Write(StatePath, state);
    }

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
            var map = new Dictionary<string, string[]>(
                JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path)) ?? [], StringComparer.OrdinalIgnoreCase);
            _log.LogInformation("Meloday: {File} loaded ({Count} entries)", file, map.Count);
            return map;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _log.LogWarning("Meloday: {File} unavailable: {Message}", file, ex.Message);
            return [];
        }
    }
}
