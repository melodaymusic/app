using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class RadioPlaybackStoreTests
{
    [Fact]
    public void PlaybackSurvivesRestartWithoutCrossingAccountLibraryOrStation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var track = new Track { RatingKey = "42", Title = "Song", ArtistRatingKey = "7" };
            var now = DateTimeOffset.UtcNow;
            new RadioPlaybackStore(path).Record("user1/server/library/pulse", track, now);
            var reopened = new RadioPlaybackStore(path);
            Assert.Single(reopened.Get("user1/server/library/pulse"));
            Assert.True(LiveRotation.IsResting(track, reopened.Get("user1/server/library/pulse"), now.AddMinutes(1)));
            Assert.Empty(reopened.Get("user2/server/library/pulse"));
            Assert.Empty(reopened.Get("user1/server/other/pulse"));
            Assert.Empty(reopened.Get("user1/server/library/chill"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void RetainsBoundedHistory()
    {
        var store = new RadioPlaybackStore();
        var track = new Track { RatingKey = "42", Title = "Song" };
        for (var i = 0; i < LiveRotation.Remembered + 10; i++) store.Record("scope", track, DateTimeOffset.UnixEpoch.AddMinutes(i));
        Assert.Equal(LiveRotation.Remembered, store.Get("scope").Count);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(10), store.Get("scope")[0].At);
    }

    [Fact]
    public void OutOfOrderBackgroundWritesStillHaveChronologicalCooldowns()
    {
        var store = new RadioPlaybackStore();
        var track = new Track { RatingKey = "42", Title = "Song" };
        store.Record("scope", track, DateTimeOffset.UnixEpoch.AddMinutes(2));
        store.Record("scope", track, DateTimeOffset.UnixEpoch.AddMinutes(1));
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(2), store.Get("scope")[^1].At);
    }

    [Fact]
    public void InvalidHistoryCanBeReplacedAndLegacyScheduleIsClaimedOnce()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "invalid");
            var store = new RadioPlaybackStore(path);
            Assert.Empty(store.Get("scope"));
            store.Record("scope", new Track { RatingKey = "1", Title = "One" }, DateTimeOffset.UtcNow);
            Assert.Single(new RadioPlaybackStore(path).Get("scope"));
            var schedules = new LiveScheduleStore();
            schedules.Set("legacy", new LiveSchedule());
            Assert.NotNull(schedules.Take("legacy"));
            Assert.Null(schedules.Take("legacy"));
        }
        finally { File.Delete(path); }
    }
}
