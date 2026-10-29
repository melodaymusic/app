namespace MelodayMusic.Core.Artwork;

public static class DominantColor
{
    private const double MinSaturation = 0.15;
    private const double MinChroma = 0.08;
    private const double MinValue = 0.14;
    private const int HueBins = 24;

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

    public static bool IsChromatic(double saturation, double value) =>
        saturation >= MinSaturation && value >= MinValue;

    public static uint ForWhiteText(uint rgb)
    {
        var (h, s, l) = ToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        s = Math.Clamp(s, 0.35, 0.85);
        l = Math.Clamp(l, 0.22, 0.6);
        var result = FromHsl(h, s, l);
        while (Luminance(result) > 0.18 && l > 0.2)
        {
            l -= 0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

    public static uint Deep(uint rgb, double lightness = 0.1)
    {
        var (h, s, _) = ToHsl(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        return FromHsl(h, Math.Clamp(s, 0.3, 0.6), lightness);
    }

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

    public static uint ForPageFill(uint rgb, bool darkText = false)
    {
        var result = rgb & 0xFFFFFF;
        var (h, s, l) = ToHsl(result);
        if (darkText)
        {
            while (Luminance(result) < 0.35 && l < 0.97)
            {
                l += 0.02;
                result = FromHsl(h, s, l);
            }
            return result;
        }
        while (Luminance(result) > 0.18 && l > 0.03)
        {
            l -= 0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

    public static double Contrast(uint a, uint b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    public static uint TintedText(uint fill, bool light, double contrast)
    {
        var (h, s, _) = ToHsl(fill & 0xFFFFFF);
        s = Math.Clamp(s, 0.25, 0.6);
        var l = light ? 0.6 : 0.4;
        var result = FromHsl(h, s, l);
        while (Contrast(result, fill) < contrast && (light ? l < 0.98 : l > 0.02))
        {
            l += light ? 0.02 : -0.02;
            result = FromHsl(h, s, l);
        }
        return result;
    }

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
