using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Tests;

public class ReleaseTests
{
    private static Album A(string title, string[]? format = null, string[]? sub = null, int? tracks = null) =>
        new() { RatingKey = title, Title = title, Formats = format ?? [], Subformats = sub ?? [], TrackCount = tracks };

    [Theory]
    [InlineData("Pink Friday", new[] { "Album" }, new string[0], null, ReleaseKind.Album)]
    [InlineData("Barbie Drip", new[] { "Single" }, new string[0], null, ReleaseKind.Single)]
    [InlineData("Beam Me Up", new[] { "EP" }, new string[0], null, ReleaseKind.EP)]
    [InlineData("Live at Wembley", new[] { "Album" }, new[] { "Live" }, null, ReleaseKind.Live)]
    [InlineData("Greatest Hits", new[] { "Album" }, new[] { "Compilation" }, null, ReleaseKind.Compilation)]
    [InlineData("Barbie World (Remixes)", new[] { "EP" }, new string[0], null, ReleaseKind.Remix)]
    [InlineData("Super Freaky Girl", new[] { "Single" }, new[] { "Remix" }, null, ReleaseKind.Remix)]
    [InlineData("Barbie: The Album", new[] { "Album" }, new[] { "Soundtrack" }, null, ReleaseKind.Soundtrack)]
    [InlineData("Beam Me Up Scotty", new[] { "Mixtape/Street" }, new string[0], null, ReleaseKind.Mixtape)]
    public void Tags_decide_the_kind(string title, string[] format, string[] sub, int? tracks, ReleaseKind expected) =>
        Assert.Equal(expected, Releases.Classify(A(title, format, sub, tracks)));

    [Fact]
    public void Untagged_releases_follow_their_hub_and_length()
    {
        Assert.Equal(ReleaseKind.Single, Releases.Classify(A("Yikes", tracks: 1), "artist.albums.singles"));
        Assert.Equal(ReleaseKind.EP, Releases.Classify(A("Five Songs", tracks: 5), "artist.albums.singles"));
        Assert.Equal(ReleaseKind.Live, Releases.Classify(A("On Stage"), "artist.albums.live"));
        Assert.Equal(ReleaseKind.Album, Releases.Classify(A("Queen")));
        Assert.Equal(ReleaseKind.Remix, Releases.Classify(A("Queen Remixed")));
    }

    [Fact]
    public void Artists_rank_by_plays_sharing_places_on_ties()
    {
        var ranks = LibraryRank.Rank([("a", 50), ("b", 120), ("c", 50), ("d", 10), ("e", 0)]);
        Assert.Equal(1, ranks["b"]);
        Assert.Equal(2, ranks["a"]);
        Assert.Equal(2, ranks["c"]);
        Assert.Equal(4, ranks["d"]);
        Assert.False(ranks.ContainsKey("e"));
        Assert.Equal("#3 in your library", LibraryRank.Describe(3));
    }

    [Theory]
    [InlineData(3, 100, true)]
    [InlineData(101, 100, false)]
    [InlineData(5000, -1, true)]
    [InlineData(1, 0, false)]
    public void The_setting_limits_which_ranks_show(int rank, int limit, bool shown) =>
        Assert.Equal(shown, LibraryRank.IsShown(rank, limit));
}

public class TagFamilyTests
{
    [Theory]
    [InlineData("Dance Pop", "Pop")]
    [InlineData("Bedroom Pop", "Pop")]
    [InlineData("K-Pop", "Pop")]
    [InlineData("Pop Rap", "Hip-Hop")]
    [InlineData("Alternative R&B/Deep House", "R&B & Soul")]
    [InlineData("Indie Pop", "Alternative")]
    [InlineData("Deep House", "Electronic")]
    [InlineData("Dubstep", "Electronic")]
    [InlineData("Dub", "Reggae")]
    [InlineData("Soft Rock", "Rock")]
    [InlineData("Adult Standards", "Easy Listening")]
    public void Genres_join_their_family(string genre, string family) =>
        Assert.Equal(family, TagFamilies.FamilyOf(genre, TagKind.Genre));

    [Theory]
    [InlineData("Amiable/Good-Natured", "Happy & Upbeat")]
    [InlineData("Angst-Ridden", "Sad & Reflective")]
    [InlineData("Aggressive", "Energetic & Intense")]
    [InlineData("Airy", "Chill & Calm")]
    [InlineData("Acerbic", "Dark & Brooding")]
    public void Moods_join_their_family(string mood, string family) =>
        Assert.Equal(family, TagFamilies.FamilyOf(mood, TagKind.Mood));

    [Fact]
    public void Unknown_tags_stay_themselves() => Assert.Null(TagFamilies.FamilyOf("Speedrun", TagKind.Genre));

    private static TagItem T(string title, string id, int count) => new() { RatingKey = id, Title = title, FilterValue = id, Count = count, TagKind = TagKind.Genre };

    [Fact]
    public void Families_gather_their_members_and_singles_stay_as_they_are()
    {
        var grouped = TagFamilies.Group([T("Dance Pop", "1", 5), T("Soft Pop", "2", 9), T("Soft Rock", "3", 2), T("Speedrun", "4", 1)], TagKind.Genre);
        Assert.Equal(["Pop", "Soft Rock", "Speedrun"], grouped.Select(g => g.Title));
        var pop = grouped.Single(g => g.Title == "Pop");
        Assert.True(pop.IsFamily);
        Assert.Equal("2,1", pop.FilterValue);
        Assert.Equal(["Soft Pop", "Dance Pop"], pop.Members.Select(m => m.Title));
        Assert.False(grouped.Single(g => g.Title == "Soft Rock").IsFamily);
    }

    [Fact]
    public void Decades_are_left_alone()
    {
        var decades = new[] { new TagItem { Title = "1990s", FilterValue = "1990", TagKind = TagKind.Decade } };
        Assert.Same(decades, TagFamilies.Group(decades, TagKind.Decade));
    }
}
