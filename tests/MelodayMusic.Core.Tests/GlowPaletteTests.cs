using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Tests;

public class GlowPaletteTests
{
    [Fact]
    public void Each_colour_is_taken_from_its_own_corner_or_edge()
    {
        const int size = 40;
        var bgra = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var i = (y * size + x) * 4;
            var (r, g, b) = y >= 30 ? (0, 255, 0) : x < 20 ? (255, 0, 0) : (0, 0, 255);
            bgra[i] = (byte)b; bgra[i + 1] = (byte)g; bgra[i + 2] = (byte)r; bgra[i + 3] = 255;
        }
        var colours = GlowPalette.From(bgra, size, size)!;
        Assert.Equal(8, colours.Length);
        Assert.True((colours[0] >> 16) > 200 && (colours[0] & 0xFF) < 50);
        Assert.True((colours[2] & 0xFF) > 200 && (colours[2] >> 16) < 50);
        Assert.True(((colours[5] >> 8) & 0xFF) > 200);
    }

    private static byte[] Image(int size, Func<int, int, (int R, int G, int B)> colour)
    {
        var bgra = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var i = (y * size + x) * 4;
            var (r, g, b) = colour(x, y);
            bgra[i] = (byte)b; bgra[i + 1] = (byte)g; bgra[i + 2] = (byte)r; bgra[i + 3] = 255;
        }
        return bgra;
    }

    [Fact]
    public void A_dark_frame_takes_its_glow_from_the_colours_inside()
    {
        var bgra = Image(40, (x, y) => x is < 12 or >= 28 || y is < 12 or >= 28 ? (0, 0, 0) : x < 20 ? (220, 30, 40) : (140, 40, 220));
        var colours = GlowPalette.From(bgra, 40, 40)!;
        Assert.All(colours, c => Assert.True(DominantColor.Luminance(c) > 0.02));
        Assert.True(colours.Distinct().Count() >= 2);
    }

    [Fact]
    public void A_single_colour_cover_gets_a_gradient_of_its_family()
    {
        var colours = GlowPalette.From(Image(40, (_, _) => (220, 20, 60)), 40, 40)!;
        Assert.True(colours.Distinct().Count() >= 3);
        Assert.True(DominantColor.Luminance(colours[0]) > DominantColor.Luminance(colours[4]));
    }

    [Fact]
    public void A_black_cover_gets_a_dark_tonal_gradient()
    {
        var colours = GlowPalette.From(Image(40, (_, _) => (5, 5, 5)), 40, 40)!;
        Assert.True(colours.Distinct().Count() >= 2);
        Assert.All(colours, c => Assert.True(DominantColor.Luminance(c) < 0.1));
    }

    [Fact]
    public void Glow_keeps_true_colours_at_their_points_and_blends_between()
    {
        uint[] colours = [0xFF0000, 0xFF0000, 0x0000FF, 0x0000FF, 0x0000FF, 0xFF0000, 0xFF0000, 0xFF0000];
        const int width = 64, height = 36;
        var pixels = GlowPalette.Render(colours, width, height);
        Assert.Equal(width * height * 4, pixels.Length);
        Assert.True(pixels[2] > 200 && pixels[0] < 60);
        var right = ((height / 2) * width + width - 1) * 4;
        Assert.True(pixels[right] > 200 && pixels[right + 2] < 60);
        Assert.All(Enumerable.Range(0, width * height), i => Assert.Equal(255, pixels[i * 4 + 3]));
    }
}
