using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;
using MelodayMusic.Core.Taste;

namespace MelodayMusic.Core.Tests;

public class RadioEngineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static Track T(string key, string artist, int plays = 0, double? rating = null, int daysAgo = 90, string[]? genres = null, string[]? moods = null, int skips = 0) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistRatingKey = artist,
        ViewCount = plays,
        UserRating = rating,
        LastViewedAt = plays == 0 ? null : Now.AddDays(-daysAgo),
        Genres = genres ?? [],
        Moods = moods ?? [],
        SkipCount = skips,
    };

    private static RadioContext Ctx(TasteEngine? taste = null, string[]? tags = null, string[]? seed = null, Dictionary<string, double>? near = null) => new()
    {
        Now = Now,
        Tags = (tags ?? []).ToHashSet(),
        SeedArtists = (seed ?? []).ToHashSet(),
        Affinity = near ?? [],
        Taste = taste ?? TasteEngine.PlexOnly,
    };

    private static TasteEngine Skipping(string key)
    {
        var memory = new TasteMemory(clock: () => Now);
        for (var i = 0; i < 4; i++) memory.Record(TasteSignal.Skip, key, null);
        return new TasteEngine(memory: memory);
    }

    [Fact]
    public void Well_rated_and_often_played_songs_outrank_plain_ones()
    {
        var loved = RadioEngine.Score(T("a", "x", plays: 30, rating: 10), Ctx());
        var plain = RadioEngine.Score(T("b", "x", plays: 1), Ctx());
        Assert.True(loved > plain);
    }

    [Fact]
    public void Songs_played_lately_wait_and_skipped_songs_lose()
    {
        var old = RadioEngine.Score(T("a", "x", plays: 5, daysAgo: 60), Ctx());
        var yesterday = RadioEngine.Score(T("a", "x", plays: 5, daysAgo: 1), Ctx());
        Assert.True(old > yesterday);
        var skipped = RadioEngine.Score(T("a", "x", plays: 5, daysAgo: 60), Ctx(Skipping("a")));
        Assert.True(old > skipped);
        var plexSkips = RadioEngine.Score(T("a", "x", plays: 1, daysAgo: 60, skips: 6), Ctx());
        Assert.True(RadioEngine.Score(T("a", "x", plays: 1, daysAgo: 60), Ctx()) > plexSkips);
    }

    [Fact]
    public void The_seed_its_neighbours_and_its_tags_pull_songs_up()
    {
        var ctx = Ctx(tags: ["dance pop", "upbeat"], seed: ["seed"], near: new() { ["near"] = 0.9 });
        var seed = RadioEngine.Score(T("a", "seed"), ctx);
        var near = RadioEngine.Score(T("b", "near"), ctx);
        var tagged = RadioEngine.Score(T("c", "far", genres: ["Dance Pop"], moods: ["Upbeat"]), ctx);
        var stranger = RadioEngine.Score(T("d", "far", genres: ["Metal"]), ctx);
        Assert.True(seed > stranger && near > stranger && tagged > stranger);
    }

    [Fact]
    public void Artists_are_spread_out_and_capped()
    {
        var pool = new List<Track>();
        for (var i = 0; i < 8; i++) pool.Add(T("a" + i, "A", plays: 50, rating: 10));
        for (var i = 0; i < 8; i++) pool.Add(T("b" + i, "B", plays: 40, rating: 10));
        for (var i = 0; i < 8; i++) pool.Add(T("c" + i, "C", plays: 30, rating: 10));
        var picked = RadioEngine.Pick(pool, Ctx(), 6, new Random(1), artistGap: 2, maxPerArtist: 2);
        Assert.Equal(6, picked.Count);
        Assert.All(picked.GroupBy(t => t.ArtistRatingKey), g => Assert.True(g.Count() <= 2));
        for (var i = 1; i < picked.Count; i++) Assert.NotEqual(picked[i - 1].ArtistRatingKey, picked[i].ArtistRatingKey);
    }

    [Fact]
    public void A_narrow_pool_still_fills_the_batch_and_excluded_songs_stay_out()
    {
        var pool = new List<Track> { T("a1", "A", plays: 9), T("a2", "A", plays: 8), T("a3", "A", plays: 7), T("b1", "B", plays: 1) };
        var ctx = new RadioContext { Now = Now, Exclude = new HashSet<string> { "a1" } };
        var picked = RadioEngine.Pick(pool, ctx, 4, new Random(2), artistGap: 4, maxPerArtist: 3);
        Assert.Equal(3, picked.Count);
        Assert.DoesNotContain(picked, t => t.RatingKey == "a1");
    }
}
