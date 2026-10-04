namespace HumeMusic.Core.Artwork;

/// <summary>A palette colour family: a lighter, the original and a darker shade.</summary>
public sealed record PaletteFamily(string Name, uint Lighter, uint Original, uint Darker)
{
    public IEnumerable<uint> Shades => [Lighter, Original, Darker];
}

/// <summary>
/// Meloday's colour palette, and Colour Drop: every colour the app takes from artwork (UltraBlur
/// backgrounds, colour boards, page accents, artist logos) is dropped onto its closest colour here,
/// so the whole app speaks one consistent set of colours.
/// </summary>
public static class MelodayPalette
{
    private static uint Rgb(int r, int g, int b) => (uint)(r << 16 | g << 8 | b);

    public static IReadOnlyList<PaletteFamily> Families { get; } =
    [
        new("Crimson", Rgb(245, 115, 135), Rgb(220, 20, 60), Rgb(150, 10, 40)),
        new("Deep Berry", Rgb(240, 95, 125), Rgb(190, 10, 55), Rgb(120, 5, 35)),
        new("Persimmon", Rgb(253, 165, 140), Rgb(238, 80, 5), Rgb(165, 45, 25)),
        new("Burnt Orange", Rgb(235, 150, 110), Rgb(200, 80, 45), Rgb(135, 45, 20)),
        new("Coral", Rgb(255, 175, 145), Rgb(255, 110, 75), Rgb(185, 60, 35)),
        new("Apricot", Rgb(255, 215, 185), Rgb(255, 155, 110), Rgb(195, 95, 60)),
        new("Amber", Rgb(255, 225, 105), Rgb(255, 180, 0), Rgb(180, 120, 0)),
        new("Bright Gold", Rgb(255, 235, 125), Rgb(255, 200, 0), Rgb(185, 140, 0)),
        new("Ochre", Rgb(245, 220, 145), Rgb(225, 175, 70), Rgb(160, 115, 30)),
        new("Leaf Green", Rgb(170, 235, 145), Rgb(90, 215, 70), Rgb(55, 145, 40)),
        new("Dark Teal", Rgb(125, 205, 205), Rgb(0, 145, 145), Rgb(0, 95, 95)),
        new("Deep Forest", Rgb(145, 180, 170), Rgb(20, 80, 65), Rgb(10, 50, 40)),
        new("Turquoise", Rgb(180, 245, 225), Rgb(45, 200, 170), Rgb(25, 135, 112)),
        new("Mint", Rgb(195, 250, 230), Rgb(35, 205, 155), Rgb(20, 140, 102)),
        new("Sky Blue", Rgb(180, 225, 245), Rgb(60, 165, 225), Rgb(40, 115, 165)),
        new("Azure", Rgb(160, 225, 255), Rgb(30, 180, 255), Rgb(15, 115, 185)),
        new("Cobalt", Rgb(130, 175, 255), Rgb(30, 90, 220), Rgb(15, 55, 150)),
        new("Cornflower", Rgb(195, 215, 255), Rgb(115, 155, 245), Rgb(75, 105, 185)),
        new("Navy Indigo", Rgb(170, 165, 195), Rgb(75, 65, 120), Rgb(45, 35, 80)),
        new("Violet", Rgb(210, 175, 255), Rgb(135, 50, 255), Rgb(85, 20, 180)),
        new("Vivid Purple", Rgb(230, 205, 250), Rgb(185, 135, 230), Rgb(120, 80, 165)),
        new("Soft Lilac", Rgb(242, 230, 252), Rgb(215, 190, 245), Rgb(145, 115, 180)),
        new("Plum", Rgb(205, 160, 185), Rgb(135, 55, 105), Rgb(85, 25, 65)),
        new("Magenta", Rgb(255, 165, 220), Rgb(225, 20, 135), Rgb(150, 10, 85)),
        new("Hot Pink", Rgb(255, 195, 225), Rgb(255, 128, 185), Rgb(180, 60, 115)),
        new("Dusty Pink", Rgb(255, 225, 230), Rgb(255, 185, 198), Rgb(205, 125, 140)),
    ];

    /// <summary>The palette colour closest to <paramref name="rgb"/> (perceptual distance, CIE Lab ΔE).</summary>
    public static uint Nearest(uint rgb) => NearestShade(rgb).Colour;

    /// <summary>The closest palette colour and the family it belongs to.</summary>
    public static (PaletteFamily Family, uint Colour) NearestShade(uint rgb)
    {
        var target = ToLab(rgb);
        PaletteFamily best = Families[0];
        var bestColour = best.Original;
        var bestDistance = double.MaxValue;
        foreach (var family in Families)
        {
            foreach (var shade in family.Shades)
            {
                var d = Distance(target, ToLab(shade));
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = family;
                    bestColour = shade;
                }
            }
        }
        return (best, bestColour);
    }

    /// <summary>
    /// The closest palette colour for a background under white text: the nearest match, or a darker
    /// shade of the same family when the match is too light to read white text on.
    /// </summary>
    public static uint ForWhiteText(uint rgb)
    {
        var (family, colour) = NearestShade(rgb);
        if (DominantColor.Luminance(colour) <= 0.3) return colour;
        return DominantColor.Luminance(family.Original) <= 0.3 ? family.Original : family.Darker;
    }

    /// <summary>The lighter shade of the palette family a colour belongs to.</summary>
    public static uint LighterShade(uint rgb) => NearestShade(rgb).Family.Lighter;

    /// <summary>
    /// A shade darker than a palette colour: its family's darker shade, or, when the colour already is
    /// that shade, the same colour a step deeper (so the two always read as different).
    /// </summary>
    public static uint DarkerShade(uint rgb)
    {
        var (family, colour) = NearestShade(rgb);
        if (family.Darker != colour && family.Darker != rgb) return family.Darker;
        static uint Scale(uint c, int shift) => (uint)Math.Round(((c >> shift) & 0xFF) * 0.72);
        return Scale(rgb, 16) << 16 | Scale(rgb, 8) << 8 | Scale(rgb, 0);
    }

    /// <summary>
    /// The page accent from an artwork colour: its palette family, in the shade that reads on the
    /// current theme (vivid or lighter on dark backgrounds, darker on light ones).
    /// </summary>
    public static uint Accent(uint rgb, bool dark)
    {
        var (family, _) = NearestShade(rgb);
        if (!dark) return family.Darker;
        return DominantColor.Luminance(family.Original) >= 0.15 ? family.Original : family.Lighter;
    }

    private static double Distance((double L, double A, double B) x, (double L, double A, double B) y) =>
        Math.Sqrt((x.L - y.L) * (x.L - y.L) + (x.A - y.A) * (x.A - y.A) + (x.B - y.B) * (x.B - y.B));

    private static (double L, double A, double B) ToLab(uint rgb)
    {
        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        var r = Lin(((rgb >> 16) & 0xFF) / 255.0);
        var g = Lin(((rgb >> 8) & 0xFF) / 255.0);
        var b = Lin((rgb & 0xFF) / 255.0);
        var x = (0.4124 * r + 0.3576 * g + 0.1805 * b) / 0.95047;
        var y = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var z = (0.0193 * r + 0.1192 * g + 0.9505 * b) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }
}
