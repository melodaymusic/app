namespace MelodayMusic.Core.Artwork;

public static class GlowPalette
{
    public static readonly (double X, double Y)[] Points =
    [
        (0, 0), (0.5, 0), (1, 0), (1, 0.5), (1, 1), (0.5, 1), (0, 1), (0, 0.5),
    ];

    private const double Reach = 0.25;

    public static uint[]? From(byte[] bgra, int width, int height)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4) return null;
        var edges = Points.Select(p =>
        {
            var left = Math.Clamp(p.X - Reach / 2, 0, 1 - Reach);
            var top = Math.Clamp(p.Y - Reach / 2, 0, 1 - Reach);
            return EdgePalette.MostCommon(bgra, width, height, (left, top, left + Reach, top + Reach));
        }).ToArray();

        var flat = Spread(edges) < 48 || edges.All(c => DominantColor.Luminance(c) < 0.02);
        var vivid = Vivid(bgra);
        if (!flat && !(edges.All(IsNeutral) && vivid.Count > 0)) return edges;
        if (vivid.Count >= 2) return Points.Select((_, i) => vivid[i % vivid.Count]).ToArray();
        if (vivid.Count == 1)
        {
            var family = MelodayPalette.NearestShade(vivid[0]).Family;
            return Tonal(family.Lighter, family.Original, family.Darker);
        }
        var tone = edges[0];
        return Tonal(Mix(tone, 0xFFFFFF, 0.18), tone, Mix(tone, 0x000000, 0.35));
    }

    private static uint[] Tonal(uint light, uint middle, uint dark) => [light, light, middle, middle, dark, dark, middle, middle];

    private static uint Mix(uint a, uint b, double t)
    {
        uint Channel(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Channel(16) << 16 | Channel(8) << 8 | Channel(0);
    }

    private static double Spread(uint[] colours)
    {
        var most = 0.0;
        for (var i = 0; i < colours.Length; i++)
        for (var j = i + 1; j < colours.Length; j++)
        {
            double dr = (int)((colours[i] >> 16) & 0xFF) - (int)((colours[j] >> 16) & 0xFF);
            double dg = (int)((colours[i] >> 8) & 0xFF) - (int)((colours[j] >> 8) & 0xFF);
            double db = (int)(colours[i] & 0xFF) - (int)(colours[j] & 0xFF);
            most = Math.Max(most, Math.Sqrt(dr * dr + dg * dg + db * db));
        }
        return most;
    }

    private static bool IsNeutral(uint rgb)
    {
        var (_, s, v) = DominantColor.ToHsv(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        return !DominantColor.IsChromatic(s, v);
    }

    private static List<uint> Vivid(byte[] bgra)
    {
        const int bins = 24;
        var counts = new int[bins];
        var sums = new long[bins * 3];
        var pixels = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            pixels++;
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var (h, s, v) = DominantColor.ToHsv(r / 255.0, g / 255.0, b / 255.0);
            var chroma = (Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b))) / 255.0;
            if (!DominantColor.IsChromatic(s, v) || chroma < 0.08) continue;
            var bin = Math.Min(bins - 1, (int)(h * bins));
            counts[bin]++;
            sums[bin * 3] += r;
            sums[bin * 3 + 1] += g;
            sums[bin * 3 + 2] += b;
        }
        var coloured = counts.Sum();
        var found = new List<uint>();
        if (coloured < Math.Max(1, pixels * 0.03)) return found;
        var chosen = new List<int>();
        foreach (var bin in Enumerable.Range(0, bins).OrderByDescending(k => counts[k]))
        {
            if (found.Count == 3 || counts[bin] < coloured * 0.08) break;
            if (chosen.Any(c => Math.Min(Math.Abs(c - bin), bins - Math.Abs(c - bin)) < 3)) continue;
            chosen.Add(bin);
            var n = counts[bin];
            found.Add((uint)(sums[bin * 3] / n) << 16 | (uint)(sums[bin * 3 + 1] / n) << 8 | (uint)(sums[bin * 3 + 2] / n));
        }
        return found;
    }

    public static byte[] Render(IReadOnlyList<uint> colours, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        if (colours.Count != Points.Length || width <= 0 || height <= 0) return pixels;
        var aspect = width / (double)height;
        const double spread = 0.42;
        var weights = new double[Points.Length];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var u = (x + 0.5) / width;
            var v = (y + 0.5) / height;
            double total = 0, r = 0, g = 0, b = 0;
            for (var i = 0; i < Points.Length; i++)
            {
                var dx = (u - Points[i].X) * aspect;
                var dy = v - Points[i].Y;
                var w = Math.Exp(-(dx * dx + dy * dy) / (spread * spread));
                weights[i] = w;
                total += w;
            }
            for (var i = 0; i < Points.Length; i++)
            {
                var c = colours[i];
                var w = weights[i] / total;
                r += ((c >> 16) & 0xFF) * w;
                g += ((c >> 8) & 0xFF) * w;
                b += (c & 0xFF) * w;
            }
            var o = (y * width + x) * 4;
            pixels[o] = (byte)Math.Round(b);
            pixels[o + 1] = (byte)Math.Round(g);
            pixels[o + 2] = (byte)Math.Round(r);
            pixels[o + 3] = 255;
        }
        return pixels;
    }
}
