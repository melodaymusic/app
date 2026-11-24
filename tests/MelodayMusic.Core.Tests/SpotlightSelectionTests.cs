using MelodayMusic.Core.Home;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class SpotlightSelectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 14, 0, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void ArtistsStayStableWithinAPeriodAndRotateAtTheNextPeriod()
    {
        var artists = Enumerable.Range(1, 24).Select(i => new Artist { RatingKey = i.ToString(), Title = $"Artist {i}", Thumb = "thumb" }).ToList();
        string[] Keys(DateTimeOffset time) => SpotlightSelection.Pick(time, [], artists, []).Select(p => p.Item.RatingKey).ToArray();
        Assert.Equal(Keys(Now), Keys(Now.AddMinutes(50)));
        Assert.NotEqual(Keys(Now), Keys(Now.AddHours(4)));
        Assert.Equal(6, Keys(Now).Distinct().Count());
    }

    [Fact]
    public void OldLibraryAdditionsAreNotCalledNewAndDuplicatesAreExcluded()
    {
        var old = new Album { RatingKey = "album", ArtistRatingKey = "artist", Title = "Someone", Thumb = "thumb", AddedAt = Now.AddYears(-1) };
        var artist = new Artist { RatingKey = "artist", Title = "Someone", Thumb = "thumb" };
        var picks = SpotlightSelection.Pick(Now, [old, old], [artist], []);
        var pick = Assert.Single(picks);
        Assert.DoesNotContain("New", pick.Tag);
        Assert.Equal("From Your Library", Assert.Single(SpotlightSelection.Pick(Now, [old, old], [], [])).Tag);
    }

    [Fact]
    public void IncludesFreshAlbumsAndRediscoveriesWithoutRepeatingArtists()
    {
        var fresh = new Album { RatingKey = "fresh", ArtistRatingKey = "new-artist", Title = "Someone", Thumb = "thumb", AddedAt = Now.AddDays(-2) };
        var quiet = new Album { RatingKey = "quiet", ArtistRatingKey = "quiet-artist", Title = "Someone", Thumb = "thumb", ViewCount = 5, LastViewedAt = Now.AddDays(-40) };
        var artist = new Artist { RatingKey = "another-artist", Title = "Someone", Thumb = "thumb" };
        var picks = SpotlightSelection.Pick(Now, [fresh], [artist], [quiet]);
        Assert.Contains(picks, p => p.Item == fresh && p.Tag == "New Addition");
        Assert.Contains(picks, p => p.Item == quiet && p.Tag == "Rediscover");
        Assert.Contains(picks, p => p.Item == artist && p.Tag == "Artist Spotlight");
    }

    [Fact]
    public void RecentReleasesAreNewReleasesAndOldMusicJustAddedIsANewAddition()
    {
        var released = new Album { RatingKey = "r", ArtistRatingKey = "a1", ArtistTitle = "One", Thumb = "t", ReleaseDate = Now.AddDays(-10), AddedAt = Now.AddDays(-5) };
        var added = new Album { RatingKey = "o", ArtistRatingKey = "a2", ArtistTitle = "Two", Thumb = "t", ReleaseDate = Now.AddYears(-9), AddedAt = Now.AddDays(-3) };
        var picks = SpotlightSelection.Pick(Now, [released, added], [], []);
        Assert.Contains(picks, p => p.Item == released && p.Tag == "New Release");
        Assert.Contains(picks, p => p.Item == added && p.Tag == "New Addition");
    }

    [Fact]
    public void VariousArtistsIsNeverSpotlit()
    {
        var various = new Artist { RatingKey = "va", Title = "Various Artists", Thumb = "thumb" };
        var compilation = new Album { RatingKey = "comp", ArtistRatingKey = "va", ArtistTitle = "Various Artists", Title = "Hits", Thumb = "thumb", AddedAt = Now.AddDays(-1) };
        Assert.Empty(SpotlightSelection.Pick(Now, [compilation], [various], [compilation]));
    }

    [Fact]
    public void SparseLibrariesAndMissingArtworkAreHandled()
    {
        Assert.Empty(SpotlightSelection.Pick(Now, [], [], []));
        Assert.Empty(SpotlightSelection.Pick(Now, [new Album()], [new Artist()], [new Album()]));
    }

    [Fact]
    public void LeadsWithTheNewestReleaseAndAnArtistNewToTheLibrary()
    {
        var older = new Album { RatingKey = "older", ArtistRatingKey = "a1", ArtistTitle = "One", Thumb = "t", ReleaseDate = Now.AddDays(-40) };
        var newest = new Album { RatingKey = "newest", ArtistRatingKey = "a2", ArtistTitle = "Two", Thumb = "t", ReleaseDate = Now.AddDays(-3) };
        var future = new Album { RatingKey = "future", ArtistRatingKey = "a3", ArtistTitle = "Three", Thumb = "t", ReleaseDate = Now.AddDays(20) };
        var arrival = new Artist { RatingKey = "a4", Title = "Four", Thumb = "t", AddedAt = Now.AddDays(-6) };
        var stale = new Artist { RatingKey = "a5", Title = "Five", Thumb = "t", AddedAt = Now.AddYears(-2) };
        var favourite = new Artist { RatingKey = "a6", Title = "Six", Thumb = "t" };
        var picks = SpotlightSelection.Pick(Now, [], [favourite], [], [future, older, newest], [stale, arrival]);
        Assert.Equal("newest", picks[0].Item.RatingKey);
        Assert.Equal("New Release", picks[0].Tag);
        Assert.Equal(arrival, picks[1].Item);
        Assert.Equal("New Artist", picks[1].Tag);
        Assert.Contains(picks, p => p.Item == favourite && p.Tag == "Artist Spotlight");
        Assert.DoesNotContain(picks, p => p.Item.RatingKey == "future" && p.Tag == "New Release");
        Assert.DoesNotContain(picks, p => p.Item == stale && p.Tag == "New Artist");
    }
}
