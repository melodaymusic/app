using MelodayMusic.Core.Playback;

namespace MelodayMusic.Services;

public sealed partial class PlayerService
{
    public async Task OptimizeUpNextAsync()
    {
        var upcomingEntries = Queue.UpNext.ToList();
        if (upcomingEntries.Count < 2)
        {
            _notifications.ShowInfo("Nothing to optimize", "Queue a few more songs first.");
            return;
        }

        var analyses = await Task.WhenAll(upcomingEntries.Select(async entry => new SmartOrderItem(
            entry.Track,
            await _analysis.HeadAsync(entry.Track).ConfigureAwait(false),
            await _analysis.TailAsync(entry.Track).ConfigureAwait(false)))).ConfigureAwait(false);

        var currentTail = CurrentTrack is { } current
            ? await _analysis.TailAsync(current).ConfigureAwait(false)
            : null;
        var orderedTracks = SmartOrder.Optimize(analyses, CurrentTrack, currentTail);

        var remaining = upcomingEntries.ToList();
        var orderedEntries = new List<QueueEntry>(remaining.Count);
        foreach (var track in orderedTracks)
        {
            var match = remaining.FindIndex(e => ReferenceEquals(e.Track, track) || e.Track.RatingKey == track.RatingKey);
            if (match < 0) continue;
            orderedEntries.Add(remaining[match]);
            remaining.RemoveAt(match);
        }
        orderedEntries.AddRange(remaining);

        var first = Queue.CurrentIndex + 1;
        for (var offset = 0; offset < orderedEntries.Count; offset++)
        {
            var target = first + offset;
            var from = Queue.Items.ToList().FindIndex(e => e.Id == orderedEntries[offset].Id);
            if (from >= 0 && from != target) Queue.Move(from, target);
        }

        _notifications.ShowSuccess("Queue optimized", "Up Next is arranged for smoother tempo, key and energy changes.");
    }
}
