using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Tests;

public class AccentColorsTests
{
    private static byte[] Image(params (uint Rgb, int Count)[] parts)
    {
        var list = new List<byte>();
        foreach (var (rgb, count) in parts)
            for (var i = 0; i < count; i++)
                list.AddRange([(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255]);
        return [.. list];
    }

    private static double Hue(uint c) => DominantColor.ToHsv(((c >> 16) & 0xFF) / 255.0, ((c >> 8) & 0xFF) / 255.0, (c & 0xFF) / 255.0).H;

    [Fact]
    public void Vivid_colour_beats_a_larger_dull_one()
    {
        var c = AccentColors.Vibrant(Image((0x6B6A4A, 700), (0xFF2A8C, 150), (0x101010, 150)))!.Value;
        Assert.True(Hue(c) > 0.85, $"expected pink, got {c:X6}");
    }

    [Fact]
    public void Colourless_artwork_has_no_accent()
    {
        Assert.Null(AccentColors.Vibrant(Image((0xFFFFFF, 500), (0x202020, 500), (0x8A8A8A, 200))));
    }

    [Theory]
    [InlineData(0x1A237Eu)]
    [InlineData(0xFFEB3Bu)]
    [InlineData(0x2E7D32u)]
    public void Accents_stay_legible_on_both_themes(uint rgb)
    {
        Assert.True(DominantColor.Luminance(AccentColors.ForDark(rgb)) >= 0.2, "too dark on a dark background");
        Assert.True(DominantColor.Luminance(AccentColors.ForLight(rgb)) <= 0.185, "too light on a light background");
    }

    [Fact]
    public void Text_on_the_accent_contrasts_with_it()
    {
        Assert.Equal(0x16130Fu, AccentColors.OnAccent(AccentColors.DefaultDark));
        Assert.Equal(0xFFFFFFu, AccentColors.OnAccent(AccentColors.DefaultLight));
    }
}
