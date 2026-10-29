using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class PulseScheduleTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "Pulse test time",
        TimeSpan.FromHours(-5),
        "Pulse test time",
        "Pulse test standard",
        "Pulse test daylight",
        [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2020, 1, 1),
            new DateTime(2030, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
            TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday))]);

    private static PulseScheduleRule Rule(
        PulseScheduleRepeat repeat = PulseScheduleRepeat.Once,
        DateOnly? day = null,
        TimeOnly? time = null,
        int minutes = 30,
        params DayOfWeek[] days) => new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Source = new(PulseScheduleSourceKind.Artist, "artist:1", "Radiohead"),
            StartsOn = day ?? new DateOnly(2026, 10, 21),
            StartsAt = time ?? new TimeOnly(20, 0),
            Repeat = repeat,
            Days = [.. days],
            Length = PulseScheduleLength.Minutes(minutes),
        };

    [Fact]
    public void Daily_weekday_weekend_and_selected_day_rules_follow_the_local_calendar()
    {
        var from = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(-4));
        var to = from.AddDays(7);

        Assert.Equal(7, PulseSchedule.Occurrences(Rule(PulseScheduleRepeat.Daily, new(2026, 10, 19)), from, to, Zone).Count);
        Assert.Equal(5, PulseSchedule.Occurrences(Rule(PulseScheduleRepeat.Weekdays, new(2026, 10, 19)), from, to, Zone).Count);
        Assert.Equal(2, PulseSchedule.Occurrences(Rule(PulseScheduleRepeat.Weekends, new(2026, 10, 19)), from, to, Zone).Count);
        var selected = PulseSchedule.Occurrences(Rule(PulseScheduleRepeat.SelectedDays, new(2026, 10, 19), days: [DayOfWeek.Monday, DayOfWeek.Friday]), from, to, Zone);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday], selected.Select(o => o.StartsAt.DayOfWeek));
    }

    [Fact]
    public void A_one_time_rule_occurs_only_on_its_date_and_an_end_date_is_inclusive()
    {
        var from = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(-4));
        var once = Rule(day: new(2026, 10, 21));
        Assert.Single(PulseSchedule.Occurrences(once, from, from.AddDays(14), Zone));

        var daily = Rule(PulseScheduleRepeat.Daily, new(2026, 10, 19)) with { EndsOn = new DateOnly(2026, 10, 21) };
        Assert.Equal(3, PulseSchedule.Occurrences(daily, from, from.AddDays(14), Zone).Count);
    }

    [Fact]
    public void A_skipped_daylight_time_moves_to_the_first_valid_minute()
    {
        var shifted = PulseSchedule.AtLocal(new DateOnly(2026, 3, 8), new TimeOnly(2, 30), Zone);
        Assert.Equal(new TimeOnly(3, 0), TimeOnly.FromDateTime(shifted.DateTime));
        Assert.Equal(TimeSpan.FromHours(-4), shifted.Offset);
    }

    [Fact]
    public void Overlaps_conflict_but_touching_programmes_do_not()
    {
        var from = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(-4));
        var friday = Rule(PulseScheduleRepeat.SelectedDays, new(2026, 10, 19), new(20, 0), 30, DayOfWeek.Friday);
        var overlap = Rule(PulseScheduleRepeat.SelectedDays, new(2026, 10, 19), new(20, 15), 30, DayOfWeek.Friday);
        var touches = Rule(PulseScheduleRepeat.SelectedDays, new(2026, 10, 19), new(20, 30), 30, DayOfWeek.Friday);

        Assert.Equal([friday.Id], PulseSchedule.Conflicts(overlap, [friday], from, Zone).Select(r => r.Id));
        Assert.Empty(PulseSchedule.Conflicts(touches, [friday], from, Zone));
    }

    [Fact]
    public void Disabled_rules_do_not_air_or_conflict()
    {
        var at = new DateTimeOffset(2026, 10, 21, 20, 10, 0, TimeSpan.FromHours(-4));
        var disabled = Rule() with { IsEnabled = false };
        Assert.Null(PulseSchedule.At([disabled], at, Zone));
        Assert.Empty(PulseSchedule.Conflicts(Rule(), [disabled], at.AddDays(-1), Zone));
    }

    [Fact]
    public void The_next_boundary_can_be_a_programme_start_or_end()
    {
        var rule = Rule(day: new(2026, 10, 21), time: new(20, 0), minutes: 30);
        var before = new DateTimeOffset(2026, 10, 21, 19, 50, 0, TimeSpan.FromHours(-4));
        Assert.Equal(before.AddMinutes(10), PulseSchedule.NextBoundary([rule], before, zone: Zone));
        Assert.Equal(before.AddMinutes(40), PulseSchedule.NextBoundary([rule], before.AddMinutes(15), zone: Zone));
    }

    [Fact]
    public void The_store_updates_removes_and_keeps_library_scopes_apart()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-pulse-guide-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var original = Rule();
            var store = new PulseScheduleStore(path);
            store.Upsert("account|server|music", original);
            store.Upsert("account|server|other", Rule(day: new(2026, 10, 22)));
            var version = store.Version("account|server|music");

            var reloaded = new PulseScheduleStore(path);
            Assert.Single(reloaded.Get("account|server|music"));
            Assert.Single(reloaded.Get("account|server|other"));
            reloaded.Upsert("account|server|music", original with { Title = "Friday Night" });
            Assert.Equal("Friday Night", new PulseScheduleStore(path).Get("account|server|music").Single().Title);
            Assert.NotEqual(version, reloaded.Version("account|server|music"));
            Assert.True(reloaded.Remove("account|server|music", original.Id));
            Assert.Empty(new PulseScheduleStore(path).Get("account|server|music"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
