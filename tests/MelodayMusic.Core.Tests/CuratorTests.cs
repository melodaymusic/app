using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;

namespace MelodayMusic.Core.Tests;

public class CuratorTests
{
    private static Track T(string key, string artist, int? year = null, string[]? genres = null, string[]? moods = null) => new()
    {
        RatingKey = key,
        Title = key,
        ArtistRatingKey = "artist:" + artist,
        ArtistTitle = artist,
        Year = year,
        Genres = genres ?? [],
        Moods = moods ?? [],
    };

    private static string[] Keys(IEnumerable<Track> tracks) => tracks.Select(t => t.RatingKey).ToArray();

    [Theory]
    [InlineData(Curator.DoubleUp)]
    [InlineData(Curator.Twin)]
    [InlineData(Curator.Drift)]
    [InlineData(Curator.TimeMachine)]
    [InlineData(Curator.Spread)]
    [InlineData(Curator.Smooth)]
    [InlineData(Curator.None)]
    public void Every_song_comes_back_exactly_once(Curator curator)
    {
        var pool = Enumerable.Range(0, 25).Select(i => T("t" + i, "artist" + i % 6, 1980 + i % 40, [i % 3 == 0 ? "Pop" : "Rock"])).ToList();
        var ordered = Curators.Order(curator, pool[0], pool);
        Assert.Equal(pool.Count, ordered.Count);
        Assert.Equal(Keys(pool).OrderBy(k => k), Keys(ordered).OrderBy(k => k));
    }

    [Fact]
    public void Double_Up_plays_two_from_each_artist_at_a_time()
    {
        var pool = new[] { T("a1", "A"), T("a2", "A"), T("a3", "A"), T("b1", "B"), T("b2", "B"), T("c1", "C") };
        Assert.Equal(["a1", "a2", "b1", "b2", "c1", "a3"], Keys(Curators.Order(Curator.DoubleUp, null, pool)));
    }

    [Fact]
    public void Time_Machine_goes_oldest_to_newest_with_the_undated_last()
    {
        var pool = new[] { T("new", "A", 2020), T("none", "B"), T("old", "C", 1971), T("mid", "D", 1999), T("none2", "E") };
        Assert.Equal(["old", "mid", "new", "none", "none2"], Keys(Curators.Order(Curator.TimeMachine, null, pool)));
    }

    [Fact]
    public void Spread_never_repeats_an_artist_while_others_remain()
    {
        var pool = new[] { T("a1", "A"), T("a2", "A"), T("a3", "A"), T("b1", "B"), T("c1", "C"), T("b2", "B") };
        var ordered = Curators.Order(Curator.Spread, T("seed", "A"), pool);
        Assert.NotEqual("artist:A", ordered[0].ArtistRatingKey);
        for (var i = 1; i < 4; i++) Assert.NotEqual(ordered[i].ArtistRatingKey, ordered[i - 1].ArtistRatingKey);
    }

    [Fact]
    public void Twin_keeps_to_the_sound_of_the_seed_and_Drift_leaves_it()
    {
        var seed = T("seed", "S", 2010, ["Dance Pop"], ["Euphoric"]);
        var pool = new List<Track>();
        for (var i = 0; i < 6; i++) pool.Add(T("near" + i, "N" + i, 2010 + i % 2, ["Dance Pop"], ["Euphoric"]));
        for (var i = 0; i < 6; i++) pool.Add(T("mid" + i, "M" + i, 1995, ["Rock", "Pop"], ["Gritty"]));
        for (var i = 0; i < 6; i++) pool.Add(T("far" + i, "F" + i, 1962, ["Jazz"], ["Mellow"]));
        var mixed = pool.OrderBy(t => t.RatingKey.GetHashCode()).ToList();

        var twin = Curators.Order(Curator.Twin, seed, mixed);
        var drift = Curators.Order(Curator.Drift, seed, mixed);
        double MeanDistanceOfFirst(IReadOnlyList<Track> t, int n) => t.Take(n).Average(x => Curators.Distance(seed, x));
        double MeanDistanceOfLast(IReadOnlyList<Track> t, int n) => t.TakeLast(n).Average(x => Curators.Distance(seed, x));

        Assert.All(twin.Take(6), t => Assert.StartsWith("near", t.RatingKey));
        Assert.True(MeanDistanceOfFirst(drift, 4) < MeanDistanceOfLast(drift, 4));
        Assert.True(MeanDistanceOfLast(drift, 6) > 0.5);
    }

    [Fact]
    public void Alike_songs_are_close_and_unalike_ones_far()
    {
        var a = T("a", "A", 2010, ["Pop"], ["Happy"]);
        Assert.True(Curators.Distance(a, T("b", "B", 2011, ["Pop"], ["Happy"])) < 0.15);
        Assert.True(Curators.Distance(a, T("c", "C", 1960, ["Jazz"], ["Sad"])) > 0.8);
        Assert.Equal(Curators.Distance(a, T("c", "C", 1960, ["Jazz"], ["Sad"])), Curators.Distance(T("c", "C", 1960, ["Jazz"], ["Sad"]), a));
    }

    [Fact]
    public void Every_curator_has_a_name_and_a_line_about_it()
    {
        foreach (var c in Curators.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(Curators.Name(c)));
            Assert.False(string.IsNullOrWhiteSpace(Curators.Description(c)));
        }
        Assert.Equal(Enum.GetValues<Curator>().Length, Curators.All.Count);
    }

    [Fact]
    public void Reordering_up_next_leaves_the_playing_song_and_history_alone_and_is_one_change()
    {
        var queue = new PlaybackQueue();
        var tracks = new[] { T("h", "H"), T("now", "N"), T("a", "A"), T("b", "B"), T("c", "C"), T("b", "B") };
        queue.SetQueue(tracks, 1, false);
        var changes = 0;
        queue.Changed += (_, _) => changes++;
        queue.ReorderUpNext([tracks[4], tracks[3], tracks[2]]);
        Assert.Equal(["h", "now", "c", "b", "a", "b"], Keys(queue.Items.Select(e => e.Track)));
        Assert.Equal("now", queue.Current!.Track.RatingKey);
        Assert.Equal(1, changes);
        queue.ReorderUpNext([tracks[4], tracks[3], tracks[2]]);
        Assert.Equal(1, changes);
    }
}
