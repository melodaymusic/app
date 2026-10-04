namespace HumeMusic.Core.Artwork;

/// <summary>
/// The app accent derived from artwork: the most vivid colour in it, tuned per theme so accent
/// text, sliders and switches stay legible (light enough on dark backgrounds, dark enough on light).
/// </summary>
public static class AccentColors
{
    /// <summary>The default accent (Hume yellow) when nothing is playing or the artwork has no colour.</summary>
    public const uint DefaultDark = 0xF2AE4E;
    public const uint DefaultLight = 0xA15C07;

    private const int HueBins = 24;

    /// <summary>
    /// The most vivid colour family in BGRA pixel data (0xRRGGBB), or null for colourless artwork.
    /// Pixels are grouped by hue and weighted by saturation² × brightness, so a vivid colour that is
    /// genuinely present wins over a larger dull one.
    /// </summary>
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
        // A handful of coloured pixels isn't enough to call the artwork colourful (judged over all hues:
        // colourful artwork often spreads its colour across several families).
        if (best < 0 || weights.Sum() < bgra.Length / 4 * 0.003) return null;
        var n = weights[best];
        return (uint)Math.Round(sums[best * 3] / n) << 16 | (uint)Math.Round(sums[best * 3 + 1] / n) << 8 | (uint)Math.Round(sums[best * 3 + 2] / n);
    }

    /// <summary>The accent for dark backgrounds: vivid and light enough to read as text.</summary>
    public static uint ForDark(uint rgb)
    {
        var (h, s, l) = DominantColor.ToHsl(rgb);
        var result = DominantColor.FromHsl(h, Math.Clamp(s, 0.55, 1), Math.Clamp(l, 0.55, 0.72));
        // Some hues (deep blues) stay dark at that lightness: lift until there is contrast on near-black.
        for (var lift = Math.Clamp(l, 0.55, 0.72); DominantColor.Luminance(result) < 0.22 && lift < 0.85; lift += 0.02)
            result = DominantColor.FromHsl(h, Math.Clamp(s, 0.55, 1), lift);
        return result;
    }

    /// <summary>The accent for light backgrounds: deep enough for text on near-white (≥ 4.5:1).</summary>
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

    /// <summary>The same hue with its lightness shifted (hover and pressed states).</summary>
    public static uint Shift(uint rgb, double lightness)
    {
        var (h, s, l) = DominantColor.ToHsl(rgb);
        return DominantColor.FromHsl(h, s, Math.Clamp(l + lightness, 0, 1));
    }

    /// <summary>Text drawn on the accent (a play button's glyph, a switch's knob): near-black or white.</summary>
    public static uint OnAccent(uint rgb) => DominantColor.Luminance(rgb) > 0.3 ? 0x16130Fu : 0xFFFFFFu;
}
