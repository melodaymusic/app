using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed partial class PlayerService
{
    private const int CuratedWindow = 200;
    private const int SmoothWindow = 25;
    private int _curatorGeneration;
    private readonly HashSet<Guid> _bridges = [];
    private readonly HashSet<(Guid, Guid)> _bridgeLooked = [];
    private bool _bridging;
    private const int BridgeAhead = 2;
    private const int BridgeSongs = 5;
    private Guid[]? _curatorBaseline;
    private DateTimeOffset _lastCuratorRefinement;

    private void OnAnalysisAvailable(object? sender, EventArgs e) => _dispatcher.TryEnqueue(() =>
    {
        if (Curator is not (Curator.Smooth or Curator.Twin or Curator.Drift) || IsLive || DateTimeOffset.UtcNow - _lastCuratorRefinement < TimeSpan.FromSeconds(5)) return;
        _lastCuratorRefinement = DateTimeOffset.UtcNow;
        _ = ApplyCuratorToUpNextAsync(announce: false);
    });

    [ObservableProperty] public partial Curator Curator { get; private set; }

    private void EndCurator()
    {
        _bridges.Clear();
        _bridgeLooked.Clear();
        _curatorBaseline = null;
        _curatorGeneration++;
        if (Curator == Curator.None) return;
        Curator = Curator.None;
        _settings.Current.Curator = Curator.None;
        _settings.Save();
    }

    public void SetCurator(Curator curator)
    {
        if (IsLive)
        {
            _notifications.ShowInfo("Not on live radio", $"{LiveStation} plays its own order. Guest curators work on everything else.");
            return;
        }
        if (curator == Curator) return;
        if (Curator == Curator.None) _curatorBaseline = Queue.UpNext.Select(e => e.Id).ToArray();
        _curatorGeneration++;
        var leaving = Curator;
        Curator = curator;
        if (leaving == Curator.Bridge) RemoveBridges();
        if (curator == Curator.None && _curatorBaseline is { } baseline)
        {
            Queue.ReorderUpNextEntries(baseline);
            _curatorBaseline = null;
        }
        if (curator == Curator.Smooth)
            _analysis.RequestPriority((CurrentTrack is { } seed ? new[] { seed } : Array.Empty<Track>())
                .Concat(Queue.UpNext.Select(e => e.Track)).Take(SmoothWindow));
        _settings.Current.Curator = curator;
        _settings.Save();
        if (curator is Curator.Twin or Curator.Drift)
            _ = App.Services.GetRequiredService<MusicIntelligence>().WarmCuratorAsync(
                (CurrentTrack is null ? Enumerable.Empty<Track>() : new[] { CurrentTrack }).Concat(Queue.UpNext.Select(e => e.Track)));
        _ = ApplyCuratorToUpNextAsync(announce: true);
    }

    private async Task ApplyCuratorToUpNextAsync(bool announce)
    {
        if (IsLive) return;
        var curator = Curator;
        var generation = _curatorGeneration;
        var entries = Queue.UpNext.ToArray();
        var upcoming = entries.Select(e => e.Track).ToList();
        if (curator == Curator.Bridge)
        {
            if (announce) _notifications.ShowSuccess("Guest curator: " + Curators.Name(curator), Curators.Description(curator));
            await BridgeAsync().ConfigureAwait(true);
            return;
        }
        if (curator == Curator.None || upcoming.Count < 2)
        {
            if (announce) _notifications.ShowInfo("Guest curator: " + Curators.Name(curator), upcoming.Count < 2 && curator != Curator.None ? "It arranges what's coming up as songs are queued." : Curators.Description(curator));
            return;
        }
        var ordered = await CuratedAsync(curator, CurrentTrack, upcoming).ConfigureAwait(true);
        if (generation != _curatorGeneration || curator != Curator || IsLive
            || !entries.Select(e => e.Id).SequenceEqual(Queue.UpNext.Select(e => e.Id))) return;
        Queue.ReorderUpNext(ordered);
        if (announce) _notifications.ShowSuccess("Guest curator: " + Curators.Name(curator), Curators.Description(curator));
    }

    private async Task<IReadOnlyList<Track>> CuratedAsync(Curator curator, Track? seed, IReadOnlyList<Track> tracks)
    {
        if (curator == Curator.None || tracks.Count < 2) return tracks;
        if (curator == Curator.Smooth)
        {
            var window = tracks.Take(SmoothWindow).ToList();
            var items = await Task.WhenAll(window.Select(async t => new SmartOrderItem(t,
                await _analysis.CachedHeadAsync(t).ConfigureAwait(false),
                await _analysis.CachedTailAsync(t).ConfigureAwait(false)))).ConfigureAwait(false);
            var tail = seed is null ? null : await _analysis.CachedTailAsync(seed).ConfigureAwait(false);
            var smoothOrder = await Task.Run(() => SmartOrder.Optimize(items, seed, tail)).ConfigureAwait(false);
            return [.. smoothOrder, .. tracks.Skip(SmoothWindow)];
        }
        var front = tracks.Take(CuratedWindow).ToList();
        var intelligence = App.Services.GetRequiredService<MusicIntelligence>();
        var distance = intelligence.CaptureDistances();
        if (curator is Curator.Twin or Curator.Drift && intelligence.IsMeloday)
            _analysis.RequestPriority((seed is null ? Enumerable.Empty<Track>() : new[] { seed }).Concat(front));
        var ordered = await Task.Run(() => Curators.Order(curator, seed, front,
            (a, b) => distance(a, b) ?? Curators.Distance(a, b))).ConfigureAwait(false);
        return [.. ordered, .. tracks.Skip(CuratedWindow)];
    }

    internal async Task BridgeAsync()
    {
        if (_bridging || Curator != Curator.Bridge || IsLive) return;
        var intelligence = App.Services.GetRequiredService<MusicIntelligence>();
        if (!intelligence.IsAvailable) return;
        _bridging = true;
        try
        {
            var disliked = await App.Services.GetRequiredService<Dislikes>().GetAsync().ConfigureAwait(true);
            for (var step = 0; step < BridgeAhead && Curator == Curator.Bridge; step++)
            {
                if (Queue.CurrentIndex < 0) return;
                var own = Queue.Items.Skip(Queue.CurrentIndex).Where(e => !_bridges.Contains(e.Id)).Take(BridgeAhead + 1).ToList();
                (QueueEntry A, QueueEntry B)? pair = null;
                for (var i = 0; i + 1 < own.Count; i++)
                    if (!_bridgeLooked.Contains((own[i].Id, own[i + 1].Id)))
                    {
                        pair = (own[i], own[i + 1]);
                        break;
                    }
                if (pair is not { } next) return;
                _bridgeLooked.Add((next.A.Id, next.B.Id));
                var queued = new SongSet(Queue.Items.Select(e => e.Track));
                var stretch = await intelligence.StretchAsync(next.A.Track, next.B.Track, BridgeSongs,
                    t => disliked.Eligible(t) && !queued.Contains(t)).ConfigureAwait(true);
                var songs = new SongSet(Queue.Items.Select(e => e.Track)).AddNew(stretch);
                if (songs.Count == 0 || Curator != Curator.Bridge) continue;
                var at = Queue.Items.ToList().FindIndex(e => e.Id == next.B.Id);
                if (at <= Queue.CurrentIndex || at < 1 || Queue.Items[at - 1].Id != next.A.Id) continue;
                Queue.InsertAt(at, songs);
                foreach (var entry in Queue.Items.Skip(at).Take(songs.Count)) _bridges.Add(entry.Id);
            }
        }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _log.LogInformation("Bridge: no way between two songs this time ({Message})", ex.Message);
        }
        finally
        {
            _bridging = false;
        }
    }

    private void RemoveBridges()
    {
        foreach (var entry in Queue.UpNext.Where(e => _bridges.Contains(e.Id)).ToList()) Queue.Remove(entry.Id);
        _bridges.Clear();
        _bridgeLooked.Clear();
    }

    private async Task<IReadOnlyList<Track>> CuratedBatchAsync(IReadOnlyList<Track> batch)
    {
        if (IsLive || Curator == Curator.None || batch.Count < 2) return batch;
        var seed = Queue.Items.LastOrDefault()?.Track ?? CurrentTrack;
        return await CuratedAsync(Curator, seed, batch).ConfigureAwait(false);
    }
}
