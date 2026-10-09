using HumeMusic.Core.Artwork;
using HumeMusic.Core.Models;

namespace HumeMusic.Core.Tests;

public class ArtworkTonesTests
{
    [Fact]
    public void The_four_colours_come_darkest_to_lightest_and_all_show_on_a_dark_ground()
    {
        var tones = ArtworkTones.From(new ArtworkPalette(0x808080, 0x1A2B66, 0x993322, 0x225544));
        Assert.Equal(4, tones.Vivid.Length);
        // Ordered by the colours as given (darkest first): the navy, the red, the green, the grey.
        var order = new[] { 0x1A2B66u, 0x993322u, 0x225544u, 0x808080u };
        for (var i = 0; i < 4; i++)
        {
            // Hue is kept: the brightest channel of the original is still the brightest of the vivid colour.
            var vivid = tones.Vivid[i];
            Assert.True(Math.Max(vivid >> 16 & 0xFF, Math.Max(vivid >> 8 & 0xFF, vivid & 0xFF)) >= 0.78 * 255 - 1);
            var o = order[i];
            var top = new[] { o >> 16 & 0xFF, o >> 8 & 0xFF, o & 0xFF };
            var topAt = Array.IndexOf(top, top.Max());
            var channels = new[] { vivid >> 16 & 0xFF, vivid >> 8 & 0xFF, vivid & 0xFF };
            Assert.Equal(channels.Max(), channels[topAt]);
        }
    }

    [Fact]
    public void The_ground_is_a_deep_shade_of_the_darkest_colour()
    {
        var tones = ArtworkTones.From(new ArtworkPalette(0xFFFFFF, 0x204060, 0x802020, 0xC0C0C0));
        Assert.True(ArtworkTones.Lightness(tones.Deep) < ArtworkTones.Lightness(0x204060));
        Assert.True(ArtworkTones.Lightness(tones.Deep) < 60);
    }

    [Fact]
    public void Greys_only_brighten_and_stay_grey()
    {
        var grey = ArtworkTones.Vividly(0x404040);
        Assert.Equal(grey >> 16 & 0xFF, grey & 0xFF);
        Assert.True(ArtworkTones.Lightness(grey) > ArtworkTones.Lightness(0x404040));
    }
}
