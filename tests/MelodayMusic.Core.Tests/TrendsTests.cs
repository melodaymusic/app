using MelodayMusic.Core.Trends;

namespace MelodayMusic.Core.Tests;

public class TrendsTests
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(1);
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 18, 0, 0, Offset);

    private static LoggedPlay Play(DateTimeOffset at, string track, string artist = "Artist", string? album = "album", int seconds = 200,
        ListenSource source = ListenSource.Library) => new(at, track, album, artist, seconds) { Source = source };

    [Fact]
    public void Periods_run_to_now_and_compare_with_the_same_span_before()
    {
        var (from, to) = ListeningTrends.Range(TrendPeriod.Week, Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, Offset), from);
        Assert.Equal(Now, to);
        Assert.Equal((from.AddDays(-7), Now.AddDays(-7)), ListeningTrends.Previous(TrendPeriod.Week, from, to));

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, Offset), ListeningTrends.Range(TrendPeriod.Month, Now).From);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, Offset), ListeningTrends.Range(TrendPeriod.Year, Now).From);
        var first = new DateTimeOffset(2019, 5, 3, 21, 0, 0, Offset);
        Assert.Equal(new DateTimeOffset(2019, 5, 3, 0, 0, 0, Offset), ListeningTrends.Range(TrendPeriod.AllTime, Now, first).From);
        Assert.Null(ListeningTrends.Previous(TrendPeriod.AllTime, first, Now));
    }

    [Fact]
    public void A_week_has_its_totals_tops_and_rhythm()
    {
        var log = new List<LoggedPlay>
        {
            Play(Now.AddDays(-8), "old", "Known"),
            Play(Now.AddDays(-6), "late", "Known"),
            Play(new DateTimeOffset(2026, 10, 12, 23, 0, 0, Offset), "a1", "Known", "lp1"),
            Play(new DateTimeOffset(2026, 10, 12, 23, 4, 0, Offset), "a1", "Known", "lp1"),
            Play(new DateTimeOffset(2026, 10, 12, 23, 8, 0, Offset), "a2", "Known", "lp1"),
            Play(new DateTimeOffset(2026, 10, 13, 8, 0, 0, Offset), "b1", "Fresh", "lp2", source: ListenSource.Radio),
            Play(new DateTimeOffset(2026, 10, 14, 17, 0, 0, Offset), "c1", "Various Artists", "comp"),
        };
        var r = ListeningTrends.Build(log, TrendPeriod.Week, Now);

        Assert.Equal(5, r.Plays);
        Assert.Equal(1000, r.Seconds);
        Assert.Equal(2, r.Artists);
        Assert.Equal(3, r.Albums);
        Assert.Equal(4, r.Songs);
        Assert.Equal(3, r.ActiveDays);
        Assert.Equal(1, r.NewArtists);
        Assert.Equal(new[] { "Fresh" }, r.Discovered);
        Assert.Equal(1, r.PreviousPlays);
        Assert.Equal(4.0, r.Change);

        Assert.Equal(new[] { "Known", "Fresh" }, r.TopArtists.Select(a => a.Name));
        Assert.Equal(3, r.TopArtists[0].Plays);
        Assert.Equal("a1", r.TopArtists[0].Example);
        Assert.Equal("lp1", r.TopAlbums[0].Key);
        Assert.Equal("a1", r.TopSongs[0].Key);
        Assert.Equal(2, r.TopSongs[0].Plays);

        Assert.Equal(23, r.PeakHour);
        Assert.Equal(DayOfWeek.Monday, r.PeakDay);
        Assert.Equal(600, r.Weekdays[0]);
        Assert.Equal(7, r.Timeline.Count);
        Assert.Equal(600, r.Timeline[0].Seconds);
        Assert.Equal(0, r.Timeline[6].Seconds);
        Assert.Equal(1, r.Sources[ListenSource.Radio]);
        Assert.Equal(new DateOnly(2026, 10, 12), r.BiggestDay!.Value.Day);
        Assert.Equal(TimeSpan.FromSeconds(680), r.LongestSession!.Value.Length);
        Assert.Empty(r.Traits);
        Assert.False(r.HasEstimates);
    }

    [Fact]
    public void Timelines_fit_the_period()
    {
        var log = new List<LoggedPlay>
        {
            Play(new DateTimeOffset(2024, 3, 1, 12, 0, 0, Offset), "x") with { Estimated = true },
            Play(new DateTimeOffset(2026, 2, 1, 12, 0, 0, Offset), "y"),
            Play(new DateTimeOffset(2026, 10, 2, 12, 0, 0, Offset), "z"),
        };
        Assert.Equal(31, ListeningTrends.Build(log, TrendPeriod.Month, Now).Timeline.Count);
        var year = ListeningTrends.Build(log, TrendPeriod.Year, Now);
        Assert.Equal(12, year.Timeline.Count);
        Assert.Equal(200, year.Timeline[1].Seconds);
        Assert.Equal(200, year.Timeline[9].Seconds);
        var all = ListeningTrends.Build(log, TrendPeriod.AllTime, Now);
        Assert.Equal(new[] { "2024", "2025", "2026" }, all.Timeline.Select(b => b.Label));
        Assert.Equal(3, all.Plays);
        Assert.True(all.HasEstimates);
        Assert.Null(all.Change);
    }

    [Fact]
    public void Traits_describe_what_stands_out()
    {
        var log = new List<LoggedPlay>();
        var night = new DateTimeOffset(2026, 10, 12, 23, 0, 0, Offset);
        for (var i = 0; i < 30; i++) log.Add(Play(night.AddMinutes(i * 4), $"s{i}", "Night Artist", "late-lp", 240, ListenSource.Radio));
        var r = ListeningTrends.Build(log, TrendPeriod.Week, Now);

        Assert.Equal(3, r.Traits.Count);
        Assert.Contains(r.Traits, t => t.Id == "night-owl");
        Assert.True(r.Traits.Zip(r.Traits.Skip(1)).All(p => p.First.Strength >= p.Second.Strength));
        Assert.All(r.Traits, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));

        Assert.Empty(ListeningTrends.Build(log.Take(ListeningTrends.TraitMinimum - 1).ToList(), TrendPeriod.Week, Now).Traits);
    }

    [Fact]
    public void Daily_totals_cover_the_calendar_days_asked_for()
    {
        var log = new List<LoggedPlay>
        {
            Play(new DateTimeOffset(2026, 10, 1, 9, 0, 0, Offset), "a"),
            Play(new DateTimeOffset(2026, 10, 1, 22, 0, 0, Offset), "b"),
            Play(new DateTimeOffset(2026, 10, 3, 9, 0, 0, Offset), "c"),
            Play(new DateTimeOffset(2026, 9, 1, 9, 0, 0, Offset), "d"),
        };
        var days = ListeningTrends.Daily(log, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31));
        Assert.Equal(2, days.Count);
        Assert.Equal(400, days[new DateOnly(2026, 10, 1)]);
        Assert.Equal("3h 20m", ListeningTrends.Describe(TimeSpan.FromMinutes(200)));
        Assert.Equal("1,204h", ListeningTrends.Describe(TimeSpan.FromHours(1204.5)));
    }

    [Fact]
    public void The_log_keeps_plays_in_order_across_restarts_and_merges_history_once()
    {
        var path = Path.Combine(Path.GetTempPath(), $"log-{Guid.NewGuid():N}.jsonl");
        try
        {
            var log = new ListeningLog(path, () => Now);
            log.Add(Play(Now.AddMinutes(10), "b"));
            log.Add(Play(Now.AddMinutes(5), "a", source: ListenSource.Meloday));
            log.Add(Play(Now.AddMinutes(20), "c"));
            Assert.Equal(new[] { "a", "b", "c" }, log.Plays().Select(p => p.Track));

            log.MergeHistory([Play(Now.AddDays(-2), "h1") with { Estimated = true }, Play(Now.AddMinutes(5), "a")]);
            log.MergeHistory([Play(Now.AddDays(-3), "h0")]);
            Assert.True(log.HistoryCounted);
            Assert.Equal(new[] { "h1", "a", "b", "c" }, log.Plays().Select(p => p.Track));

            log.Add(Play(Now.AddMinutes(30), "d"));
            File.AppendAllText(path, "{\"a\":\"2026-10-14T19:00:00+01:0");

            var again = new ListeningLog(path, () => Now.AddDays(1));
            var plays = again.Plays();
            Assert.Equal(new[] { "h1", "a", "b", "c", "d" }, plays.Select(p => p.Track));
            Assert.True(again.HistoryCounted);
            Assert.Equal(Now, again.Since);
            Assert.Equal(ListenSource.Meloday, plays[1].Source);
            Assert.True(plays[0].Estimated);
            Assert.Equal(200, plays[4].Seconds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Fresh_finds_are_artists_never_played_before_the_period_and_all_time_has_none()
    {
        var now = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
        var log = new List<LoggedPlay>
        {
            Play(now.AddYears(-3), "old", "Lana Del Rey"),
            Play(now.AddDays(-2), "a", "Lana Del Rey"),
            Play(now.AddDays(-2).AddMinutes(5), "b", "Newcomer"),
            Play(now.AddDays(-1), "c", "Newcomer"),
        };
        var year = ListeningTrends.Build(log, TrendPeriod.Year, now);
        Assert.Equal(["Newcomer"], year.FreshFinds.Select(f => f.Name));
        Assert.DoesNotContain("Lana Del Rey", year.Discovered);
        var all = ListeningTrends.Build(log, TrendPeriod.AllTime, now);
        Assert.Empty(all.FreshFinds);
        Assert.DoesNotContain(all.Traits, t => t.Id == "explorer");
    }
}
