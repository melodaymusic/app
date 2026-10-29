using MelodayMusic.Core.Mixes;

namespace MelodayMusic.Core.Tests;

public class FreshnessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_song_just_played_counts_most_against_and_a_month_clears_it()
    {
        Assert.Equal(0, Freshness.Penalty(null, Now));
        Assert.Equal(1.2, Freshness.Penalty(Now, Now), 6);
        var yesterday = Freshness.Penalty(Now.AddDays(-1), Now);
        var week = Freshness.Penalty(Now.AddDays(-7), Now);
        var twoWeeks = Freshness.Penalty(Now.AddDays(-14), Now);
        Assert.True(yesterday > week && week > twoWeeks && twoWeeks > 0);
        Assert.Equal(0, Freshness.Penalty(Now.AddDays(-30), Now));
        Assert.Equal(0, Freshness.Penalty(Now.AddDays(-400), Now));
        Assert.Equal(1.2, Freshness.Penalty(Now.AddHours(2), Now), 6);
    }

    [Fact]
    public void The_weight_runs_from_one_to_a_little_above_nothing()
    {
        Assert.Equal(1, Freshness.Weight(null, Now));
        Assert.Equal(1, Freshness.Weight(Now.AddDays(-60), Now));
        Assert.Equal(0.06, Freshness.Weight(Now, Now), 6);
        Assert.InRange(Freshness.Weight(Now.AddDays(-7), Now), 0.06, 1);
    }

    private sealed record Song(string Name, DateTimeOffset? Played);

    [Fact]
    public void Songs_played_lately_are_left_out_where_there_are_enough_others()
    {
        var songs = new[] { new Song("a", Now.AddHours(-5)), new Song("b", null), new Song("c", Now.AddDays(-10)), new Song("d", Now.AddDays(-1)), new Song("e", Now.AddDays(-20)) };
        var picked = Freshness.Prefer(songs, s => s.Played, Now, needed: 3);
        Assert.Equal(["b", "c", "e"], picked.Take(3).Select(s => s.Name));
    }

    [Fact]
    public void A_small_library_still_gets_songs_those_not_played_for_longest_first()
    {
        var songs = new[] { new Song("a", Now.AddHours(-5)), new Song("b", null), new Song("d", Now.AddDays(-2)) };
        var picked = Freshness.Prefer(songs, s => s.Played, Now, needed: 3);
        Assert.Equal(["b", "d", "a"], picked.Select(s => s.Name));
    }

    [Fact]
    public void A_weighted_order_puts_songs_not_played_lately_first_far_more_often()
    {
        var random = new Random(7);
        var songs = new[] { new Song("fresh", Now.AddDays(-40)), new Song("stale", Now.AddHours(-3)) };
        var freshFirst = Enumerable.Range(0, 2000).Count(_ => Freshness.WeightedOrder(songs, s => s.Played, Now, random).First().Name == "fresh");
        Assert.InRange(freshFirst, 1750, 1990);
    }
}
