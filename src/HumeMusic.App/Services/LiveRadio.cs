using System.Collections.Concurrent;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Radio;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>Where a live station is right now: what to play, how far into the first song, how to keep it going, and where a song is in its artist's block.</summary>
public sealed record LiveTuning(
    IReadOnlyList<Track> Queue,
    TimeSpan Offset,
    Func<CancellationToken, Task<IReadOnlyList<Track>>> More,
    Func<string, LiveBlockSlot?> BlockOf,
    LiveFeedback Feedback);

/// <summary>What the listener can say of the song on: whether it has been taken off this station, and taking it off (or putting it back).</summary>
public sealed record LiveFeedback(Func<Track, bool> IsOff, Action<Track, bool> SetOff);

/// <summary>
/// The live stations (Headliners, Hits, Club and Chill, and Library Radio and Deep Cuts): always on, as if each had been
/// playing since before you arrived. Each keeps a running order (<see cref="LiveSchedule"/>) that is
/// remembered between visits, so tuning in joins the song that is on, in the middle of it, and carries
/// on from there; come back an hour later and it has played an hour's worth of other songs. A radio you
/// start yourself (an artist's, a genre's) begins at its first song instead.
/// </summary>
public sealed class LiveRadio
{
    /// <summary>The station always has at least this much still to come.</summary>
    private static readonly TimeSpan Ahead = TimeSpan.FromMinutes(40);
    /// <summary>Songs that finished longer ago than this are forgotten.</summary>
    private static readonly TimeSpan KeepPlayed = TimeSpan.FromHours(1);
    /// <summary>How many songs are queued on tuning in, and each time the queue runs low.</summary>
    private const int QueueLength = 8;
    private const int MaxRemembered = 800;

    private readonly PlexSession _session;
    private readonly MixService _mixes;
    private readonly LocalRadio _local;
    private readonly LiveScheduleStore _store;
    private readonly StationFeedbackStore _feedback;
    private readonly ILogger<LiveRadio> _log;
    private readonly ConcurrentDictionary<string, Track> _tracks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, BlockRadio> _blocks = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    // The stations tuned in during this run of the app (each station's running order is made again, from the song on, on its first tune-in of a run).
    private readonly HashSet<string> _tuned = [];

    public LiveRadio(PlexSession session, MixService mixes, LocalRadio local, LiveScheduleStore store, StationFeedbackStore feedback, ILogger<LiveRadio> log)
    {
        _session = session;
        _mixes = mixes;
        _local = local;
        _store = store;
        _feedback = feedback;
        _log = log;
    }

    /// <summary>The songs taken off a station (none where the server isn't known).</summary>
    private IReadOnlySet<string> Off(Station station) =>
        _session.Server?.MachineIdentifier is { } server ? _feedback.Off($"{server}|{station.RatingKey}") : new HashSet<string>();

    /// <summary>Tunes in: the songs from the one that is on now, and how far into it. Null if the station has nothing to play.</summary>
    public async Task<LiveTuning?> TuneInAsync(Station station, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Server?.MachineIdentifier is not { } server) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var key = $"{server}|{station.RatingKey}";
            var now = DateTimeOffset.Now;
            var schedule = _store.Get(key);
            if (schedule is null || schedule.Locate(now) is null)
            {
                // Never visited, or it ran out while nobody was here: a new running order, already under way.
                schedule = await BeginAsync(station, now, ct).ConfigureAwait(false);
                if (schedule is null) return null;
            }
            else
            {
                lock (schedule)
                {
                    schedule.Trim(now, KeepPlayed);
                    // A new run of the app (an update, a restart) joins the song that is on but plans what follows afresh, so the
                    // station doesn't play out the order it had already shown the last time (the same songs, the same artists).
                    if (_tuned.Add(key)) schedule.DropAfterCurrent(now);
                }
            }
            _tuned.Add(key);
            await FillAsync(station, schedule, w => w.Remaining(now) < Ahead, ct).ConfigureAwait(false);
            _store.Set(key, schedule);

            if (schedule.Locate(now) is not { } at) return null;
            var taken = schedule.Entries.Skip(at.Index).Take(QueueLength).ToList();
            // Songs taken off this station are skipped over (the one that is on may be one: the next starts from its beginning).
            var off = _feedback.Off(key);
            var queued = taken.Where(e => !off.Contains(e.Key)).ToList();
            if (queued.Count == 0) return null;
            var tracks = await TracksForAsync(client, queued, ct).ConfigureAwait(false);
            if (tracks.Count == 0) return null;
            // The song that is on may have left the library: then the next one starts from its beginning.
            var offset = queued[0].Key == taken[0].Key && tracks[0].RatingKey == queued[0].Key ? TimeSpan.FromMilliseconds(at.OffsetMs) : TimeSpan.Zero;

            var given = at.Index + taken.Count;
            async Task<IReadOnlyList<Track>> More(CancellationToken c)
            {
                await _gate.WaitAsync(c).ConfigureAwait(false);
                try
                {
                    await FillAsync(station, schedule, w => w.Entries.Count < given + QueueLength, c).ConfigureAwait(false);
                    var next = schedule.Entries.Skip(given).Take(QueueLength).ToList();
                    given += next.Count;
                    _store.Set(key, schedule);
                    return (await TracksForAsync(client, next, c).ConfigureAwait(false)).Where(t => !_feedback.IsOff(key, t.RatingKey)).ToList();
                }
                finally
                {
                    _gate.Release();
                }
            }
            LiveBlockSlot? BlockOf(string ratingKey)
            {
                lock (schedule) return schedule.SlotOf(ratingKey);
            }
            var feedback = new LiveFeedback(t => _feedback.IsOff(key, t.RatingKey), (t, away) => _feedback.Set(key, t.RatingKey, away));
            return new LiveTuning(tracks, offset, More, BlockOf, feedback);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>A running order that began a little before now, so tuning in lands in the middle of a song.</summary>
    private async Task<LiveSchedule?> BeginAsync(Station station, DateTimeOffset now, CancellationToken ct)
    {
        var first = await SourceAsync(station, ct).ConfigureAwait(false);
        if (first.Tracks.Count < 3) return null;
        Remember(first.Tracks);
        var entries = Entries(first, 1);
        // Somewhere in the first two songs, never in the first minute (that would be a station that had just begun).
        var span = entries.Take(2).Sum(e => e.DurationMs) / 1000;
        var into = TimeSpan.FromSeconds(Random.Shared.Next(60, Math.Max(61, (int)(span * 0.9))));
        return LiveSchedule.Begin(entries, now, into);
    }

    /// <summary>Adds songs to the running order while <paramref name="needsMore"/> says it is short (a few rounds at most).</summary>
    private async Task FillAsync(Station station, LiveSchedule schedule, Func<LiveSchedule, bool> needsMore, CancellationToken ct)
    {
        for (var round = 0; round < 6 && needsMore(schedule); round++)
        {
            var batch = await SourceAsync(station, ct).ConfigureAwait(false);
            Remember(batch.Tracks);
            lock (schedule)
            {
                if (schedule.Append(Entries(batch, schedule.NextBlock)) == 0) break;
            }
        }
    }

    /// <summary>Songs to add to a running order; <c>Groups</c> (parallel to the songs) puts them in artists' blocks (0: in none).</summary>
    private sealed record Batch(IReadOnlyList<Track> Tracks, IReadOnlyList<int> Groups)
    {
        public static Batch Empty { get; } = new([], []);
    }

    /// <summary>The next songs the station would play: Headliners' artist blocks, or the station as Plex (or Meloday) serves it.</summary>
    private async Task<Batch> SourceAsync(Station station, CancellationToken ct)
    {
        try
        {
            if (station.StationKind == StationKind.Flagship)
            {
                var (tracks, groups) = await _blocks.GetOrAdd(station.RatingKey, _ => _local.ForBlocks()).NextAsync(Off(station), ct).ConfigureAwait(false);
                return new Batch(tracks, groups);
            }
            var off = Off(station);
            var plain = await _mixes.FirstTracksAsync(station, batches: 2).ConfigureAwait(false) ?? [];
            return new Batch(plain.Where(t => !off.Contains(t.RatingKey)).ToList(), []);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("{Station} couldn't be filled: {Message}", station.Title, ex.Message);
            return Batch.Empty;
        }
    }

    /// <summary>The batch as running-order entries, its blocks numbered from <paramref name="firstBlock"/>.</summary>
    private static List<LiveEntry> Entries(Batch batch, int firstBlock) =>
        batch.Tracks.Select((t, i) => new LiveEntry(t.RatingKey, (int)Math.Min(t.DurationMs, int.MaxValue),
            i < batch.Groups.Count && batch.Groups[i] > 0 ? firstBlock + batch.Groups[i] - 1 : 0)).ToList();

    private void Remember(IEnumerable<Track> tracks)
    {
        if (_tracks.Count > MaxRemembered) _tracks.Clear();
        foreach (var t in tracks) _tracks[t.RatingKey] = t;
    }

    /// <summary>The songs of a running order, in order: from memory, the rest asked of Plex (a station remembered from an earlier run).</summary>
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
