using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Services;
using MelodayMusic.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace MelodayMusic.Services;

public sealed class BackgroundAudioAnalysis : IDisposable
{
    private readonly PlexSession _session;
    private readonly AudioAnalysisService _analysis;
    private readonly MelodayEngine _engine;
    private readonly PlayerService _player;
    private readonly SettingsStore _settings;
    private readonly ILogger<BackgroundAudioAnalysis> _log;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly Queue<Track> _page = new();
    private readonly Dictionary<string, DateTimeOffset> _failed = [];
    private CancellationTokenSource _lifetime = new();
    private string? _scope;
    private int _cursor;
    private bool _reachedEnd;
    private DateTimeOffset _nextSweep;
    private bool _working;
    private bool _disposed;
    private readonly Dictionary<string, (AnalysisNote? Head, AnalysisNote? Tail)> _coverage = [];
    private bool _inventoryWorking;
    private bool _inventoryKnown;
    private int _total;
    private DateTimeOffset _nextInventory;
    public event EventHandler? Changed;
    public AnalysisLibrarySummary Summary { get; private set; } = AnalysisLibrarySummary.Create(0, []);
    public bool InventoryKnown => _inventoryKnown;
    public string Status { get; private set; } = "Waiting for your music library";
    public bool FastMode => _settings.Current.FastAudioAnalysis;

    public void SetFastMode(bool fast)
    {
        if (fast == FastMode) return;
        _settings.Current.FastAudioAnalysis = fast;
        _settings.Save();
        _log.LogInformation("Audio analysis pace: {Mode}", fast ? "Power through" : "Background");
        Changed?.Invoke(this, EventArgs.Empty);
        if (!_working) ScheduleNext();
    }

    private void ScheduleNext()
    {
        if (_disposed) return;
        var fast = FastMode && _session.IsReady && !_player.IsBuffering && HasBackgroundBudget()
            && (_nextSweep <= DateTimeOffset.UtcNow || _analysis.HasPriority);
        _timer.Stop();
        _timer.Interval = fast ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(10);
        _timer.Start();
    }

    private void Report(string? status = null)
    {
        if (status is not null) Status = status;
        Summary = AnalysisLibrarySummary.FromNotes(_total, _coverage.Values);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public BackgroundAudioAnalysis(PlexSession session, AudioAnalysisService analysis, MelodayEngine engine, PlayerService player, SettingsStore settings,
        ILogger<BackgroundAudioAnalysis> log)
    {
        _session = session; _analysis = analysis; _player = player; _log = log;
        _settings = settings;
        _engine = engine;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _timer = _dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += async (_, _) =>
        {
            try { await WorkAsync(); }
            finally { ScheduleNext(); }
        };
        _session.StateChanged += OnSessionChanged;
        _player.PropertyChanged += OnPlayerChanged;
        _analysis.Completed += OnCompleted;
        ScheduleNext();
        OnSessionChanged(this, EventArgs.Empty);
        if (_player.CurrentTrack is { } current) _analysis.RequestPriority([current]);
    }

    private void OnSessionChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        var scope = _session.IsReady ? $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}" : null;
        if (scope == _scope) return;
        _lifetime.Cancel(); _lifetime.Dispose(); _lifetime = new();
        _analysis.ClearPriority();
        _scope = scope; _cursor = 0; _reachedEnd = false; _nextSweep = default; _page.Clear(); _failed.Clear();
        if (scope is not null)
            _analysis.RequestPriority((_player.CurrentTrack is { } current ? new[] { current } : Array.Empty<Track>())
                .Concat(_player.Queue.UpNext.Select(e => e.Track)).Take(10));
        _coverage.Clear(); _total = 0; _inventoryKnown = false; _nextInventory = default;
        Report(scope is null ? "Waiting for your music library" : "Checking saved analysis");
        if (!_working) ScheduleNext();
    });

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerService.CurrentTrack) && _player.CurrentTrack is { } track)
        {
            _analysis.RequestPriority([track]);
            if (FastMode && !_working) ScheduleNext();
        }
    }

    private void OnCompleted(Track track, bool head, AudioAnalysis result)
    {
        var scope = $"{_session.Server?.MachineIdentifier}|{_session.Section?.Key}";
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || scope != _scope) return;
            Observe(track, head ? result : null, head ? null : result);
            Report();
        });
    }

    private async Task WorkAsync()
    {
        if (_working || _disposed) return;
        if (!_session.IsReady) { Report("Waiting for your music library"); return; }
        if (_player.IsBuffering) { Report("Paused while playback buffers"); return; }
        if (!HasBackgroundBudget()) { Report("Paused for energy saver or a metered connection"); return; }
        if (_session.Client is not { } client || _session.Section is not { } section) return;
        if (!_inventoryWorking && DateTimeOffset.UtcNow >= _nextInventory) _ = InventoryAsync();
        if (_nextSweep > DateTimeOffset.UtcNow && !_analysis.HasPriority)
        {
            Report(_inventoryKnown && _total == 0 ? "Your library has no tracks" : Summary.Complete == _total && _engine.Count >= _total && _inventoryKnown
                ? "Up to date · watching for new music" : "Waiting to retry remaining tracks");
            return;
        }
        _working = true;
        var ct = _lifetime.Token;
        try
        {
            for (var inspected = 0; inspected < 100 && !ct.IsCancellationRequested; inspected++)
            {
                Track? track;
                if (!_analysis.TryPriority(out track))
                {
                    if (_page.Count == 0)
                    {
                        if (_reachedEnd)
                        {
                            _reachedEnd = false;
                            _nextSweep = DateTimeOffset.UtcNow.AddMinutes(15);
                            return;
                        }
                        PagedResult<Track> page;
                        using (PlexServerClient.Uncached()) page = await client.GetTracksAsync(section.Key, _cursor, 100, "titleSort", ct);
                        if (ct.IsCancellationRequested || _session.Client != client) return;
                        foreach (var t in page.Items) _page.Enqueue(t);
                        _reachedEnd = page.Items.Count < 100;
                        _cursor = _reachedEnd ? 0 : _cursor + page.Items.Count;
                        if (_page.Count == 0) return;
                    }
                    track = _page.Dequeue();
                }
                if (track is null) continue;
                if (_failed.TryGetValue(track.RatingKey, out var retry) && retry > DateTimeOffset.UtcNow) continue;
                if (IsComplete(track) && _engine.Knows(track)) continue;
                var (_, head, tail, heard) = (await ReadCachedAsync([track], ct))[0];
                if (ct.IsCancellationRequested || _session.Client != client || _player.IsBuffering) return;
                Observe(track, head, tail);
                var (hasHead, hasTail) = (_coverage[track.RatingKey].Head is not null, _coverage[track.RatingKey].Tail is not null);
                if (!heard)
                {
                    Report($"ME is learning the sound of {track.Title}");
                    var profile = await _analysis.WarmEngineAsync(track, ct, fast: FastMode);
                    if (ct.IsCancellationRequested) return;
                    if (profile is not null) _analysis.RequestPriority([track]);
                    else _failed[track.RatingKey] = DateTimeOffset.UtcNow.AddMinutes(15);
                    Report(profile is null ? _engine.Error ?? "Track unavailable · continuing" : "Analysing your library in the background");
                    return;
                }
                if (hasHead && hasTail) continue;
                Report($"Analysing {track.Title}");
                var analysed = await _analysis.WarmAsync(track, head: !hasHead, ct, fast: FastMode);
                if (ct.IsCancellationRequested) return;
                if (analysed is null)
                {
                    if (_failed.Count >= 500) _failed.Clear();
                    _failed[track.RatingKey] = DateTimeOffset.UtcNow.AddMinutes(15);
                }
                else
                {
                    Observe(track, hasHead ? null : analysed, hasHead ? analysed : null);
                    if (!hasHead && !hasTail) _analysis.RequestPriority([track]);
                }
                Report(analysed is null ? "Track unavailable · continuing with other music" : "Analysing your library in the background");
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _log.LogDebug(ex, "Background audio analysis deferred"); Report("Library unavailable · retrying automatically"); }
        finally { _working = false; }
    }

    private bool IsComplete(Track track) => _coverage.TryGetValue(track.RatingKey, out var known) && known.Head is not null && known.Tail is not null;

    private Task<(Track Track, AudioAnalysis? Head, AudioAnalysis? Tail, bool Heard)[]> ReadCachedAsync(IReadOnlyList<Track> tracks, CancellationToken ct)
    {
        var complete = tracks.Where(IsComplete).Select(t => t.RatingKey).ToHashSet(StringComparer.Ordinal);
        return Task.Run(async () =>
        {
            var found = new (Track, AudioAnalysis?, AudioAnalysis?, bool)[tracks.Count];
            for (var i = 0; i < tracks.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var track = tracks[i];
                var known = complete.Contains(track.RatingKey);
                var head = known ? null : await _analysis.CachedHeadAsync(track).ConfigureAwait(false);
                var tail = known ? null : await _analysis.CachedTailAsync(track).ConfigureAwait(false);
                found[i] = (track, head, tail, await _engine.CachedAsync(track, ct).ConfigureAwait(false) is not null);
            }
            return found;
        }, ct);
    }

    private void Observe(Track track, AudioAnalysis? head, AudioAnalysis? tail)
    {
        _coverage.TryGetValue(track.RatingKey, out var previous);
        _coverage[track.RatingKey] = (head is { } h ? AnalysisNote.Of(h) : previous.Head, tail is { } t ? AnalysisNote.Of(t) : previous.Tail);
    }

    private async Task InventoryAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return;
        _inventoryWorking = true;
        var ct = _lifetime.Token;
        var seen = new HashSet<string>();
        try
        {
            for (var offset = 0; ;)
            {
                PagedResult<Track> page;
                using (PlexServerClient.Uncached()) page = await client.GetTracksAsync(section.Key, offset, 500, "titleSort", ct);
                if (ct.IsCancellationRequested || _session.Client != client) return;
                if (page.TotalSize is { } total) _total = total;
                var found = await ReadCachedAsync(page.Items, ct);
                if (ct.IsCancellationRequested || _session.Client != client) return;
                foreach (var (track, head, tail, _) in found)
                {
                    seen.Add(track.RatingKey);
                    Observe(track, head, tail);
                }
                Report();
                offset += page.Items.Count;
                if (page.Items.Count == 0 || !page.HasMore) break;
                await Task.Delay(100, ct);
            }
            foreach (var removed in _coverage.Keys.Where(k => !seen.Contains(k)).ToArray()) _coverage.Remove(removed);
            _total = seen.Count;
            _engine.Reconcile(seen, _scope ?? string.Empty);
            _inventoryKnown = true;
            Report(_total == 0 ? "Your library has no tracks" : null);
            _log.LogInformation("Audio analysis coverage: {Complete}/{Total} tracks, {Partial} partial, {Percent:0.0}%", Summary.Complete, Summary.Total, Summary.Partial, Summary.Percent);
            _nextInventory = DateTimeOffset.UtcNow.AddMinutes(15);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Audio analysis inventory deferred");
            if (!ct.IsCancellationRequested) { _nextInventory = DateTimeOffset.UtcNow.AddMinutes(1); Report("Library unavailable · retrying automatically"); }
        }
        finally { _inventoryWorking = false; }
    }

    private static bool HasBackgroundBudget()
    {
        try
        {
            if (PowerManager.EnergySaverStatus == EnergySaverStatus.On) return false;
            var cost = NetworkInformation.GetInternetConnectionProfile()?.GetConnectionCost();
            return cost is null || !cost.Roaming && !cost.OverDataLimit
                && cost.NetworkCostType is not (NetworkCostType.Fixed or NetworkCostType.Variable);
        }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _lifetime.Cancel();
        _session.StateChanged -= OnSessionChanged;
        _player.PropertyChanged -= OnPlayerChanged;
        _analysis.Completed -= OnCompleted;
    }
}
