using MelodayMusic.Core.Home;

namespace MelodayMusic.Core.Tests;

public class HomeLayoutTests
{
    [Fact]
    public void Contextual_rails_keep_order_and_visibility_when_they_return()
    {
        string[] saved = ["context.season", "top.albums", "context.discovery", "context.time"];
        string[] hidden = ["context.discovery"];
        Assert.Equal(["top.albums", "context.time"],
            HomeLayout.Visible(["context.time", "context.discovery", "top.albums"], saved, hidden));
        Assert.Equal(["context.season", "top.albums", "context.time"],
            HomeLayout.Visible(["context.discovery", "context.time", "context.season", "top.albums"], saved, hidden));
    }

    private static readonly string[] Available = ["pinned", "spotlight", "recent.played", "top.albums", "hub.new"];

    [Fact]
    public void Without_an_arrangement_the_default_order_is_kept()
    {
        Assert.Equal(Available, HomeLayout.Arrange(Available, []));
    }

    [Fact]
    public void Arranged_sections_come_first_and_new_ones_follow_in_default_order()
    {
        var saved = new[] { "top.albums", "pinned", "gone.hub", "spotlight" };
        Assert.Equal(["top.albums", "pinned", "spotlight", "recent.played", "hub.new"], HomeLayout.Arrange(Available, saved));
    }

    [Fact]
    public void Hidden_sections_are_left_out_but_keep_their_place()
    {
        var saved = new[] { "top.albums", "pinned", "spotlight", "recent.played", "hub.new" };
        Assert.Equal(["top.albums", "spotlight", "hub.new"], HomeLayout.Visible(Available, saved, ["pinned", "recent.played"]));
        Assert.Equal(saved, HomeLayout.Arrange(Available, saved));
    }

    [Fact]
    public void Meloday_rails_lead_a_fresh_layout_and_library_hubs_follow()
    {
        Assert.Equal(["pinned", "artist.mixes", "context.time", "spotlight", "recent.played", "music.hub"],
            HomeLayout.CuratedFirst(["recent.played", "spotlight", "pinned", "context.time", "music.hub", "artist.mixes"]));
        Assert.True(HomeLayout.IsLibraryHub("music.hub"));
        Assert.True(HomeLayout.IsLibraryHub("recent.added"));
        Assert.False(HomeLayout.IsLibraryHub("pinned"));
        Assert.False(HomeLayout.IsLibraryHub("trending"));
    }
}
