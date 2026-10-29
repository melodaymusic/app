using MelodayMusic.Core.Home;

namespace MelodayMusic.Core.Tests;

public class ListeningClockTests
{
    [Fact]
    public void Time_adds_up_and_is_described_in_hours_and_minutes()
    {
        var now = new DateTime(2026, 10, 4, 14, 0, 0);
        var clock = new ListeningClock(clock: () => now);
        Assert.Equal("0m", ListeningClock.Describe(clock.Today));
        for (var i = 0; i < 540; i++) clock.Add(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromMinutes(135), clock.Today);
        Assert.Equal("2h 15m", ListeningClock.Describe(clock.Today));
        Assert.Equal("42m", ListeningClock.Describe(TimeSpan.FromMinutes(42.9)));
        Assert.Equal("1h 0m", ListeningClock.Describe(TimeSpan.FromMinutes(60)));
    }

    [Fact]
    public void A_new_day_starts_from_nothing_and_the_minute_shown_is_announced_as_it_changes()
    {
        var now = new DateTime(2026, 10, 4, 23, 59, 0);
        var clock = new ListeningClock(clock: () => now);
        var announced = 0;
        clock.Changed += (_, _) => announced++;
        clock.Add(TimeSpan.FromSeconds(30));
        clock.Add(TimeSpan.FromSeconds(10));
        Assert.Equal(1, announced);
        clock.Add(TimeSpan.FromSeconds(30));
        Assert.Equal(2, announced);
        now = now.AddMinutes(2);
        Assert.Equal(TimeSpan.Zero, clock.Today);
        clock.Add(TimeSpan.FromSeconds(20));
        Assert.Equal(TimeSpan.FromSeconds(20), clock.Today);
    }

    [Fact]
    public void The_day_survives_a_restart_but_not_a_new_day()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-listening-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var now = new DateTime(2026, 10, 4, 9, 0, 0);
            var first = new ListeningClock(path, () => now);
            first.Add(TimeSpan.FromMinutes(75));
            first.Flush();
            Assert.Equal(TimeSpan.FromMinutes(75), new ListeningClock(path, () => now.AddHours(5)).Today);
            Assert.Equal(TimeSpan.Zero, new ListeningClock(path, () => now.AddDays(1)).Today);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
