namespace HumeMusic.Core.Artwork;

/// <summary>
/// Picks a representative, genuinely chromatic colour from artwork and makes it usable behind white
/// text. Whites, blacks and greys are never chosen; if the artwork is entirely neutral the caller
/// falls back to its own deterministic colour.
/// </summary>
public static class DominantColor
{
    // A pixel is a colour (not white, grey or black) when it has real chroma and isn't near-black.
    // Pastels such as pale pink count as colours; only true whites and greys are excluded.
    private const double MinSaturation = 0.15;
    private const double MinChroma = 0.08;
    private const double MinValue = 0.14;
    private const int HueBins = 24; // 15° families

    /// <summary>
    /// The most dominant colour in BGRA pixel data (0xRRGGBB), or null when the image has no colour.
    /// Pixels are grouped by hue family (so the shades of one gradient count together) and the
    /// family covering the most area wins; neutral pixels are ignored. The result is the average of
    /// that family's pixels.
    /// </summary>
    public static uint? Find(byte[] bgra)
    {
        if (bgra.Length < 4) return null;
        var counts = new int[HueBins];
        var sums = new long[HueBins * 3];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var (h, sat, val) = ToHsv(r / 255.0, g / 255.0, b / 255.0);
            var chroma = (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255.0;
            if (!IsChromatic(sat, val) || chroma < MinChroma) continue;
            var bin = Math.Min(HueBins - 1, (int)(h * HueBins));
            counts[bin]++;
            sums[bin * 3] += r;
            sums[bin * 3 + 1] += g;
            sums[bin * 3 + 2] += b;
        }

        // Neighbouring families share a little weight, so a hue that straddles a bin edge isn't split.
        var best = -1;
        var bestScore = 0.0;
        for (var k = 0; k < HueBins; k++)
        {
            if (counts[k] == 0) continue;
            var score = counts[k] + 0.5 * (counts[(k + HueBins - 1) % HueBins] + counts[(k + 1) % HueBins]);
            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }
        if (best < 0) return null;
        var n = counts[best];
        return (uint)(sums[best * 3] / n) << 16 | (uint)(sums[best * 3 + 1] / n) << 8 | (uint)(sums[best * 3 + 2] / n);
    }

    /// <summary>Not white, black, near-white, near-black or grey.</summary>
    public static bool IsChromatic(double saturation, double value) =>
        saturation >= MinSaturation && value >= MinValue;

    /// <summary>
    /// Adjusts a colour so white text on it is comfortably readable (WCAG AA, ≥ 4.5:1) without
    /// turning it into black: hue is kept, saturation kept vivid, lightness lowered only as needed.
    /// </summary>
    public static uint ForWhiteText(uint rgb)
    {
        var (h, s, l) = ToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        s = Math.Clamp(s, 0.35, 0.85);
        l = Math.Clamp(l, 0.22, 0.6);
        var result = FromHsl(h, s, l);
        // Step lightness down until white has enough contrast (luminance ≤ 0.18 ⇒ contrast ≥ 4.5).
        while (Luminance(result) > 0.18 && l > 0.2)
        {
            l -= 0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

    /// <summary>A deep version of the colour for long page backgrounds (still clearly that hue).</summary>
    public static uint Deep(uint rgb, double lightness = 0.1)
    {
        var (h, s, _) = ToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        return FromHsl(h, Math.Clamp(s, 0.3, 0.6), lightness);
    }

    /// <summary>
    /// A background colour white text stays readable on: same hue and saturation, with lightness
    /// lowered only as far as needed (luminance ≤ 0.12, about 6:1 against white). Dark colours are
    /// left alone, so the UltraBlur keeps its character; only whites and pale tones are deepened.
    /// </summary>
    public static uint ForBackdrop(uint rgb)
    {
        if (Luminance(rgb) <= 0.12) return rgb & 0xFFFFFF;
        var (h, s, l) = ToHsl(rgb);
        var result = rgb & 0xFFFFFF;
        while (Luminance(result) > 0.12 && l > 0.05)
        {
            l -= 0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

    /// <summary>Relative luminance (0-1).</summary>
    public static double Luminance(uint rgb)
    {
        static double Lin(uint v)
        {
            var c = v / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Lin((rgb >> 16) & 0xFF) + 0.7152 * Lin((rgb >> 8) & 0xFF) + 0.0722 * Lin(rgb & 0xFF);
    }

    public static (double H, double S, double V) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var s = max <= 0 ? 0 : d / max;
        double h = 0;
        if (d > 1e-6)
        {
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h /= 6;
        }
        return (h, s, max);
    }

    internal static (double H, double S, double L) ToHsl(uint rgb) =>
        ToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);

    private static (double H, double S, double L) ToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6) return (0, 0, l);
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        return (h / 6, s, l);
    }

    internal static uint FromHsl(double h, double s, double l)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        double r, g, b;
        if (s < 1e-6) r = g = b = l;
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Hue(p, q, h + 1.0 / 3);
            g = Hue(p, q, h);
            b = Hue(p, q, h - 1.0 / 3);
        }
        return (uint)Math.Round(r * 255) << 16 | (uint)Math.Round(g * 255) << 8 | (uint)Math.Round(b * 255);
    }
}
