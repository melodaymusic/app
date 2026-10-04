using HumeMusic.Core.Artwork;

namespace HumeMusic.Core.Tests;

public class MelodayPaletteTests
{
    private static readonly HashSet<uint> AllShades = MelodayPalette.Families.SelectMany(f => f.Shades).ToHashSet();

    [Theory]
    [InlineData(0xDC143Cu, "Crimson")]      // exact palette colour
    [InlineData(0x1E5ADCu, "Cobalt")]
    [InlineData(0xFF7FB8u, "Hot Pink")]     // one step off
    [InlineData(0x0A8F90u, "Dark Teal")]
    public void Colours_match_their_nearest_family(uint rgb, string family)
    {
        Assert.Equal(family, MelodayPalette.NearestShade(rgb).Family.Name);
    }

    [Theory]
    [InlineData(0x3D6E2Fu)]
    [InlineData(0xC0C0C0u)]
    [InlineData(0x101010u)]
    [InlineData(0xFFFFFFu)]
    public void Every_match_is_a_palette_colour(uint rgb)
    {
        Assert.Contains(MelodayPalette.Nearest(rgb), AllShades);
        Assert.Contains(MelodayPalette.ForWhiteText(rgb), AllShades);
        Assert.Contains(MelodayPalette.Accent(rgb, dark: true), AllShades);
        Assert.Contains(MelodayPalette.Accent(rgb, dark: false), AllShades);
    }

    [Theory]
    [InlineData(0xFFE1E6u)] // pale pink: Dusty Pink lighter
    [InlineData(0xF2E6FCu)] // pale lilac
    [InlineData(0xFFEB7Du)] // pale gold
    public void Backgrounds_under_white_text_take_a_darker_shade(uint rgb)
    {
        Assert.True(DominantColor.Luminance(MelodayPalette.ForWhiteText(rgb)) <= 0.3);
    }

    [Theory]
    [InlineData(0xDC143Cu)] // Crimson original: its darker shade
    [InlineData(0x960A28u)] // Crimson darker: deeper still
    [InlineData(0x2D2350u)] // Navy Indigo darker
    public void Darker_shade_is_always_darker_than_the_colour(uint rgb)
    {
        var darker = MelodayPalette.DarkerShade(rgb);
        Assert.NotEqual(rgb, darker);
        Assert.True(DominantColor.Luminance(darker) < DominantColor.Luminance(rgb));
    }
}
