using MelodayMusic.Core.Home;

namespace MelodayMusic.Core.Tests;

public class HomeRefreshTests
{
    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Evening_boundary_refreshes_before_regular_interval()
    {
        Assert.True(HomeRefresh.IsDue(At(8, 16, 59), At(8, 17, 0)));
        Assert.True(HomeRefresh.ContextChanged(At(8, 16, 59), At(8, 17, 0)));
    }

    [Fact]
    public void Midnight_changes_day_rails_within_the_same_late_night_period() =>
        Assert.True(HomeRefresh.ContextChanged(At(8, 23, 59), At(9, 0, 0)));

    [Fact]
    public void Routine_refreshes_are_throttled_within_a_period()
    {
        Assert.False(HomeRefresh.IsDue(At(8, 17, 0), At(8, 17, 9)));
        Assert.True(HomeRefresh.IsDue(At(8, 17, 0), At(8, 17, 10)));
        Assert.False(HomeRefresh.ContextChanged(At(8, 17, 0), At(8, 17, 10)));
    }

    [Fact]
    public void Returning_after_sleep_rechecks_the_current_period() =>
        Assert.True(HomeRefresh.IsDue(At(8, 13, 0), At(8, 20, 0)));

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(13)]
    [InlineData(17)]
    [InlineData(20)]
    [InlineData(23)]
    public void Every_day_period_boundary_invalidates_the_context(int hour) =>
        Assert.True(HomeRefresh.IsDue(At(8, hour - 1, 59), At(8, hour, 0)));
}
