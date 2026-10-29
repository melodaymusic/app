using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using MelodayMusic.Core.Settings;

namespace MelodayMusic.Core.Tests;

public class PlaybackQueueTests
{
    private static List<Track> Tracks(int n) =>
        Enumerable.Range(1, n).Select(i => new Track { RatingKey = i.ToString(), Title = $"T{i}" }).ToList();

    private static string Keys(PlaybackQueue q) => string.Join(",", q.Items.Select(e => e.Track.RatingKey));

    [Fact]
    public void SetQueue_starts_at_requested_index()
    {
        var q = new PlaybackQueue();
        var current = q.SetQueue(Tracks(5), 2);
        Assert.Equal("3", current!.Track.RatingKey);
        Assert.Equal(2, q.CurrentIndex);
        Assert.Equal(2, q.History.Count());
        Assert.Equal(2, q.UpNext.Count());
    }

    [Fact]
    public void MoveNext_stops_at_end_without_repeat()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(2));
        Assert.Equal("2", q.MoveNext(false)!.Track.RatingKey);
        Assert.Null(q.MoveNext(false));
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Repeat_all_wraps_to_start()
    {
        var q = new PlaybackQueue { Repeat = RepeatMode.All };
        q.SetQueue(Tracks(2), 1);
        Assert.Equal("1", q.MoveNext(false)!.Track.RatingKey);
    }

    [Fact]
    public void Repeat_one_repeats_on_natural_end_but_next_button_advances()
    {
        var q = new PlaybackQueue { Repeat = RepeatMode.One };
        q.SetQueue(Tracks(3));
        Assert.Equal("1", q.MoveNext(false)!.Track.RatingKey);
        Assert.Equal("2", q.MoveNext(true)!.Track.RatingKey);
    }

    [Fact]
    public void Previous_at_start_stays_without_repeat_and_wraps_with_repeat()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(3));
        Assert.Equal("1", q.MovePrevious()!.Track.RatingKey);
        q.Repeat = RepeatMode.All;
        Assert.Equal("3", q.MovePrevious()!.Track.RatingKey);
    }

    [Fact]
    public void Shuffle_keeps_current_first_and_contains_everything_once()
    {
        var q = new PlaybackQueue(new Random(42));
        q.SetQueue(Tracks(20), 7);
        q.SetShuffle(true);
        Assert.Equal("8", q.Current!.Track.RatingKey);
        Assert.Equal(0, q.CurrentIndex);
        Assert.Equal(20, q.Items.Select(e => e.Track.RatingKey).Distinct().Count());
    }

    [Fact]
    public void Unshuffle_restores_original_order_and_keeps_current()
    {
        var q = new PlaybackQueue(new Random(1));
        q.SetQueue(Tracks(10), 3);
        q.SetShuffle(true);
        q.MoveNext(true);
        var playing = q.Current!;
        q.SetShuffle(false);
        Assert.Equal("1,2,3,4,5,6,7,8,9,10", Keys(q));
        Assert.Same(playing, q.Current);
    }

    [Fact]
    public void SetQueue_with_shuffle_plays_start_track_first()
    {
        var q = new PlaybackQueue(new Random(3));
        var first = q.SetQueue(Tracks(10), 4, shuffle: true);
        Assert.Equal("5", first!.Track.RatingKey);
        Assert.True(q.IsShuffled);
    }

    [Fact]
    public void PlayNext_inserts_after_current()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(3));
        q.PlayNext([new Track { RatingKey = "x", Title = "X" }]);
        Assert.Equal("1,x,2,3", Keys(q));
        Assert.Equal("x", q.MoveNext(true)!.Track.RatingKey);
    }

    [Fact]
    public void Enqueue_on_empty_queue_sets_current()
    {
        var q = new PlaybackQueue();
        q.Enqueue(Tracks(2));
        Assert.Equal(0, q.CurrentIndex);
        Assert.Equal("1,2", Keys(q));
    }

    [Fact]
    public void Remove_before_current_keeps_current_entry()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(4), 2);
        var current = q.Current!;
        var wasCurrent = q.Remove(q.Items[0].Id);
        Assert.False(wasCurrent);
        Assert.Same(current, q.Current);
        Assert.Equal(1, q.CurrentIndex);
    }

    [Fact]
    public void Remove_current_reports_it_and_next_entry_becomes_current()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(3), 1);
        Assert.True(q.Remove(q.Current!.Id));
        Assert.Equal("3", q.Current!.Track.RatingKey);
    }

    [Fact]
    public void Remove_last_entry_empties_queue()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(1));
        q.Remove(q.Current!.Id);
        Assert.Null(q.Current);
        Assert.Equal(-1, q.CurrentIndex);
    }

    [Fact]
    public void Move_reorders_and_tracks_current()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(4), 1);
        q.Move(3, 0);
        Assert.Equal("4,1,2,3", Keys(q));
        Assert.Equal("2", q.Current!.Track.RatingKey);
        Assert.Equal(2, q.CurrentIndex);
    }

    [Fact]
    public void JumpTo_selects_entry()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(4));
        var target = q.Items[3];
        Assert.Same(target, q.JumpTo(target.Id));
        Assert.Equal(3, q.CurrentIndex);
    }

    [Fact]
    public void ClearUpNext_keeps_history_and_current()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(5), 2);
        q.ClearUpNext();
        Assert.Equal("1,2,3", Keys(q));
        Assert.Equal("3", q.Current!.Track.RatingKey);
    }

    [Fact]
    public void Clear_keep_current()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(5), 2);
        q.Clear(keepCurrent: true);
        Assert.Equal("3", Keys(q));
        Assert.Equal(0, q.CurrentIndex);
    }

    [Fact]
    public void Duplicate_tracks_are_distinct_entries()
    {
        var q = new PlaybackQueue();
        var t = new Track { RatingKey = "1", Title = "Same" };
        q.SetQueue([t, t]);
        Assert.NotEqual(q.Items[0].Id, q.Items[1].Id);
        q.Remove(q.Items[1].Id);
        Assert.Single(q.Items);
    }

    [Fact]
    public void Restore_shuffled_queue_can_unshuffle()
    {
        var q = new PlaybackQueue();
        var t = Tracks(3);
        q.Restore([t[2], t[0], t[1]], t, 0, shuffled: true);
        Assert.True(q.IsShuffled);
        q.SetShuffle(false);
        Assert.Equal("1,2,3", Keys(q));
        Assert.Equal("3", q.Current!.Track.RatingKey);
    }

    [Fact]
    public void Changed_event_fires()
    {
        var q = new PlaybackQueue();
        var count = 0;
        q.Changed += (_, _) => count++;
        q.SetQueue(Tracks(2));
        q.MoveNext(true);
        Assert.Equal(2, count);
    }

    [Fact]
    public void Insert_after_the_current_track_keeps_it_playing()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(4));
        q.MoveNext(true);
        q.InsertAt(3, [new Track { RatingKey = "x", Title = "X" }]);
        Assert.Equal("T2", q.Current!.Track.Title);
        Assert.Equal(["T1", "T2", "T3", "X", "T4"], q.Items.Select(e => e.Track.Title));
    }

    [Fact]
    public void Insert_before_the_current_track_shifts_it_along()
    {
        var q = new PlaybackQueue();
        q.SetQueue(Tracks(3));
        q.MoveNext(true);
        q.InsertAt(0, [new Track { RatingKey = "x", Title = "X" }, new Track { RatingKey = "y", Title = "Y" }]);
        Assert.Equal("T2", q.Current!.Track.Title);
        Assert.Equal(3, q.CurrentIndex);
    }
}
