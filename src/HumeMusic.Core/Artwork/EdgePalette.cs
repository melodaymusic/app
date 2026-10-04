using HumeMusic.Core.Models;

namespace HumeMusic.Core.Artwork;

/// <summary>
/// Backdrop colours taken from where a hero image meets the page, so the image dissolves into colours
/// that are really in it. The image sits at the top right and fades out to its left and bottom, so:
/// the page's top left takes the image's left edge, its bottom left the image's bottom-left corner,
/// its bottom right the image's bottom edge, and its top right the image's top-right corner (mostly
/// under the image). Each region's colour is its most common one, neutrals included (a beige wall
/// stays beige), rather than an average that muddies two colours into a third.
/// </summary>
public static class EdgePalette
{
    // Regions as fractions of the image: left, top, right, bottom.
    private static readonly (double L, double T, double R, double B) LeftEdge = (0, 0, 0.18, 0.6);
    private static readonly (double L, double T, double R, double B) BottomLeft = (0, 0.65, 0.3, 1);
    private static readonly (double L, double T, double R, double B) BottomEdge = (0.45, 0.8, 1, 1);
    private static readonly (double L, double T, double R, double B) TopRight = (0.7, 0, 1, 0.35);

    private static readonly (int, int, int)[] Neighbours = [(-1, 0, 0), (1, 0, 0), (0, -1, 0), (0, 1, 0), (0, 0, -1), (0, 0, 1)];

    /// <summary>The four backdrop colours for BGRA pixels of a <paramref name="width"/>×<paramref name="height"/> image.</summary>
    public static ArtworkPalette? From(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;
        return new ArtworkPalette(
            MostCommon(bgra, width, height, LeftEdge),
            MostCommon(bgra, width, height, TopRight),
            MostCommon(bgra, width, height, BottomEdge),
            MostCommon(bgra, width, height, BottomLeft));
    }

    /// <summary>
    /// The most common colour in a region (0xRRGGBB): pixels are grouped into buckets of 32 levels a
    /// channel, and the fullest bucket's pixels are averaged, so the result is a colour that is
    /// actually there. The six neighbouring buckets add half their weight, so a smooth gradient that straddles
    /// a bucket edge isn't split in two.
    /// </summary>
    public static uint MostCommon(byte[] bgra, int width, int height, (double L, double T, double R, double B) region)
    {
        var x0 = (int)Math.Floor(region.L * width);
        var x1 = Math.Max(x0 + 1, (int)Math.Ceiling(region.R * width));
        var y0 = (int)Math.Floor(region.T * height);
        var y1 = Math.Max(y0 + 1, (int)Math.Ceiling(region.B * height));
        const int Levels = 8; // 3 bits a channel
        var counts = new int[Levels * Levels * Levels];
        var sums = new long[counts.Length * 3];
        for (var y = y0; y < Math.Min(y1, height); y++)
        for (var x = x0; x < Math.Min(x1, width); x++)
        {
            var i = (y * width + x) * 4;
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var bin = (r >> 5) * Levels * Levels + (g >> 5) * Levels + (b >> 5);
            counts[bin]++;
            sums[bin * 3] += r;
            sums[bin * 3 + 1] += g;
            sums[bin * 3 + 2] += b;
        }

        var best = -1;
        var bestScore = 0.0;
        for (var bin = 0; bin < counts.Length; bin++)
        {
            if (counts[bin] == 0) continue;
            var score = (double)counts[bin];
            int r = bin / (Levels * Levels), g = bin / Levels % Levels, b = bin % Levels;
            foreach (var (dr, dg, db) in Neighbours)
            {
                int nr = r + dr, ng = g + dg, nb = b + db;
                if (nr is >= 0 and < Levels && ng is >= 0 and < Levels && nb is >= 0 and < Levels)
                    score += 0.5 * counts[nr * Levels * Levels + ng * Levels + nb];
            }
            if (score > bestScore)
            {
                bestScore = score;
                best = bin;
            }
        }
        if (best < 0) return 0;
        var n = counts[best];
        return (uint)(sums[best * 3] / n) << 16 | (uint)(sums[best * 3 + 1] / n) << 8 | (uint)(sums[best * 3 + 2] / n);
    }
}
