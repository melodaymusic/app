using MelodayMusic.Core.Meloday;

namespace MelodayMusic.Core.Home;

public static class HomeRefresh
{
    public static bool ContextChanged(DateTimeOffset previous, DateTimeOffset now) =>
        previous.Date != now.Date || MelodayPeriods.At(previous.DateTime).Name != MelodayPeriods.At(now.DateTime).Name;

    public static bool IsDue(DateTimeOffset previous, DateTimeOffset now) =>
        ContextChanged(previous, now) || now < previous || now - previous >= TimeSpan.FromMinutes(10);
}
