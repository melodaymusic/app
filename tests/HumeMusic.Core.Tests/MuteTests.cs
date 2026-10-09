using HumeMusic.Core.Mutes;

namespace HumeMusic.Core.Tests;

public class MuteTests
{
    private sealed class Clock
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private static (MuteStore Store, Clock Clock, string Path) Make()
    {
        var clock = new Clock();
        var path = Path.Combine(Path.GetTempPath(), "meloday-mutes-" + Guid.NewGuid().ToString("N"), "mutes.json");
        return (new MuteStore(path, () => clock.Now), clock, path);
    }

    [Fact]
    public void A_muted_song_or_artist_is_kept_out_until_its_time_is_up()
    {
        var (store, clock, _) = Make();
        store.Mute("srv", MuteKind.Artist, "ar1", "The Killers", null, 7);
        store.Mute("srv", MuteKind.Song, "t9", "Mr. Brightside", "The Killers", 30);

        // The artist's songs are out, whichever song it is; the song is out whoever's artist key it comes with.
        Assert.True(store.IsMuted("srv", "t1", "ar1"));
        Assert.True(store.IsMuted("srv", "t9", "ar-other"));
        Assert.False(store.IsMuted("srv", "t1", "ar2"));
        // Another server's keys are not these.
        Assert.False(store.IsMuted("other", "t1", "ar1"));

        clock.Now = clock.Now.AddDays(7).AddMinutes(1);
        Assert.False(store.IsMuted("srv", "t1", "ar1"));
        Assert.True(store.IsMuted("srv", "t9", null));
        clock.Now = clock.Now.AddDays(24);
        Assert.False(store.IsMuted("srv", "t9", null));
        Assert.Empty(store.Active("srv"));
    }

    [Fact]
    public void Muting_again_starts_the_time_over_and_letting_back_ends_it()
    {
        var (store, clock, _) = Make();
        var first = store.Mute("srv", MuteKind.Song, "t1", "Song", "Artist", 7);
        clock.Now = clock.Now.AddDays(5);
        var second = store.Mute("srv", MuteKind.Song, "t1", "Song", "Artist", 7);
        Assert.True(second.Until > first.Until);
        Assert.Single(store.Active("srv"));

        Assert.True(store.Unmute("srv", MuteKind.Song, "t1"));
        Assert.False(store.Unmute("srv", MuteKind.Song, "t1"));
        Assert.False(store.IsMuted("srv", "t1", null));
    }

    [Fact]
    public void Mutes_are_kept_between_runs_and_listed_soonest_ending_first()
    {
        var (store, clock, path) = Make();
        store.Mute("srv", MuteKind.Artist, "a", "Long", null, 30);
        store.Mute("srv", MuteKind.Song, "b", "Short", "X", 7);
        var again = new MuteStore(path, () => clock.Now);
        Assert.Equal(["Short", "Long"], again.Active("srv").Select(e => e.Title));
        Assert.Equal(MuteKind.Song, again.Get("srv", MuteKind.Song, "b")!.Kind);
        Assert.Equal("X", again.Get("srv", MuteKind.Song, "b")!.Detail);
    }

    [Fact]
    public void Changes_are_announced_and_an_unreadable_file_is_just_empty()
    {
        var (store, _, path) = Make();
        var count = 0;
        store.Changed += (_, _) => count++;
        store.Mute("srv", MuteKind.Song, "t1", "Song", null, 3);
        store.Unmute("srv", MuteKind.Song, "t1");
        store.Unmute("srv", MuteKind.Song, "t1");
        Assert.Equal(2, count);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ not json");
        Assert.Empty(new MuteStore(path).Active("srv"));
    }

    [Theory]
    [InlineData(MuteLength.Week, 99, 7)]
    [InlineData(MuteLength.Month, 99, 30)]
    [InlineData(MuteLength.Custom, 12, 12)]
    [InlineData(MuteLength.Custom, 0, 1)]
    [InlineData(MuteLength.Custom, 9999, 365)]
    public void A_setting_stands_for_a_number_of_days(MuteLength length, int custom, int expected) =>
        Assert.Equal(expected, MuteLengths.Days(length, custom));

    [Theory]
    [InlineData(1, "1 day")]
    [InlineData(3, "3 days")]
    [InlineData(7, "1 week")]
    [InlineData(14, "2 weeks")]
    [InlineData(30, "1 month")]
    [InlineData(45, "45 days")]
    [InlineData(60, "60 days")]
    public void A_length_is_put_as_a_person_says_it(int days, string expected) => Assert.Equal(expected, MuteLengths.Describe(days));

    [Fact]
    public void What_is_left_of_a_mute_is_said_in_whole_days()
    {
        var now = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal("6 days left", MuteLengths.Remaining(now.AddDays(5).AddHours(2), now));
        Assert.Equal("Ends today", MuteLengths.Remaining(now.AddHours(6), now));
        Assert.Equal("Ended", MuteLengths.Remaining(now.AddMinutes(-1), now));
    }
}
