using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Tests;

public class DominantColorTests
{
    private static byte[] Image(params (uint Rgb, int Count)[] parts)
    {
        var list = new List<byte>();
        foreach (var (rgb, count) in parts)
            for (var i = 0; i < count; i++)
                list.AddRange([(byte)rgb, (byte)(rgb >> 8), (byte)(rgb >> 16), 255]);
        return [.. list];
    }

    [Fact]
    public void Neutral_only_artwork_has_no_colour()
    {
        Assert.Null(DominantColor.Find(Image((0xFFFFFF, 500), (0x000000, 300), (0x808080, 200), (0xF5F5F2, 100))));
    }

    [Fact]
    public void Chromatic_colour_wins_over_more_common_white_and_black()
    {
        var c = DominantColor.Find(Image((0xFFFFFF, 800), (0x050505, 600), (0x2E5BD6, 120)));
        Assert.NotNull(c);
        var (h, s, v) = DominantColor.ToHsv(((c!.Value >> 16) & 0xFF) / 255.0, ((c.Value >> 8) & 0xFF) / 255.0, (c.Value & 0xFF) / 255.0);
        Assert.True(s > 0.5 && h > 0.55 && h < 0.7, $"expected blue, got {c:X6}");
    }

    [Fact]
    public void Pastel_colour_covering_most_of_the_artwork_wins()
    {
        var c = DominantColor.Find(Image((0xFFB6C8, 600), (0x1F8A8A, 150), (0xFFFFFF, 100)))!.Value;
        var (h, _, _) = DominantColor.ToHsv(((c >> 16) & 0xFF) / 255.0, ((c >> 8) & 0xFF) / 255.0, (c & 0xFF) / 255.0);
        Assert.True(h > 0.9 || h < 0.03, $"expected pink, got {c:X6}");
    }

    [Fact]
    public void Shades_of_one_hue_count_together()
    {
        var blues = Enumerable.Range(0, 10).Select(i => ((uint)(0x20 + i * 4) << 8 | 0x3060C0u + (uint)i * 6, 40)).ToArray();
        var c = DominantColor.Find(Image([.. blues, (0xF08020u, 250)]))!.Value;
        var (h, _, _) = DominantColor.ToHsv(((c >> 16) & 0xFF) / 255.0, ((c >> 8) & 0xFF) / 255.0, (c & 0xFF) / 255.0);
        Assert.True(h > 0.5 && h < 0.7, $"expected blue, got {c:X6}");
    }

    [Theory]
    [InlineData(0xF2E36Bu)]
    [InlineData(0xE88AC2u)]
    [InlineData(0x21D39Bu)]
    [InlineData(0x2E5BD6u)]
    public void Colours_are_adjusted_for_white_text_without_going_neutral(uint rgb)
    {
        var adjusted = DominantColor.ForWhiteText(rgb);
        Assert.True(DominantColor.Luminance(adjusted) <= 0.185, $"{adjusted:X6} too light for white text");
        var (_, s, v) = DominantColor.ToHsv(((adjusted >> 16) & 0xFF) / 255.0, ((adjusted >> 8) & 0xFF) / 255.0, (adjusted & 0xFF) / 255.0);
        Assert.True(DominantColor.IsChromatic(s, v), $"{adjusted:X6} became neutral");
    }
}

public class BackdropColourTests
{
    [Theory]
    [InlineData(0xF2F2F2u)]
    [InlineData(0xBDBDBDu)]
    [InlineData(0xFFC0DAu)]
    public void Pale_backdrop_colours_are_deepened_for_white_text(uint rgb)
    {
        Assert.True(DominantColor.Luminance(DominantColor.ForBackdrop(rgb)) <= 0.12);
    }

    [Fact]
    public void Dark_backdrop_colours_are_left_alone()
    {
        Assert.Equal(0x2A1F4Du, DominantColor.ForBackdrop(0x2A1F4Du));
    }
}

public class EdgePaletteTests
{
    private static byte[] Image(int w, int h, Func<int, int, uint> colour)
    {
        var px = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var c = colour(x, y);
            var i = (y * w + x) * 4;
            px[i] = (byte)c; px[i + 1] = (byte)(c >> 8); px[i + 2] = (byte)(c >> 16); px[i + 3] = 255;
        }
        return px;
    }

    [Fact]
    public void The_most_common_colour_wins_rather_than_an_average()
    {
        var px = Image(30, 10, (x, _) => x < 20 ? 0xC8B49Au : 0xE01010u);
        Assert.Equal(0xC8B49Au, EdgePalette.MostCommon(px, 30, 10, (0, 0, 1, 1)));
    }

    [Fact]
    public void Each_corner_comes_from_where_the_image_meets_the_page()
    {
        var px = Image(64, 36, (x, y) => y >= 30 ? 0x1030C0u : x < 12 ? 0x20A040u : 0x808080u);
        var p = EdgePalette.From(px, 64, 36)!;
        Assert.Equal(0x20A040u, p.TopLeft);
        Assert.Equal(0x808080u, p.TopRight);
        Assert.Equal(0x1030C0u, p.BottomRight);
    }

    [Fact]
    public void Neutrals_are_kept() =>
        Assert.Equal(0x202020u, EdgePalette.MostCommon(Image(8, 8, (_, _) => 0x202020u), 8, 8, (0, 0, 1, 1)));
}
