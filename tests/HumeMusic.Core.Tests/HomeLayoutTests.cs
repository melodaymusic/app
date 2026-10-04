using HumeMusic.Core.Home;

namespace HumeMusic.Core.Tests;

public class HomeLayoutTests
{
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
}
