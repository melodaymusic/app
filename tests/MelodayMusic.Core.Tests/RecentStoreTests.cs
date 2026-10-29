using MelodayMusic.Core.Home;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Pins;

namespace MelodayMusic.Core.Tests;

public class RecentStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Newest_first_and_playing_again_moves_it_to_the_front()
    {
        var store = new RecentStore();
        store.Record("s1", new Album { RatingKey = "1", Title = "One" }, T0);
        store.Record("s1", new Playlist { RatingKey = "2", Title = "Two" }, T0.AddMinutes(1));
        store.Record("s1", new Artist { RatingKey = "3", Title = "Three" }, T0.AddMinutes(2));
        store.Record("s1", new Album { RatingKey = "1", Title = "One" }, T0.AddMinutes(3));
        Assert.Equal(["1", "3", "2"], store.Get("s1").Select(i => i.RatingKey));
        Assert.Equal(2, store.Get("s1", 2).Count);
        Assert.Empty(store.Get("other"));
    }

    [Fact]
    public void A_song_counts_as_its_album_and_what_cant_be_pinned_is_ignored()
    {
        var store = new RecentStore();
        store.Record("s1", new Track { RatingKey = "9", Title = "Song", AlbumRatingKey = "7", AlbumTitle = "The Album", AlbumThumb = "/a" }, T0);
        store.Record("s1", new Track { RatingKey = "10", Title = "Loose song" }, T0);
        store.Record("s1", new Station { RatingKey = "builder:artists", Title = "Builder", StationKind = StationKind.ArtistMixBuilder }, T0);
        var only = Assert.Single(store.Get("s1"));
        Assert.Equal((PinKind.Album, "7", "The Album", "/a"), (only.Kind, only.RatingKey, only.Title, only.Thumb));
    }

    [Fact]
    public void Made_items_are_kept_with_what_they_need_and_it_survives_a_restart()
    {
        var path = Path.Combine(Path.GetTempPath(), "meloday-recent-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var first = new RecentStore(path);
            first.Record("s1", new Station { RatingKey = "station:meloday", Title = "Meloday", StationKind = StationKind.Meloday }, T0);
            first.Record("s1", new TagMix { RatingKey = "mix:genre:Pop", Title = "Pop Mix", TagKind = MixTagKind.Genre, Tag = "Pop" }, T0.AddMinutes(1));
            var again = new RecentStore(path).Get("s1");
            Assert.Equal(["mix:genre:Pop", "station:meloday"], again.Select(i => i.RatingKey));
            Assert.Equal("Pop", Assert.IsType<TagMix>(again[0].ToItem()).Tag);
            Assert.Equal(StationKind.Meloday, Assert.IsType<Station>(again[1].ToItem()).StationKind);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Only_the_most_recent_are_kept()
    {
        var store = new RecentStore();
        for (var i = 0; i < 100; i++) store.Record("s1", new Album { RatingKey = "a" + i, Title = "A" + i }, T0.AddMinutes(i));
        Assert.Equal(60, store.Get("s1").Count);
        Assert.Equal("a99", store.Get("s1")[0].RatingKey);
    }
}
