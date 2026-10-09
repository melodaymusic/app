using HumeMusic.Core.Models;
using HumeMusic.Core.Playlists;

namespace HumeMusic.Core.Tests;

public class PlaylistViewTests
{
    private static Track T(string key, string title, string artist, string album, int track = 1, int plays = 0, double? rating = null, long ms = 200_000, int addedDays = 0) => new()
    {
        RatingKey = key,
        Title = title,
        ArtistTitle = artist,
        AlbumTitle = album,
        TrackNumber = track,
        ViewCount = plays,
        UserRating = rating,
        DurationMs = ms,
        AddedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddDays(addedDays),
    };

    private static readonly Track[] Songs =
    [
        T("1", "Zebra", "Bee", "Hive", plays: 5, rating: 4, ms: 100_000, addedDays: 3),
        T("2", "apple", "Cat", "Barn", plays: 9, rating: 10, ms: 300_000, addedDays: 1),
        T("3", "Mango", "Ant", "Barn", track: 2, plays: 1, ms: 200_000, addedDays: 2),
    ];

    private static string[] Keys(string sort) => PlaylistView.Sort(Songs, sort).Select(t => t.RatingKey).ToArray();

    [Fact]
    public void The_playlists_own_order_is_the_order_given_and_each_sort_orders_as_it_says()
    {
        Assert.Equal(["1", "2", "3"], Keys(PlaylistView.Order));
        Assert.Equal(["2", "3", "1"], Keys("title"));
        Assert.Equal(["3", "1", "2"], Keys("artist"));
        Assert.Equal(["2", "3", "1"], Keys("album"));
        Assert.Equal(["1", "3", "2"], Keys("added"));
        Assert.Equal(["2", "1", "3"], Keys("plays"));
        Assert.Equal(["2", "1", "3"], Keys("rating"));
        Assert.Equal(["2", "3", "1"], Keys("duration"));
    }

    [Fact]
    public void Every_offered_sort_is_known_and_the_first_is_the_playlists_own()
    {
        Assert.Equal(PlaylistView.Order, PlaylistView.Sorts[0].Key);
        Assert.Equal(PlaylistView.Sorts.Count, PlaylistView.Sorts.Select(s => s.Key).Distinct().Count());
        foreach (var (_, key) in PlaylistView.Sorts.Skip(1)) Assert.NotEqual(Keys(PlaylistView.Order), Keys(key).Concat(["x"]).ToArray());
    }

    [Theory]
    [InlineData("zebra", true)]
    [InlineData("BEE", true)]
    [InlineData("hive bee", true)]
    [InlineData("zebra cat", false)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("giraffe", false)]
    public void A_song_matches_when_every_word_typed_is_in_its_title_artist_or_album(string query, bool expected) =>
        Assert.Equal(expected, PlaylistView.Matches(Songs[0], query));
}
