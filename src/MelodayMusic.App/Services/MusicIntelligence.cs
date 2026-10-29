using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using System.Collections.Concurrent;

namespace MelodayMusic.Services;

public sealed class MusicIntelligence
{
    private readonly SettingsStore _settings;
    private readonly PlexSession _session;
    private readonly PlexPass _pass;
    private readonly MelodayEngine _engine;
    private readonly AudioAnalysisService _analysis;
    private readonly SemaphoreSlim _search = new(1, 1);
    private readonly ConcurrentDictionary<(string Scope, string From, string To), double> _plexDistances = new();
    public event EventHandler? DistancesAvailable;
    public event EventHandler? Changed;
    public MusicEngineKind Kind => _settings.Current.MusicEngine;
    public bool IsMeloday => Kind == MusicEngineKind.Meloday;
    public bool CanUsePlex => !IsMeloday && _pass.IsAvailable;
    public bool IsAvailable => IsMeloday || _pass.IsAvailable;
    public string Name => IsMeloday ? "Meloday Engine" : "Plex Sonic Analysis";

    public MusicIntelligence(SettingsStore settings, PlexSession session, PlexPass pass, MelodayEngine engine, AudioAnalysisService analysis)
    { _settings = settings; _session = session; _pass = pass; _engine = engine; _analysis = analysis; }

    public void Select(MusicEngineKind kind)
    {
        if (kind == Kind) return;
        _settings.Current.MusicEngine = kind;
        _settings.Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<T>> SimilarAsync<T>(string ratingKey, int limit = 20, double maxDistance = 0.3,
        CancellationToken ct = default) where T : MediaItem
    {
        var kind = Kind;
        var scope = _engine.Scope;
        if (_session.Client is not { } client) return [];
        if (kind == MusicEngineKind.Plex)
        {
            var plex = _pass.IsAvailable ? await client.GetSonicallySimilarAsync<T>(ratingKey, limit, maxDistance, ct).ConfigureAwait(false) : [];
            if (scope != _engine.Scope || kind != Kind) return [];
            foreach (var track in plex.OfType<Track>().Where(t => t.SonicDistance is >= 0))
                _plexDistances[(scope, ratingKey, track.RatingKey)] = track.SonicDistance!.Value;
            if (_plexDistances.Count > 100000) _plexDistances.Clear();
            return plex;
        }
        var items = await client.GetItemsAsync([ratingKey], ct).ConfigureAwait(false);
        var seed = items.FirstOrDefault();
        if (seed is null || scope != _engine.Scope || kind != Kind) return [];
        var snapshot = _engine.Snapshot();
        var seeds = snapshot.Values.Where(p => seed switch
        {
            Track t => p.Track.RatingKey == t.RatingKey && p.Track.DurationMs == t.DurationMs,
            Artist a => p.Track.ArtistRatingKey == a.RatingKey,
            Album a => p.Track.AlbumRatingKey == a.RatingKey,
            _ => false
        }).ToArray();
        if (seeds.Length == 0)
        {
            if (seed is Track t) _analysis.RequestPriority([t]);
            else if (seed is Album album) _analysis.RequestPriority(await client.GetAlbumTracksAsync(album.RatingKey, ct).ConfigureAwait(false));
            else if (seed is Artist artist) _analysis.RequestPriority(await client.GetArtistTracksAsync(artist.RatingKey, ct).ConfigureAwait(false));
            return [];
        }
        var results = await SearchAsync(() =>
        {
            var centre = MusicSimilarity.Normalize(Enumerable.Range(0, 512).Select(i => seeds.Average(p => p.Vector[i])));
            var excluded = seeds.Select(p => p.Track.RatingKey).ToHashSet();
            var ranked = snapshot.Values.Where(p => !excluded.Contains(p.Track.RatingKey))
                .Select(p => { ct.ThrowIfCancellationRequested(); return (Profile: p, Score: MusicSimilarity.Cosine(centre, p.Vector)); })
                .Where(p => double.IsFinite(p.Score) && p.Score >= 0.2).OrderByDescending(p => p.Score).ToArray();
            if (typeof(T) == typeof(Track)) return ranked.Take(limit).Select(p => p.Profile.Track.RatingKey).ToArray();
            return ranked.GroupBy(p => typeof(T) == typeof(Artist) ? p.Profile.Track.ArtistRatingKey : p.Profile.Track.AlbumRatingKey)
                .Where(g => g.Key is not null && g.Key != ratingKey)
                .OrderByDescending(g => g.Take(3).Average(p => p.Score)).Take(limit).Select(g => g.Key!).ToArray();
        }, ct).ConfigureAwait(false);
        if (scope != _engine.Scope || kind != Kind) return [];
        if (typeof(T) == typeof(Track))
            return results.Select(k => snapshot[k].Track).Cast<T>().ToArray();
        var found = await client.GetItemsAsync(results, ct).ConfigureAwait(false);
        if (scope != _engine.Scope || kind != Kind) return [];
        return results.Select(k => found.OfType<T>().FirstOrDefault(t => t.RatingKey == k)).OfType<T>().ToArray();
    }

    public async Task<IReadOnlyList<Track>> AdventureAsync(Track start, Track end, CancellationToken ct = default, Func<Track, bool>? allowed = null)
    {
        var kind = Kind;
        var scope = _engine.Scope;
        if (_session.Client is not { } client || _session.Section is not { } section) return [];
        if (kind == MusicEngineKind.Plex)
        {
            var plex = _pass.IsAvailable ? await client.GetSonicAdventureAsync(section.Key, start.RatingKey, end.RatingKey, ct).ConfigureAwait(false) : [];
            return scope == _engine.Scope && kind == Kind ? plex : [];
        }
        _analysis.RequestPriority([start, end]);
        var snapshot = _engine.Snapshot();
        var vectors = snapshot.Where(p => p.Key == start.RatingKey || p.Key == end.RatingKey || allowed?.Invoke(p.Value.Track) != false)
            .ToDictionary(p => p.Key, p => p.Value.Vector);
        var path = await SearchAsync(() => MusicSimilarity.Path(vectors, start.RatingKey, end.RatingKey, ct: ct), ct).ConfigureAwait(false);
        return scope != _engine.Scope || kind != Kind ? [] : path.Select(k => snapshot[k].Track).ToArray();
    }

    public async Task<IReadOnlyList<Track>> StretchAsync(Track start, Track end, int count, Func<Track, bool> allowed, CancellationToken ct = default)
    {
        if (Kind == MusicEngineKind.Plex)
        {
            var path = await AdventureAsync(start, end, ct, allowed).ConfigureAwait(false);
            var between = path.Skip(1).Take(Math.Max(0, path.Count - 2)).Where(allowed).ToList();
            if (between.Count <= count) return between;
            return Enumerable.Range(0, count).Select(i => between[(int)Math.Round((i + 0.5) * between.Count / count - 0.5)]).Distinct().ToList();
        }
        _analysis.RequestPriority([start, end]);
        var scope = _engine.Scope;
        var snapshot = _engine.Snapshot();
        var vectors = snapshot.ToDictionary(p => p.Key, p => p.Value.Vector);
        var keys = await SearchAsync(() => MusicSimilarity.Stretch(vectors, start.RatingKey, end.RatingKey, count,
            k => snapshot.TryGetValue(k, out var profile) && allowed(profile.Track), ct), ct).ConfigureAwait(false);
        return scope != _engine.Scope ? [] : keys.Select(k => snapshot[k].Track).ToList();
    }

    public Func<Track, Track, double?> CaptureDistances()
    {
        if (!IsMeloday)
        {
            var scope = _engine.Scope;
            return (a, b) => _plexDistances.TryGetValue((scope, a.RatingKey, b.RatingKey), out var distance)
                || _plexDistances.TryGetValue((scope, b.RatingKey, a.RatingKey), out distance) ? distance : null;
        }
        var profiles = _engine.Snapshot();
        return (a, b) =>
        {
            if (!profiles.TryGetValue(a.RatingKey, out var left) || !profiles.TryGetValue(b.RatingKey, out var right)) return null;
            var value = MusicSimilarity.Cosine(left.Vector, right.Vector);
            return double.IsFinite(value) ? 1 - value : null;
        };
    }

    private async Task<T> SearchAsync<T>(Func<T> search, CancellationToken ct)
    {
        await _search.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var thread = Thread.CurrentThread;
                var priority = thread.Priority;
                try { thread.Priority = ThreadPriority.BelowNormal; return search(); }
                finally { thread.Priority = priority; }
            }, ct).ConfigureAwait(false);
        }
        finally { _search.Release(); }
    }

    public async Task WarmCuratorAsync(IEnumerable<Track> tracks)
    {
        var kind = Kind;
        var front = tracks.DistinctBy(t => t.RatingKey).Take(8).ToArray();
        if (IsMeloday) { _analysis.RequestPriority(front); return; }
        if (!IsAvailable) return;
        try
        {
            await Parallel.ForEachAsync(front, new ParallelOptions { MaxDegreeOfParallelism = 2 }, async (track, ct) =>
            {
                if (kind != Kind) return;
                await SimilarAsync<Track>(track.RatingKey, 100, 1, ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
            if (kind == Kind) DistancesAvailable?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException or OperationCanceledException) { }
    }
}
