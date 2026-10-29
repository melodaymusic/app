using MelodayMusic.Core;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Tagging;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record MoodRow(Track Track, IReadOnlyList<string> Current, bool Locked, MoodPlan Plan, MoodTagEntry? ByMeloday, bool Analysed);

public sealed class MoodTagService
{
    private const int Batch = 50;
    private readonly PlexSession _session;
    private readonly MelodayEngine _engine;
    private readonly ILogger<MoodTagService> _log;
    private readonly MoodTagLedger _ledger = new(Path.Combine(AppPaths.Root, "tags", "moods.json"));
    private readonly SemaphoreSlim _writing = new(1, 1);
    private CancellationTokenSource? _run;

    public MoodTagService(PlexSession session, MelodayEngine engine, ILogger<MoodTagService> log)
    {
        _session = session;
        _engine = engine;
        _log = log;
    }

    public event EventHandler? Changed;

    private string Library => $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}";

    public int Tagged => _ledger.Count(Library);

    public bool IsRunning => _run is not null;
    public string Progress { get; private set; } = string.Empty;

    public async Task<IReadOnlyList<MoodRow>> PreviewAsync(IReadOnlyList<Track> tracks, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || tracks.Count == 0) return [];
        var states = (await client.GetMoodsAsync(tracks.Select(t => t.RatingKey), ct).ConfigureAwait(false))
            .ToDictionary(s => s.RatingKey, StringComparer.Ordinal);
        foreach (var t in tracks) await _engine.CachedAsync(t, ct).ConfigureAwait(false);
        var heard = await _engine.DescribeKnownAsync(tracks, ct).ConfigureAwait(false);
        var library = Library;
        return tracks.Select(t =>
        {
            var (_, moods, locked) = states.TryGetValue(t.RatingKey, out var s) ? s : (t.RatingKey, t.Moods, false);
            heard.TryGetValue(t.RatingKey, out var description);
            return new MoodRow(t, moods, locked, MoodTags.Plan(t.RatingKey, moods, MoodTags.Heard(description)),
                _ledger.Get(library, t.RatingKey), description is not null);
        }).ToList();
    }

    public async Task<int> ApplyAsync(IEnumerable<MoodRow> rows, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return 0;
        await _writing.WaitAsync(ct).ConfigureAwait(false);
        var changed = 0;
        try
        {
            foreach (var row in rows.Where(r => r.Plan.HasChange))
            {
                ct.ThrowIfCancellationRequested();
                await client.SetMoodsAsync(section.Key, row.Track.RatingKey, row.Plan.Result, locked: true, ct).ConfigureAwait(false);
                _ledger.Record(Library, row.Track.RatingKey, row.Plan.Add.Select(a => a.Mood), row.Locked, DateTimeOffset.Now);
                changed++;
            }
        }
        finally
        {
            _writing.Release();
            if (changed > 0) Changed?.Invoke(this, EventArgs.Empty);
        }
        _log.LogInformation("Mood tags: added ME's moods to {Count} songs in Plex", changed);
        return changed;
    }

    public async Task<int> UndoAsync(IEnumerable<string> ratingKeys, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return 0;
        var library = Library;
        var keys = ratingKeys.Where(k => _ledger.Get(library, k) is not null).ToList();
        if (keys.Count == 0) return 0;
        await _writing.WaitAsync(ct).ConfigureAwait(false);
        var changed = 0;
        try
        {
            foreach (var chunk in keys.Chunk(Batch))
            {
                var states = (await client.GetMoodsAsync(chunk, ct).ConfigureAwait(false)).ToDictionary(s => s.RatingKey, StringComparer.Ordinal);
                foreach (var key in chunk)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_ledger.Get(library, key) is not { } entry) continue;
                    if (states.TryGetValue(key, out var state))
                    {
                        var remove = MoodTags.ToRemove(state.Moods, entry.Added);
                        if (remove.Count > 0)
                            await client.RemoveMoodsAsync(section.Key, key, remove, entry.WasLocked, ct).ConfigureAwait(false);
                        else if (state.Locked != entry.WasLocked)
                            await client.SetMoodsAsync(section.Key, key, state.Moods, entry.WasLocked, ct).ConfigureAwait(false);
                        changed++;
                    }
                    _ledger.Forget(library, key);
                }
            }
        }
        finally
        {
            _writing.Release();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        _log.LogInformation("Mood tags: took ME's moods back out of {Count} songs in Plex", changed);
        return changed;
    }

    public IReadOnlyList<string> TaggedKeys => _ledger.Keys(Library);

    public Task<int> TagLibraryAsync() => RunAsync("Tagging", async (report, ct) =>
    {
        var tracks = _engine.Snapshot().Values.Select(p => p.Track).ToList();
        var changed = 0;
        for (var i = 0; i < tracks.Count; i += Batch)
        {
            ct.ThrowIfCancellationRequested();
            var rows = await PreviewAsync(tracks.Skip(i).Take(Batch).ToList(), ct).ConfigureAwait(false);
            changed += await ApplyAsync(rows, ct).ConfigureAwait(false);
            report(Math.Min(tracks.Count, i + Batch), tracks.Count);
        }
        return changed;
    });

    public Task<int> UndoLibraryAsync() => RunAsync("Removing", async (report, ct) =>
    {
        var keys = TaggedKeys;
        var changed = 0;
        for (var i = 0; i < keys.Count; i += Batch)
        {
            changed += await UndoAsync(keys.Skip(i).Take(Batch), ct).ConfigureAwait(false);
            report(Math.Min(keys.Count, i + Batch), keys.Count);
        }
        return changed;
    });

    public void Cancel() => _run?.Cancel();

    private async Task<int> RunAsync(string verb, Func<Action<int, int>, CancellationToken, Task<int>> work)
    {
        if (_run is not null) return 0;
        _run = new CancellationTokenSource();
        var ct = _run.Token;
        Progress = $"{verb}…";
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            return await Task.Run(() => work((done, total) =>
            {
                Progress = $"{verb}: {done:N0} of {total:N0} songs";
                Changed?.Invoke(this, EventArgs.Empty);
            }, ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException or IOException)
        {
            _log.LogWarning(ex, "Mood tags: {Verb} stopped", verb);
            return 0;
        }
        finally
        {
            _run.Dispose();
            _run = null;
            Progress = string.Empty;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
