using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class PulseLineupTests
{
    // A Friday.
    private static readonly DateTimeOffset Friday = new(2026, 11, 20, 0, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    public void The_default_lineup_matches_the_built_in_schedule_hour_by_hour()
    {
        string Expected(DateTimeOffset t) => t.Hour switch
        {
            >= 6 and < 9 => "Morning Pulse",
            >= 9 and < 12 => "New Music",
            >= 12 and < 14 => "Chart Pulse",
            >= 14 and < 16 => "Then & Now",
            >= 16 and < 18 => "Pop Focus",
            >= 18 and < 20 => t.DayOfWeek == DayOfWeek.Friday ? t.Hour == 19 ? "New Release Countdown" : "Weekly Picks" : "Rap & R&B",
            >= 20 and < 22 => "Alternative Focus",
            _ => "Continuous Pulse",
        };
        for (var hour = 0; hour < 24 * 7; hour++)
        {
            var time = Friday.AddHours(hour).AddMinutes(30);
            Assert.Equal(Expected(time), PulseLineup.At(PulseLineup.Defaults, time).Title);
        }
    }

    [Fact]
    public void Making_a_program_longer_shortens_the_ones_it_runs_into()
    {
        var lineup = PulseLineup.Defaults;
        var chart = lineup.Single(s => s.Id == "default-chart") with { End = new TimeOnly(15, 0) };
        var placed = PulseLineup.Place(lineup, chart);
        Assert.Equal("Chart Pulse", PulseLineup.At(placed, Friday.AddHours(14.5)).Title);
        var thenNow = placed.Single(s => s.Id == "default-then-now");
        Assert.Equal(new TimeOnly(15, 0), thenNow.Start);
        Assert.Equal(new TimeOnly(16, 0), thenNow.End);
    }

    [Fact]
    public void A_program_on_some_days_splits_an_everyday_program_only_on_those_days()
    {
        var weekend = new PulseSlot { Start = new TimeOnly(10, 0), End = new TimeOnly(11, 0), Format = PulseFormat.Chart, Days = [DayOfWeek.Saturday, DayOfWeek.Sunday] };
        var placed = PulseLineup.Place(PulseLineup.Defaults, weekend);
        var saturday = Friday.AddDays(1);
        Assert.Equal("Chart Pulse", PulseLineup.At(placed, saturday.AddHours(10.5)).Title);
        Assert.Equal("New Music", PulseLineup.At(placed, saturday.AddHours(9.5)).Title);
        Assert.Equal("New Music", PulseLineup.At(placed, saturday.AddHours(11.5)).Title);
        Assert.Equal("New Music", PulseLineup.At(placed, Friday.AddHours(10.5)).Title);
        Assert.All(placed, s => Assert.True(s.IsValid));
    }

    [Fact]
    public void A_program_covering_another_entirely_replaces_it()
    {
        var long_ = new PulseSlot { Start = new TimeOnly(8, 0), End = new TimeOnly(13, 0), Format = PulseFormat.Throwbacks };
        var placed = PulseLineup.Place(PulseLineup.Defaults, long_);
        Assert.DoesNotContain(placed, s => s.Id == "default-new-music");
        Assert.Equal(new TimeOnly(8, 0), placed.Single(s => s.Id == "default-morning").End);
        Assert.Equal(new TimeOnly(13, 0), placed.Single(s => s.Id == "default-chart").Start);
    }

    [Fact]
    public void An_empty_lineup_is_continuous_pulse_all_day()
    {
        Assert.Equal("Continuous Pulse", PulseLineup.At([], Friday.AddHours(10)).Title);
        Assert.Equal(Friday.AddDays(8), PulseLineup.NextBoundary([], Friday.AddHours(10)));
    }

    [Fact]
    public void Boundaries_follow_an_edited_lineup_and_run_until_midnight()
    {
        var late = new PulseSlot { Start = new TimeOnly(22, 0), End = TimeOnly.MinValue, Format = PulseFormat.Genre, Genre = "jazz" };
        Assert.True(late.IsValid);
        List<PulseSlot> lineup = [late];
        Assert.Equal("Jazz Focus", PulseLineup.At(lineup, Friday.AddHours(23.5)).Title);
        Assert.Equal(Friday.AddHours(22), PulseLineup.NextBoundary(lineup, Friday.AddHours(10)));
        Assert.Equal(Friday.AddDays(1), PulseLineup.NextBoundary(lineup, Friday.AddHours(23)));
    }

    [Fact]
    public void A_program_that_ends_before_it_starts_or_a_genre_without_a_name_is_invalid()
    {
        Assert.False(new PulseSlot { Start = new TimeOnly(14, 0), End = new TimeOnly(13, 0), Format = PulseFormat.Chart }.IsValid);
        Assert.False(new PulseSlot { Start = new TimeOnly(14, 0), End = new TimeOnly(15, 0), Format = PulseFormat.Genre }.IsValid);
    }

    [Fact]
    public void The_store_keeps_an_edited_lineup_per_library_and_resets_to_the_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-lineup-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new PulseLineupStore(path);
            Assert.False(store.IsEdited("a"));
            Assert.Equal("DEFAULT", store.Version("a"));
            store.Set("a", PulseLineup.Defaults.Where(s => s.Id != "default-pop"));

            var reloaded = new PulseLineupStore(path);
            Assert.True(reloaded.IsEdited("a"));
            Assert.DoesNotContain(reloaded.Get("a"), s => s.Id == "default-pop");
            Assert.Contains(reloaded.Get("b"), s => s.Id == "default-pop");
            Assert.NotEqual("DEFAULT", reloaded.Version("a"));

            reloaded.Reset("a");
            Assert.False(reloaded.IsEdited("a"));
            Assert.Equal(PulseLineup.Defaults.Count, reloaded.Get("a").Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
