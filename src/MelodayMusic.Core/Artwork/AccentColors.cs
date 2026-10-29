namespace MelodayMusic.Core.Artwork;

public static class AccentColors
{
    public const uint DefaultDark = 0xF2AE4E;
    public const uint DefaultLight = 0xA15C07;

    private const int HueBins = 24;

    public static uint? Vibrant(byte[] bgra)
    {
        var weights = new double[HueBins];
        var sums = new double[HueBins * 3];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var (h, s, v) = DominantColor.ToHsv(r / 255.0, g / 255.0, b / 255.0);
            if (s < 0.25 || v < 0.3) continue;
            var w = s * s * v;
            var bin = Math.Min(HueBins - 1, (int)(h * HueBins));
            weights[bin] += w;
            sums[bin * 3] += r * w;
            sums[bin * 3 + 1] += g * w;
            sums[bin * 3 + 2] += b * w;
        }
        var best = -1;
        var bestScore = 0.0;
        for (var k = 0; k < HueBins; k++)
        {
            var score = weights[k] + 0.5 * (weights[(k + HueBins - 1) % HueBins] + weights[(k + 1) % HueBins]);
            if (weights[k] > 0 && score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }
        if (best < 0 || weights.Sum() < bgra.Length / 4 * 0.003) return null;
        var n = weights[best];
        return (uint)Math.Round(sums[best * 3] / n) << 16 | (uint)Math.Round(sums[best * 3 + 1] / n) << 8 | (uint)Math.Round(sums[best * 3 + 2] / n);
    }

    public static uint ForDark(uint rgb)
    {
        var (h, s, l) = DominantColor.ToHsl(rgb);
        var result = DominantColor.FromHsl(h, Math.Clamp(s, 0.55, 1), Math.Clamp(l, 0.55, 0.72));
        for (var lift = Math.Clamp(l, 0.55, 0.72); DominantColor.Luminance(result) < 0.22 && lift < 0.85; lift += 0.02)
            result = DominantColor.FromHsl(h, Math.Clamp(s, 0.55, 1), lift);
        return result;
    }

    public static uint ForLight(uint rgb)
    {
        var (h, s, l) = DominantColor.ToHsl(rgb);
        var sat = Math.Clamp(s, 0.55, 1);
        var light = Math.Clamp(l, 0.25, 0.42);
        var result = DominantColor.FromHsl(h, sat, light);
        while (DominantColor.Luminance(result) > 0.18 && light > 0.15)
        {
            light -= 0.02;
            result = DominantColor.FromHsl(h, sat, light);
        }
        return result;
    }

    public static uint Shift(uint rgb, double lightness)
    {
        var (h, s, l) = DominantColor.ToHsl(rgb);
        return DominantColor.FromHsl(h, s, Math.Clamp(l + lightness, 0, 1));
    }

    public static uint OnAccent(uint rgb) => DominantColor.Luminance(rgb) > 0.3 ? 0x16130Fu : 0xFFFFFFu;
}
