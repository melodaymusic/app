using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class LiveRadioTests
{
    private static readonly DateTimeOffset Now = new(new DateTime(2026, 10, 5, 15, 0, 0, DateTimeKind.Local));

    private static LiveEntry E(string key, int seconds = 180) => new(key, seconds * 1000);

    [Fact]
    public void Tuning_in_lands_in_the_middle_of_the_song_that_is_on()
    {
        var schedule = LiveSchedule.Begin([E("a"), E("b"), E("c")], Now, TimeSpan.FromSeconds(200));
        Assert.Equal((1, 20_000), schedule.Locate(Now));
    }

    [Fact]
    public void The_station_moves_on_while_nobody_is_listening()
    {
        var schedule = LiveSchedule.Begin([E("a"), E("b"), E("c")], Now, TimeSpan.FromSeconds(10));
        Assert.Equal(0, schedule.Locate(Now)!.Value.Index);
        Assert.Equal(2, schedule.Locate(Now.AddMinutes(6))!.Value.Index);
    }

    [Fact]
    public void Past_the_last_song_there_is_nothing_on()
    {
        var schedule = LiveSchedule.Begin([E("a"), E("b")], Now, TimeSpan.Zero);
        Assert.Null(schedule.Locate(Now.AddMinutes(7)));
        Assert.Equal(TimeSpan.Zero, schedule.Remaining(Now.AddMinutes(7)));
        Assert.Equal(TimeSpan.FromMinutes(6), schedule.Remaining(Now));
    }

    [Fact]
    public void Trimming_forgets_old_songs_without_changing_what_is_on()
    {
        var schedule = LiveSchedule.Begin([E("a"), E("b"), E("c"), E("d")], Now, TimeSpan.FromSeconds(500));
        var (index, offset) = schedule.Locate(Now)!.Value;
        var on = schedule.Entries[index].Key;
        schedule.Trim(Now, TimeSpan.FromSeconds(60));
        var (after, offsetAfter) = schedule.Locate(Now)!.Value;
        Assert.Equal(on, schedule.Entries[after].Key);
        Assert.Equal(offset, offsetAfter);
        Assert.True(schedule.Entries.Count < 4);
    }

    [Fact]
    public void A_song_already_in_the_running_order_is_not_added_again()
    {
        var schedule = LiveSchedule.Begin([E("a"), E("b")], Now, TimeSpan.Zero);
        Assert.Equal(1, schedule.Append([E("b"), E("c")]));
        Assert.Equal(["a", "b", "c"], schedule.Entries.Select(e => e.Key));
    }

    [Fact]
    public void A_song_of_unknown_length_gets_a_stand_in()
    {
        var schedule = LiveSchedule.Begin([new LiveEntry("a", 0)], Now, TimeSpan.Zero);
        Assert.Equal(LiveSchedule.FallbackDurationMs, schedule.Entries[0].DurationMs);
    }

    [Fact]
    public void The_store_remembers_a_running_order_between_visits()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-live-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var schedule = LiveSchedule.Begin([E("a"), E("b")], Now, TimeSpan.FromSeconds(30));
            schedule.ProgrammeRound = 2;
            new LiveScheduleStore(path).Set("srv|library", schedule);
            var kept = new LiveScheduleStore(path).Get("srv|library");
            Assert.NotNull(kept);
            Assert.Equal(["a", "b"], kept!.Entries.Select(e => e.Key));
            Assert.Equal(Now.AddSeconds(-30), kept.StartedAt);
            Assert.Equal(2, kept.ProgrammeRound);
            Assert.Null(new LiveScheduleStore(path).Get("srv|other"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
