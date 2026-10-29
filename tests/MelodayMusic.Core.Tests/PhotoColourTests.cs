using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Covers;

namespace MelodayMusic.Core.Tests;

public class PhotoColourTests
{
    private const int Side = 120;

    private static byte[] Photo(Func<int, int, (int R, int G, int B)> at)
    {
        var bytes = new byte[Side * Side * 4];
        for (var y = 0; y < Side; y++)
        for (var x = 0; x < Side; x++)
        {
            var (r, g, b) = at(x, y);
            var i = (y * Side + x) * 4;
            (bytes[i], bytes[i + 1], bytes[i + 2], bytes[i + 3]) = ((byte)b, (byte)g, (byte)r, 255);
        }
        return bytes;
    }

    private static bool Middle(int x, int y) => x is >= 30 and < 90 && y is >= 22 and < 98;

    private static double Hue(uint rgb)
    {
        double r = (rgb >> 16 & 0xFF) / 255.0, g = (rgb >> 8 & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var d = max - Math.Min(r, Math.Min(g, b));
        if (d <= 0) return -1;
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60 + 360) % 360;
    }

    private static double Saturation(uint rgb)
    {
        double r = (rgb >> 16 & 0xFF) / 255.0, g = (rgb >> 8 & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        return max <= 0 ? 0 : (max - Math.Min(r, Math.Min(g, b))) / max;
    }

    [Fact]
    public void A_face_on_a_coloured_backdrop_takes_the_backdrop_not_the_skin()
    {
        var colour = PhotoColour.Pick(Photo((x, y) => Middle(x, y) ? (214, 160, 130) : (30, 140, 200)), Side, Side);
        Assert.InRange(Hue(colour), 180, 240);
    }

    [Fact]
    public void A_small_vivid_accent_wins_over_a_big_dull_area()
    {
        var colour = PhotoColour.Pick(Photo((x, y) => x < 10 && y < 60 ? (230, 20, 140) : x < 60 ? (120, 110, 80) : (95, 100, 120)), Side, Side);
        var hue = Hue(colour);
        Assert.True(hue >= 290 || hue < 20, $"hue {hue}");
    }

    [Fact]
    public void A_dark_black_and_white_photo_gets_a_light_neutral()
    {
        var colour = PhotoColour.Pick(Photo((x, y) => Middle(x, y) ? (90, 90, 90) : (40, 40, 40)), Side, Side);
        Assert.True(Saturation(colour) <= 0.20 || (colour >> 16 & 0xFF) > 150, $"{colour:x6}");
        Assert.True(((colour >> 16 & 0xFF) + (colour >> 8 & 0xFF) + (colour & 0xFF)) / 3 > 120);
    }

    [Fact]
    public void A_photo_all_of_one_hue_takes_the_opposite_one()
    {
        var colour = PhotoColour.Pick(Photo((x, y) => ((x + y) % 3) switch { 0 => (20, 60, 200), 1 => (40, 90, 220), _ => (30, 75, 180) }), Side, Side);
        var hue = Hue(colour);
        Assert.InRange(hue, 0, 90);
    }

    [Fact]
    public void The_answer_is_always_a_palette_colour_and_always_the_same()
    {
        var photo = Photo((x, y) => ((x * 7 + y * 13) % 255, (x * 3) % 255, (y * 5) % 255));
        var first = PhotoColour.Pick(photo, Side, Side);
        Assert.Equal(first, PhotoColour.Pick(photo, Side, Side));
        Assert.Contains(first, MelodayPalette.Families.Select(f => f.Original));
    }

    [Fact]
    public void An_empty_or_blown_out_photo_still_gets_a_colour()
    {
        Assert.Contains(PhotoColour.Pick([], 0, 0), MelodayPalette.Families.Select(f => f.Original));
        Assert.Contains(PhotoColour.Pick(Photo((_, _) => (255, 255, 255)), Side, Side), MelodayPalette.Families.Select(f => f.Original));
    }
}
