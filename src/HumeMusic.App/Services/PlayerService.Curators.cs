using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playback;

namespace HumeMusic.Services;

public sealed partial class PlayerService
{
    /// <summary>The most songs a curator arranges at once (a queue of a whole library is arranged from its front).</summary>
    private const int CuratedWindow = 200;
    /// <summary>The most songs Smooth looks at the audio of at once.</summary>
    private const int SmoothWindow = 25;

    /// <summary>The guest curator arranging what plays next (never on live radio).</summary>
    [ObservableProperty] public partial Curator Curator { get; private set; }

    /// <summary>Chooses the guest curator: what is coming up is arranged by it now, and so is whatever is added after.</summary>
    public void SetCurator(Curator curator)
    {
        if (IsLive)
        {
            _notifications.ShowInfo("Not on live radio", $"{LiveStation} plays its own order. Guest curators work on everything else.");
            return;
        }
        if (curator == Curator) return;
        Curator = curator;
        _settings.Current.Curator = curator;
        _settings.Save();
        _ = ApplyCuratorToUpNextAsync(announce: true);
    }

    /// <summary>
    /// Arranges Up Next by the curator. The song playing and the history never move. Choosing Smooth is the listener's own
    /// action, so audio not analysed yet may be (for the first few songs); otherwise only what is already known is used.
    /// </summary>
    private async Task ApplyCuratorToUpNextAsync(bool announce)
    {
        if (IsLive) return;
        var curator = Curator;
        var upcoming = Queue.UpNext.Select(e => e.Track).ToList();
        if (curator == Curator.None || upcoming.Count < 2)
        {
            if (announce) _notifications.ShowInfo("Guest curator: " + Curators.Name(curator), upcoming.Count < 2 && curator != Curator.None ? "It arranges what's coming up as songs are queued." : Curators.Description(curator));
            return;
        }
        var ordered = await CuratedAsync(curator, CurrentTrack, upcoming, allowAnalysis: announce).ConfigureAwait(true);
        // The listener may have changed curator, or what is queued, while the audio was looked at.
        if (curator != Curator || IsLive) return;
        Queue.ReorderUpNext(ordered);
        if (announce) _notifications.ShowSuccess("Guest curator: " + Curators.Name(curator), Curators.Description(curator));
    }

    /// <summary>The songs arranged by a curator (the same songs, none added or dropped).</summary>
    private async Task<IReadOnlyList<Track>> CuratedAsync(Curator curator, Track? seed, IReadOnlyList<Track> tracks, bool allowAnalysis)
    {
        if (curator == Curator.None || tracks.Count < 2) return tracks;
        if (curator == Curator.Smooth)
        {
            var window = tracks.Take(SmoothWindow).ToList();
            var items = await Task.WhenAll(window.Select(async t => new SmartOrderItem(t,
                allowAnalysis ? await _analysis.HeadAsync(t).ConfigureAwait(false) : await _analysis.CachedHeadAsync(t).ConfigureAwait(false),
                allowAnalysis ? await _analysis.TailAsync(t).ConfigureAwait(false) : await _analysis.CachedTailAsync(t).ConfigureAwait(false)))).ConfigureAwait(false);
            var tail = seed is null ? null : allowAnalysis ? await _analysis.TailAsync(seed).ConfigureAwait(false) : await _analysis.CachedTailAsync(seed).ConfigureAwait(false);
            return [.. SmartOrder.Optimize(items, seed, tail), .. tracks.Skip(SmoothWindow)];
        }
        var front = tracks.Take(CuratedWindow).ToList();
        return [.. Curators.Order(curator, seed, front), .. tracks.Skip(CuratedWindow)];
    }

    /// <summary>A batch about to join the queue (a radio's next songs), arranged by the curator after the last song queued.</summary>
    private async Task<IReadOnlyList<Track>> CuratedBatchAsync(IReadOnlyList<Track> batch)
    {
        if (IsLive || Curator == Curator.None || batch.Count < 2) return batch;
        var seed = Queue.Items.LastOrDefault()?.Track ?? CurrentTrack;
        return await CuratedAsync(Curator, seed, batch, allowAnalysis: false).ConfigureAwait(false);
    }
}
