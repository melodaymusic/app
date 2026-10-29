using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Radio;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record LiveTuning(
    IReadOnlyList<Track> Queue,
    TimeSpan Offset,
    Func<CancellationToken, Task<IReadOnlyList<Track>>> More,
    Func<string, LiveBlockSlot?> BlockOf,
    LiveFeedback Feedback,
    Station Station,
    Action<Track> Played,
    Func<string, string?> ProgrammeOf);

public sealed record LiveFeedback(Func<Track, bool> IsOff, Action<Track, bool> SetOff);

public sealed class LiveRadio
{
    private static readonly TimeSpan Ahead = TimeSpan.FromMinutes(40);
    private static readonly TimeSpan KeepPlayed = TimeSpan.FromHours(4);
    private const int QueueLength = 8;
    private const int MaxRemembered = 800;

    private readonly PlexSession _session;
    private readonly MixService _mixes;
    private readonly LocalRadio _local;
    private readonly LiveScheduleStore _store;
    private readonly RadioPlaybackStore _played;
    private readonly StationFeedbackStore _feedback;
    private readonly ILogger<LiveRadio> _log;
    private readonly SettingsStore _settings;
    private readonly MusicIntelligence _intelligence;
    private readonly PulseScheduleService? _pulse;
    private string? _countryScope;
    private HashSet<string> _canadianArtists = [];
    private DateTimeOffset _countriesAt;
    private readonly ConcurrentDictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LiveRadio(PlexSession session, MixService mixes, LocalRadio local, LiveScheduleStore store, RadioPlaybackStore played, StationFeedbackStore feedback, SettingsStore settings, MusicIntelligence intelligence, ILogger<LiveRadio> log, PulseScheduleService? pulse = null)
    {
        _session = session;
        _mixes = mixes;
        _local = local;
        _store = store;
        _played = played;
        _feedback = feedback;
        _log = log;
        _settings = settings;
        _intelligence = intelligence;
        _pulse = pulse;
    }

    private IReadOnlySet<string> Off(Station station) =>
        _session.Server?.MachineIdentifier is { } server ? _feedback.Off($"{server}|{station.RatingKey}") : new HashSet<string>();

    public async Task<LiveTuning?> TuneInAsync(Station station, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Server?.MachineIdentifier is not { } server) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var legacyKey = $"{server}|{_session.Section?.Key}|{station.RatingKey}";
            var playedScope = PlaybackScope(station);
            var key = playedScope + "|" + _intelligence.Kind;
            var feedbackKey = $"{server}|{station.RatingKey}";
            var now = DateTimeOffset.Now;
            var schedule = _store.Get(key) ?? (!_intelligence.IsMeloday ? _store.Take(playedScope) ?? _store.Take(legacyKey) ?? _store.Take(feedbackKey) : null);
            if (schedule is not null)
            {
                var known = await TracksForAsync(client, schedule.Entries, ct).ConfigureAwait(false);
                var byKey = known.DistinctBy(t => t.RatingKey).ToDictionary(t => t.RatingKey);
                schedule.Entries = schedule.Entries.Select(e => byKey.TryGetValue(e.Key, out var t)
                    ? e with { Artist = LiveRotation.ArtistOf(t), Song = LiveRotation.SongOf(t) } : e).ToList();
                schedule.ImportRotation();
            }
            var pulseVersion = station.StationKind == StationKind.Flagship ? _pulse?.Version : null;
            if (schedule is not null && pulseVersion is not null && schedule.PulseGuideVersion != pulseVersion)
            {
                lock (schedule) schedule.DropAfterCurrent(now);
                schedule.PulseGuideVersion = pulseVersion;
            }
            if (schedule is null || schedule.Locate(now) is null)
            {
                schedule = await BeginAsync(station, now, schedule, ct).ConfigureAwait(false);
                if (schedule is null) return null;
            }
            else
            {
                lock (schedule)
                {
                    schedule.Trim(now, KeepPlayed);
                }
            }
            await FillAsync(station, schedule, w => w.Remaining(now) < Ahead, ct).ConfigureAwait(false);
            if (pulseVersion is not null) schedule.PulseGuideVersion = pulseVersion;
            _store.Set(key, schedule);
            if (_store.SaveError is { } saveError) _log.LogWarning("Live radio history could not be saved: {Error}", saveError);

            if (schedule.Locate(now) is not { } at) return null;
            var taken = schedule.Entries.Skip(at.Index).Take(QueueLength).ToList();
            var off = _feedback.Off(feedbackKey);
            var queued = taken.Where(e => !off.Contains(e.Key)).ToList();
            if (queued.Count == 0) return null;
            var tracks = await TracksForAsync(client, queued, ct).ConfigureAwait(false);
            if (tracks.Count == 0) return null;
            var offset = queued[0].Key == taken[0].Key && tracks[0].RatingKey == queued[0].Key ? TimeSpan.FromMilliseconds(at.OffsetMs) : TimeSpan.Zero;

            var given = at.Index + taken.Count;
            async Task<IReadOnlyList<Track>> More(CancellationToken c)
            {
                await _gate.WaitAsync(c).ConfigureAwait(false);
                try
                {
                    var beforeTrim = schedule.Entries.Count;
                    schedule.Trim(DateTimeOffset.Now, KeepPlayed);
                    given = Math.Max(0, given - (beforeTrim - schedule.Entries.Count));
                    await FillAsync(station, schedule, w => w.Entries.Count < given + QueueLength, c).ConfigureAwait(false);
                    var next = schedule.Entries.Skip(given).Take(QueueLength).ToList();
                    given += next.Count;
                    _store.Set(key, schedule);
                    if (_store.SaveError is { } saveError) _log.LogWarning("Live radio history could not be saved: {Error}", saveError);
                    return (await TracksForAsync(client, next, c).ConfigureAwait(false)).Where(t => !_feedback.IsOff(feedbackKey, t.RatingKey)).ToList();
                }
                finally
                {
                    _gate.Release();
                }
            }
            LiveBlockSlot? BlockOf(string ratingKey)
            {
                lock (schedule) return schedule.SlotOf(ratingKey, DateTimeOffset.Now);
            }
            string? ProgrammeOf(string ratingKey)
            {
                if (station.StationKind != StationKind.Flagship) return null;
                lock (schedule) return schedule.EntryOf(ratingKey, DateTimeOffset.Now)?.Programme ?? "Continuous Pulse";
            }
            var feedback = new LiveFeedback(t => _feedback.IsOff(feedbackKey, t.RatingKey), (t, away) => _feedback.Set(feedbackKey, t.RatingKey, away));
            void Played(Track track)
            {
                var at = DateTimeOffset.Now;
                var featured = BlockOf(track.RatingKey) is not null;
                var scope = playedScope;
                _ = Task.Run(() =>
                {
                    _played.Record(scope, track, at, featured);
                    if (_played.SaveError is not null) _log.LogWarning("Radio playback history could not be saved");
                });
            }
            return new LiveTuning(tracks, offset, More, BlockOf, feedback, station, Played, ProgrammeOf);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<LiveSchedule?> BeginAsync(Station station, DateTimeOffset now, LiveSchedule? previous, CancellationToken ct)
    {
        var round = previous?.ProgrammeRound ?? Random.Shared.Next(3);
        var previousRotation = RotationFor(station, previous?.Rotation.Where(p => p.At <= now).ToList() ?? []);
        var occurrences = previous?.PulseOccurrences.ToHashSet(StringComparer.Ordinal) ?? [];
        var first = await SourceAsync(station, ct, round, now, previousRotation, occurrences).ConfigureAwait(false);
        if (first.Tracks.Count == 0) return null;
        Remember(first.Tracks);
        var rotation = previousRotation.ToList();
        var start = first.Custom?.StartsAt ?? now.AddSeconds(-90);
        var canadian = await CanadianSelectorAsync(ct).ConfigureAwait(false);
        var picks = Pick(station, first, rotation, start, canadian);
        var entries = Entries(new Batch(picks), 1);
        if (entries.Count == 0) return null;
        if (first.Custom is null)
        {
            var into = TimeSpan.FromMilliseconds(Math.Min(90_000, entries[0].DurationMs / 2));
            var shift = (now - into) - start;
            for (var i = rotation.Count - picks.Count; i < rotation.Count; i++) rotation[i] = rotation[i] with { At = rotation[i].At + shift };
            start = now - into;
        }
        return new LiveSchedule { StartedAt = start, Entries = entries, Rotation = rotation, ProgrammeRound = (round + 1) % 3,
            PulseGuideVersion = station.StationKind == StationKind.Flagship ? _pulse?.Version : null,
            PulseOccurrences = occurrences.TakeLast(100).ToList() };
    }

    private async Task FillAsync(Station station, LiveSchedule schedule, Func<LiveSchedule, bool> needsMore, CancellationToken ct)
    {
        for (var round = 0; round < 6 && needsMore(schedule); round++)
        {
            schedule.Rotation = RotationFor(station, schedule.Rotation);
            var occurrences = schedule.PulseOccurrences.ToHashSet(StringComparer.Ordinal);
            var batch = await SourceAsync(station, ct, schedule.ProgrammeRound, schedule.EndsAt, schedule.Rotation, occurrences).ConfigureAwait(false);
            schedule.PulseOccurrences = occurrences.TakeLast(100).ToList();
            schedule.ProgrammeRound = (schedule.ProgrammeRound + 1) % 3;
            Remember(batch.Tracks);
            var canadian = await CanadianSelectorAsync(ct).ConfigureAwait(false);
            lock (schedule)
            {
                var picks = Pick(station, batch, schedule.Rotation, schedule.EndsAt, canadian);
                if (picks.Count == 0) continue;
                schedule.AppendProgrammed(Entries(new Batch(picks), schedule.NextBlock));
            }
        }
    }

    private sealed record Batch(IReadOnlyList<BlockPick> Picks, PulseScheduleOccurrence? Custom = null)
    {
        public IReadOnlyList<Track> Tracks => Picks.Select(p => p.Track).ToList();
        public static Batch Empty { get; } = new([]);
    }

    private int ProgrammeCount(Station station, Batch batch, DateTimeOffset airtime)
    {
        if (station.StationKind != StationKind.Flagship || batch.Tracks.Count == 0) return 20;
        if (batch.Custom is not null) return batch.Tracks.Count;
        var boundary = PulseProgramme.NextBoundary(airtime);
        if (_pulse?.NextBoundary(airtime) is { } custom && custom < boundary) boundary = custom;
        var remaining = (boundary - airtime).TotalMilliseconds;
        var duration = batch.Tracks.Average(t => t.DurationMs > 0 ? t.DurationMs : LiveSchedule.FallbackDurationMs);
        return Math.Clamp((int)Math.Ceiling(remaining / duration), 1, 20);
    }

    private IReadOnlyList<BlockPick> Pick(Station station, Batch batch, List<RotationPlay> rotation, DateTimeOffset startsAt, Func<Track, bool>? canadian)
    {
        if (batch.Custom is null)
            return LiveRotation.Pick(batch.Picks, rotation, startsAt, count: ProgrammeCount(station, batch, startsAt),
                canadian: canadian, preserveOrder: station.StationKind == StationKind.Flagship);

        var songs = new Core.Meloday.SongSet();
        var picks = batch.Picks.Where(p => songs.Add(p.Track)).ToList();
        if (picks.Count > 1 && rotation.LastOrDefault() is { } last && LiveRotation.SongOf(picks[0].Track) == last.Song)
            picks = picks.Skip(1).Append(picks[0]).ToList();
        var at = startsAt;
        foreach (var pick in picks)
        {
            rotation.Add(new RotationPlay(pick.Track.RatingKey, LiveRotation.ArtistOf(pick.Track), LiveRotation.SongOf(pick.Track), at));
            at = at.AddMilliseconds(pick.Track.DurationMs > 0 ? pick.Track.DurationMs : LiveSchedule.FallbackDurationMs);
        }
        if (rotation.Count > LiveRotation.Remembered) rotation.RemoveRange(0, rotation.Count - LiveRotation.Remembered);
        return picks;
    }

    private async Task<Batch> SourceAsync(Station station, CancellationToken ct, int round, DateTimeOffset airtime, IReadOnlyList<RotationPlay>? rotation = null, ISet<string>? plannedOccurrences = null)
    {
        try
        {
            if (station.StationKind == StationKind.Flagship)
            {
                if (_pulse?.At(airtime) is { } occurrence)
                {
                    var occurrenceKey = occurrence.Rule.Id + "|" + occurrence.StartsAt.ToUnixTimeSeconds();
                    if (plannedOccurrences?.Add(occurrenceKey) != false)
                    {
                        var scheduled = await _pulse.TracksAsync(occurrence, ct).ConfigureAwait(false);
                        var custom = scheduled.Where(t => !Off(station).Contains(t.RatingKey))
                            .Select(t => new BlockPick(t, 0, occurrence.Rule.ProgrammeTitle)).ToList();
                        if (custom.Count > 0) return new Batch(custom, occurrence);
                    }
                }
                var picks = await _local.ForBlocks(round).NextAsync(Off(station), ct, recordFeatures: false, airtime: airtime, rotation: rotation).ConfigureAwait(false);
                return new Batch(picks);
            }
            var off = Off(station);
            ct.ThrowIfCancellationRequested();
            var plain = station.Key is null
                ? await _local.ForStation(station).NextAsync(80, ct).ConfigureAwait(false)
                : await _mixes.FirstTracksAsync(station, batches: 4).ConfigureAwait(false) ?? [];
            return new Batch(plain.Where(t => !off.Contains(t.RatingKey)).Select(t => new BlockPick(t, 0)).ToList());
        }
        catch (PlexException ex)
        {
            _log.LogInformation("{Station} couldn't be filled: {Message}", station.Title, ex.Message);
            return Batch.Empty;
        }
    }

    private static List<LiveEntry> Entries(Batch batch, int firstBlock) =>
        batch.Picks.Select(p => new LiveEntry(p.Track.RatingKey, (int)Math.Min(p.Track.DurationMs, int.MaxValue),
            p.Group > 0 ? firstBlock + p.Group - 1 : 0,
            LiveRotation.ArtistOf(p.Track), LiveRotation.SongOf(p.Track), p.Programme)).ToList();

    private string PlaybackScope(Station station) =>
        $"{_session.Account?.Id ?? _session.Account?.Username}|{_session.Server?.MachineIdentifier}|{_session.Section?.Key}|{station.RatingKey}";

    private List<RotationPlay> RotationFor(Station station, IReadOnlyList<RotationPlay> planned) =>
        planned.Concat(_played.Get(PlaybackScope(station)))
            .DistinctBy(p => (p.Key, p.At)).OrderBy(p => p.At).TakeLast(LiveRotation.Remembered).ToList();

    private void Remember(IEnumerable<Track> tracks)
    {
        if (_tracks.Count > MaxRemembered) _tracks.Clear();
        foreach (var t in tracks) _tracks[t.RatingKey] = t;
    }

    private async Task<Func<Track, bool>?> CanadianSelectorAsync(CancellationToken ct)
    {
        if (!_settings.Current.CanadianBroadcast || _session.Client is not { } client || _session.Section is not { } section) return null;
        var scope = client.Server.MachineIdentifier + ":" + section.Key;
        if (_countryScope != scope || DateTimeOffset.UtcNow - _countriesAt > TimeSpan.FromMinutes(15))
        {
            var artists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                for (var start = 0; ; start += 1000)
                {
                    var page = await client.GetArtistsAsync(section.Key, start, 1000, ct: ct).ConfigureAwait(false);
                    foreach (var artist in page.Items)
                        if (artist.Countries.Any(c => c.Equals("Canada", StringComparison.OrdinalIgnoreCase) || c.Equals("CA", StringComparison.OrdinalIgnoreCase) || c.Equals("CAN", StringComparison.OrdinalIgnoreCase)))
                            artists.Add(artist.Title.Trim());
                    if (page.Items.Count < 1000) break;
                }
                _canadianArtists = artists; _countryScope = scope; _countriesAt = DateTimeOffset.UtcNow;
            }
            catch (PlexException ex) { _log.LogWarning(ex, "Canadian artist countries could not be loaded"); }
        }
        var names = _canadianArtists.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return t => names.Contains(t.DisplayArtist.Trim());
    }

    private async Task<IReadOnlyList<Track>> TracksForAsync(PlexServerClient client, IReadOnlyList<LiveEntry> entries, CancellationToken ct)
    {
        var missing = entries.Where(e => !_tracks.ContainsKey(e.Key)).Select(e => e.Key).ToList();
        if (missing.Count > 0)
        {
            try
            {
                Remember((await client.GetItemsAsync(missing, ct).ConfigureAwait(false)).OfType<Track>());
            }
            catch (PlexException ex)
            {
                _log.LogInformation("Songs of a live station couldn't be fetched: {Message}", ex.Message);
            }
        }
        return entries.Select(e => _tracks.GetValueOrDefault(e.Key)).OfType<Track>().ToList();
    }
}
