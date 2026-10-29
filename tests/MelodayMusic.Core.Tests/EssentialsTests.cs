using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;

namespace MelodayMusic.Core.Tests;

public class EssentialsTests
{
    private static Track Song(string key, string title, double? rating = null) => new() { RatingKey = key, Title = title, ArtistTitle = "Artist", UserRating = rating };

    [Fact]
    public void A_key_says_whose_essentials_it_is()
    {
        var key = Essentials.KeyFor("1234");
        Assert.True(Essentials.IsKey(key));
        Assert.Equal("1234", Essentials.ArtistOf(key));
        Assert.False(Essentials.IsKey("1234"));
        Assert.False(Essentials.IsKey("essentials:"));
        Assert.Null(Essentials.ArtistOf("lastfm-top100"));
        Assert.Null(Essentials.ArtistOf(null));
    }

    [Fact]
    public void The_most_popular_song_comes_first_and_the_list_stops_at_25()
    {
        var ranked = Enumerable.Range(0, 40).Select(i => Song(i.ToString(), "Song " + i)).ToList();
        var picked = Essentials.Pick(ranked);
        Assert.Equal(Essentials.Size, picked.Count);
        Assert.Equal("0", picked[0].RatingKey);
        Assert.Equal(Enumerable.Range(0, 25).Select(i => i.ToString()), picked.Select(t => t.RatingKey));
    }

    [Fact]
    public void A_song_listed_twice_counts_once_at_its_most_popular_place_and_a_one_star_song_is_left_out()
    {
        var picked = Essentials.Pick([
            Song("a", "Hit"), Song("b", "Hit - Remastered"), Song("c", "Disliked", rating: 2), Song("d", "Other"), Song("e", "Loved", rating: 10),
        ]);
        Assert.Equal(new[] { "a", "d", "e" }, picked.Select(t => t.RatingKey));
    }

    private static byte[] Pixels(params (uint Rgb, int Count)[] groups)
    {
        var bytes = new List<byte>();
        foreach (var (rgb, count) in groups)
            for (var i = 0; i < count; i++) bytes.AddRange([(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255]);
        return [.. bytes];
    }

    [Fact]
    public void The_most_common_colour_wins_even_when_it_is_muted()
    {
        var teal = Essentials.MostCommonColour(Pixels((0x7C9A9Fu, 600), (0xE0904Au, 250), (0xFFFFFFu, 400), (0x000000u, 400)));
        Assert.NotNull(teal);
        Assert.InRange((int)((teal!.Value >> 16) & 0xFF), 0x70, 0x88);
        Assert.InRange((int)((teal.Value >> 8) & 0xFF), 0x90, 0xA4);
        Assert.InRange((int)(teal.Value & 0xFF), 0x94, 0xA8);
    }

    [Fact]
    public void Shades_of_one_colour_count_together_and_black_and_white_do_not_count()
    {
        var shades = Pixels((0x3060C0u, 100), (0x3262C2u, 100), (0x2E5EBEu, 100), (0xC04030u, 250), (0xFFFFFFu, 5000), (0x000000u, 5000));
        var found = Essentials.MostCommonColour(shades)!.Value;
        Assert.True((found & 0xFF) > (found >> 16 & 0xFF), "blue should beat the single orange, white and black ignored");
        Assert.Null(Essentials.MostCommonColour(Pixels((0xFFFFFFu, 50), (0x000000u, 50))));
        Assert.Null(Essentials.MostCommonColour([]));
    }

    [Fact]
    public void A_dull_colour_is_tinted_but_keeps_its_hue_and_a_clear_one_is_left_alone()
    {
        var dull = Essentials.Tint(0x889096u);
        var (h0, _, _) = MelodayMusic.Core.Artwork.DominantColor.ToHsv(0x88 / 255.0, 0x90 / 255.0, 0x96 / 255.0);
        var (h1, s1, v1) = MelodayMusic.Core.Artwork.DominantColor.ToHsv(((dull >> 16) & 0xFF) / 255.0, ((dull >> 8) & 0xFF) / 255.0, (dull & 0xFF) / 255.0);
        Assert.True(s1 >= 0.22, "saturation is lifted");
        Assert.InRange(Math.Abs(h1 - h0), 0, 0.02);
        Assert.InRange(v1, 0.5, 0.9);
        var clear = Essentials.Tint(0x58B89Cu);
        Assert.True(Math.Abs((int)((clear >> 16) & 0xFF) - 0x58) <= 3 && Math.Abs((int)((clear >> 8) & 0xFF) - 0xB8) <= 3 && Math.Abs((int)(clear & 0xFF) - 0x9C) <= 3, "a clear colour stays as it is");
        Assert.Equal(Essentials.Tint(0xA6C9BEu), Essentials.CoverColour([]));
    }

    [Theory]
    [InlineData(0xFFFFFFu, true)]
    [InlineData(0xA6C9BEu, true)]
    [InlineData(0xF2C94Cu, true)]
    [InlineData(0x1F3A93u, false)]
    [InlineData(0x000000u, false)]
    [InlineData(0x7A1F2Bu, false)]
    public void Ink_is_black_on_a_light_colour_and_white_on_a_dark_one(uint colour, bool dark) =>
        Assert.Equal(dark, Essentials.InkIsDark(colour));
}
