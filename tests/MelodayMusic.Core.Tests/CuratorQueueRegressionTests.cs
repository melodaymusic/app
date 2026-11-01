using MelodayMusic.Core.Home;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class CuratorQueueRegressionTests
{
    [Fact]
    public void RestoringQueueOrderKeepsDuplicateOccurrencesAndNewSongs()
    {
        var a = new Track { RatingKey = "a", Title = "A" };
        var b = new Track { RatingKey = "b", Title = "B" };
        var queue = new PlaybackQueue();
        queue.SetQueue([new Track { RatingKey = "now", Title = "Now" }, a, b, a]);
        var current = queue.Current;
        var baseline = queue.UpNext.Select(e => e.Id).ToArray();
        queue.ReorderUpNextEntries(baseline.Reverse().ToArray());
        Assert.Equal(baseline.Reverse(), queue.UpNext.Select(e => e.Id));
        queue.Enqueue([new Track { RatingKey = "added", Title = "Added" }]);
        queue.ReorderUpNextEntries(baseline);
        Assert.Equal(baseline, queue.UpNext.Take(3).Select(e => e.Id));
        Assert.Equal("added", queue.UpNext.Last().Track.RatingKey);
        Assert.Same(current, queue.Current);
    }

    [Fact]
    public void RestoringAfterPlaybackAdvancesDoesNotBringPlayedEntriesBack()
    {
        var queue = new PlaybackQueue();
        queue.SetQueue(Enumerable.Range(0, 5).Select(i => new Track { RatingKey = i.ToString(), Title = i.ToString() }).ToList());
        var baseline = queue.UpNext.Select(e => e.Id).ToArray();
        queue.ReorderUpNextEntries(baseline.Reverse().ToArray());
        queue.MoveNext(false);
        var now = queue.Current;
        queue.ReorderUpNextEntries(baseline);
        Assert.Same(now, queue.Current);
        Assert.Equal(3, queue.UpNext.Count());
        Assert.DoesNotContain(queue.UpNext, e => e.Id == now!.Id);
    }

    [Fact]
    public void MovingTheSystemClockBackwardsMakesHomeRefreshDue()
    {
        var previous = new DateTimeOffset(2026, 10, 11, 18, 15, 0, TimeSpan.FromHours(-4));
        Assert.True(HomeRefresh.IsDue(previous, previous.AddMinutes(-5)));
    }
}
