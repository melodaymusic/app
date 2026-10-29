using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class LiveRotationTests
{
    [Fact]
    public void Candidate_selection_uses_saved_history_before_building_a_shortlist()
    {
        var old = T("old", "artist");
        var duplicate = T("deluxe", "artist", "old");
        var otherSong = T("other", "artist");
        var fresh = T("fresh", "new artist");
        List<RotationPlay> history = [P(old, Now.AddMinutes(-5))];
        Assert.True(LiveRotation.IsResting(old, history, Now));
        Assert.True(LiveRotation.IsResting(duplicate, history, Now));
        Assert.True(LiveRotation.IsResting(otherSong, history, Now));
        Assert.False(LiveRotation.IsResting(fresh, history, Now));
        Assert.True(LiveRotation.CandidatePenalty(old, history, Now) > LiveRotation.CandidatePenalty(otherSong, history, Now));
        Assert.True(LiveRotation.CandidatePenalty(otherSong, history, Now) > LiveRotation.CandidatePenalty(fresh, history, Now));
        Assert.Equal(0, LiveRotation.CandidatePenalty(old, history, Now.AddDays(2)));
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static Track T(string key, string artist, string? title = null) => new()
    { RatingKey = key, Title = title ?? key, ArtistRatingKey = artist, ArtistTitle = artist, DurationMs = 240_000 };
    private static RotationPlay P(Track t, DateTimeOffset at, bool featured = false) =>
        new(t.RatingKey, LiveRotation.ArtistOf(t), LiveRotation.SongOf(t), at, featured);

    [Fact]
    public void A_recent_song_and_its_deluxe_duplicate_wait_when_other_music_is_available()
    {
        var played = T("old", "a", "Song");
        List<RotationPlay> history = [P(played, Now.AddMinutes(-5))];
        var pool = Enumerable.Range(1, 80).Select(i => new BlockPick(T("new" + i, "b" + i), 0))
            .Append(new BlockPick(T("deluxe", "a", " Song "), 0)).Append(new BlockPick(played, 0));
        var picked = LiveRotation.Pick(pool, history, Now, 20, new Random(1));
        Assert.Equal(20, picked.Count);
        Assert.DoesNotContain(picked, p => p.Track.RatingKey is "old" or "deluxe");
    }

    [Fact]
    public void Artists_are_spaced_across_batch_boundaries()
    {
        var pool = Enumerable.Range(0, 12).SelectMany(a => Enumerable.Range(0, 8)
            .Select(s => new BlockPick(T($"{a}-{s}", a.ToString()), 0))).ToList();
        List<RotationPlay> history = [];
        var first = LiveRotation.Pick(pool, history, Now, 20, new Random(2));
        var second = LiveRotation.Pick(pool, history, Now.AddMinutes(80), 20, new Random(3));
        var all = first.Concat(second).ToList();
        Assert.Equal(40, all.Count);
        for (var i = 0; i < all.Count; i++)
            Assert.DoesNotContain(all.Skip(Math.Max(0, i - LiveRotation.ArtistGap)).Take(Math.Min(i, LiveRotation.ArtistGap)),
                p => p.Track.ArtistRatingKey == all[i].Track.ArtistRatingKey);
        Assert.Equal(40, all.Select(p => p.Track.RatingKey).Distinct().Count());
    }

    [Fact]
    public void Expired_schedule_history_survives_disk_reload_and_changes_the_opening()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var old = T("old", "a");
            var schedule = LiveSchedule.Begin([new LiveEntry(old.RatingKey, 240_000)], Now.AddMinutes(-10), TimeSpan.Zero);
            schedule.Rotation.Add(P(old, Now.AddMinutes(-10)));
            new LiveScheduleStore(path).Set("server|library|station", schedule);
            var restored = new LiveScheduleStore(path).Get("server|library|station")!;
            Assert.Null(restored.Locate(Now));
            var picks = LiveRotation.Pick([new(old, 0), new(T("fresh", "b"), 0)], restored.Rotation, Now, 1, new Random(1));
            Assert.Equal("fresh", Assert.Single(picks).Track.RatingKey);
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public void Two_song_library_relaxes_without_repeating_the_last_song()
    {
        var a = T("a", "one"); var b = T("b", "one");
        List<RotationPlay> history = [P(a, Now.AddMinutes(-8)), P(b, Now.AddMinutes(-4))];
        var pick = LiveRotation.Pick([new(a, 0), new(b, 0)], history, Now, 1);
        Assert.Equal("a", Assert.Single(pick).Track.RatingKey);
    }

    [Fact]
    public void Artist_feature_cannot_follow_a_recent_feature_even_on_restart()
    {
        List<RotationPlay> history = [P(T("previous", "other"), Now.AddMinutes(-30), true)];
        var candidates = Enumerable.Range(1, 3).Select(i => new BlockPick(T("a" + i, "a"), 1))
            .Concat(Enumerable.Range(1, 30).Select(i => new BlockPick(T("b" + i, "b" + i), 0)));
        var picks = LiveRotation.Pick(candidates, history, Now, 20, new Random(4));
        Assert.All(picks, p => Assert.Equal(0, p.Group));
    }

    [Fact]
    public void Eligible_feature_keeps_consecutive_songs_and_restores_cooldown()
    {
        List<RotationPlay> history = [];
        var picks = LiveRotation.Pick(Enumerable.Range(1, 3).Select(i => new BlockPick(T("a" + i, "a"), 1)), history, Now, 3);
        Assert.Equal(3, picks.Count);
        Assert.All(picks, p => Assert.Equal(1, p.Group));
        Assert.All(history, p => Assert.True(p.Featured));
    }

    [Fact]
    public void Blocks_are_offered_only_once_every_three_programme_turns()
    {
        var pool = Enumerable.Range(1, 40).SelectMany(a => Enumerable.Range(1, 4).Select(s => T($"{a}-{s}", a.ToString()))).ToList();
        var turns = Enumerable.Range(0, 3).Select(i => HeadlinerProgram.Build(new(pool, [], []), i, new Random(i))).ToList();
        Assert.All(turns.Take(2), picks => Assert.All(picks, p => Assert.Equal(0, p.Group)));
        Assert.Single(turns[2].Where(p => p.Group > 0).Select(p => p.Group).Distinct());
    }

    [Fact]
    public void Legacy_schedule_import_and_repeat_after_cooldown_keep_the_timeline_valid()
    {
        var schedule = LiveSchedule.Begin([new LiveEntry("a", 240_000, 0, "artist", "song")], Now, TimeSpan.Zero);
        schedule.ImportRotation(); schedule.ImportRotation();
        Assert.Single(schedule.Rotation);
        schedule.AppendProgrammed([new LiveEntry("a", 240_000)]);
        Assert.Equal((1, 0), schedule.Locate(Now.AddMinutes(4)));
    }

    [Fact]
    public void Song_count_does_not_override_the_four_hour_rest()
    {
        var old = T("old", "old");
        List<RotationPlay> history = [P(old, Now.AddHours(-2))];
        history.AddRange(Enumerable.Range(0, 70).Select(i => P(T("history" + i, "h" + i), Now.AddMinutes(-70 + i))));
        var candidates = Enumerable.Range(0, 25).Select(i => new BlockPick(T("fresh" + i, "f" + i), 0)).Append(new(old, 0));
        Assert.DoesNotContain(LiveRotation.Pick(candidates, history, Now, 20), p => p.Track.RatingKey == "old");
    }

    [Fact]
    public void A_single_artist_pool_can_continue_after_a_mixed_library_becomes_unavailable()
    {
        var a = T("a", "one"); var b = T("b", "one");
        List<RotationPlay> history = [P(T("other", "other"), Now.AddMinutes(-12)), P(a, Now.AddMinutes(-8)), P(b, Now.AddMinutes(-4))];
        Assert.Equal("a", Assert.Single(LiveRotation.Pick([new(a, 0), new(b, 0)], history, Now, 1)).Track.RatingKey);
    }

    [Fact]
    public void Bounded_history_retains_the_newest_reservations()
    {
        var history = Enumerable.Range(0, LiveRotation.Remembered).Select(i => P(T("old" + i, "artist" + i), Now.AddDays(-10).AddMinutes(i))).ToList();
        LiveRotation.Pick([new(T("fresh", "fresh"), 0)], history, Now, 1);
        Assert.Equal(LiveRotation.Remembered, history.Count);
        Assert.Equal("fresh", history[^1].Key);
    }

    [Fact]
    public void Missing_artist_ids_use_names_for_duplicate_song_matching()
    {
        var a = new Track { RatingKey = "album", Title = "Song", ArtistTitle = "Artist" };
        var b = new Track { RatingKey = "deluxe", Title = " song ", ArtistTitle = " artist " };
        Assert.Equal(LiveRotation.SongOf(a), LiveRotation.SongOf(b));
    }
}
