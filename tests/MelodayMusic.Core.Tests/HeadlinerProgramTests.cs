using MelodayMusic.Core.Models;
using MelodayMusic.Core.Radio;

namespace MelodayMusic.Core.Tests;

public class HeadlinerProgramTests
{
    private static Track T(string key, string artist, int plays = 0, params string[] genres) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistRatingKey = artist,
        ArtistTitle = "Artist " + artist,
        ViewCount = plays,
        Genres = genres,
    };

    private static List<Track> Played(int artists, int songsEach, string genre = "Pop") =>
        Enumerable.Range(1, artists).SelectMany(a => Enumerable.Range(1, songsEach).Select(s => T($"p{a}s{s}", $"p{a}", plays: s * 4, genre))).ToList();

    [Fact]
    public void A_turn_of_the_programme_mixes_artist_blocks_a_new_run_and_a_genre_block()
    {
        var pools = new HeadlinerPools(
            Played(10, 5),
            Enumerable.Range(1, 8).Select(i => T($"f{i}", $"f{i}", 0, "Pop")).ToList(),
            []);
        var picks = HeadlinerProgram.Build(pools, 2, new Random(1));
        var blocks = picks.Where(p => p.Group > 0).GroupBy(p => p.Group).ToList();
        Assert.Single(blocks);
        Assert.All(blocks, b => { Assert.Equal(3, b.Count()); Assert.Single(b.Select(p => p.Track.ArtistRatingKey).Distinct()); });
        var loose = picks.Where(p => p.Group == 0).ToList();
        Assert.True(loose.Count >= HeadlinerProgram.NewRunLength);
        Assert.Equal(picks.Count, picks.Select(p => p.Track.RatingKey).Distinct().Count());
        Assert.Equal(picks.Select(p => p.Track.ArtistRatingKey).Distinct().Count(), picks.Select(p => p.Track.ArtistRatingKey).Distinct().Count());
    }

    [Fact]
    public void Newer_and_trending_songs_are_given_the_lead_over_what_was_only_played_a_lot()
    {
        var played = Played(10, 5);
        var fresh = new List<Track> { T("n1", "new", 0, "Pop"), T("n2", "new", 0, "Pop"), T("n3", "new", 0, "Pop") };
        var seen = 0;
        for (var seed = 0; seed < 40; seed++)
            if (HeadlinerProgram.Build(new HeadlinerPools(played, fresh, []), 0, new Random(seed)).Any(p => p.Track.ArtistRatingKey == "new")) seen++;
        Assert.True(seen >= 28, $"the new artist was in {seen} of 40 turns");
    }

    [Fact]
    public void Trending_songs_come_into_the_new_runs()
    {
        var trending = Enumerable.Range(1, 6).Select(i => T($"t{i}", $"t{i}", 0, "Pop")).ToList();
        var picks = HeadlinerProgram.Build(new HeadlinerPools(Played(6, 4), [], trending), 1, new Random(3));
        Assert.Contains(picks, p => p.Group == 0 && p.Track.RatingKey.StartsWith('t'));
    }

    [Fact]
    public void A_genre_block_is_of_one_genre_by_different_artists()
    {
        var pool = new List<Track>();
        for (var a = 1; a <= 6; a++)
            for (var s = 1; s <= 3; s++) pool.Add(T($"r{a}s{s}", $"r{a}", s * 3, "Rock"));
        for (var a = 1; a <= 6; a++)
            for (var s = 1; s <= 3; s++) pool.Add(T($"j{a}s{s}", $"j{a}", s * 3, "Jazz"));
        var picks = HeadlinerProgram.Build(new HeadlinerPools(pool, [], []), 2, new Random(7));
        var run = picks.Take(HeadlinerProgram.GenreLength).Select(p => p.Track).ToList();
        Assert.True(run.Count >= 3);
        Assert.Single(run.Select(t => t.Genres[0]).Distinct());
        Assert.Equal(run.Count, run.Select(t => t.ArtistRatingKey).Distinct().Count());
    }

    [Fact]
    public void Rested_artists_are_not_featured()
    {
        var picks = HeadlinerProgram.Build(new HeadlinerPools(Played(5, 4), [], []), 0, new Random(2),
            restedArtists: new HashSet<string> { "p1", "p2", "p3" });
        Assert.DoesNotContain(picks.Where(p => p.Group > 0), p => p.Track.ArtistRatingKey is "p1" or "p2" or "p3");
    }

    [Fact]
    public void Different_turns_and_different_chances_give_different_line_ups()
    {
        var pools = new HeadlinerPools(Played(30, 5), [], []);
        var first = HeadlinerProgram.Build(pools, 0, new Random(1)).Select(p => p.Track.ArtistRatingKey).ToHashSet();
        var others = Enumerable.Range(2, 8).Select(seed => HeadlinerProgram.Build(pools, 0, new Random(seed)).Select(p => p.Track.ArtistRatingKey).ToHashSet()).ToList();
        Assert.Contains(others, o => !o.SetEquals(first));
    }

    [Fact]
    public void Artists_featured_are_remembered_for_a_day_and_a_half_and_then_forgotten()
    {
        var path = Path.Combine(Path.GetTempPath(), "headliners-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var now = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
            var history = new HeadlinerHistory(path);
            history.Record(["a", "b"], now);
            var again = new HeadlinerHistory(path);
            Assert.Equal(["a", "b"], again.Rested(now.AddHours(10)).OrderBy(x => x));
            Assert.Empty(again.Rested(now.AddHours(40)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_library_with_nothing_to_programme_gives_nothing()
    {
        Assert.Empty(HeadlinerProgram.Build(new HeadlinerPools([], [], []), 0, new Random(1)));
    }
}
