using MelodayMusic.Core.Artwork;

namespace MelodayMusic.Core.Covers;

/// <summary>
/// Picks a photo cover's edge-bar colour: the photo's most characteristic hue (clustered in Lab, skin tones down-weighted),
/// snapped to the Meloday palette.
/// </summary>
public static class PhotoColour
{
    private readonly record struct Rgb(int R, int G, int B)
    {
        public uint Packed => (uint)(R << 16 | G << 8 | B);
        public static Rgb From(uint rgb) => new((int)(rgb >> 16 & 0xFF), (int)(rgb >> 8 & 0xFF), (int)(rgb & 0xFF));
    }

    private readonly record struct Lab(double L, double A, double B);

    private sealed record Candidate(double Score, Rgb Colour, double Saturation, double Brightness, double Share, bool Skin);

    /// <summary>The edge colour for a photo given as BGRA pixels (Win2D's byte order), ideally 100–200 pixels square.</summary>
    public static uint Pick(ReadOnlySpan<byte> bgra, int width, int height)
    {
        var all = new List<(Rgb Colour, int X, int Y)>(width * height);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            if (i + 3 >= bgra.Length) break;
            all.Add((new Rgb(bgra[i + 2], bgra[i + 1], bgra[i]), x, y));
        }
        var image = all.Select(p => p.Colour).ToList();
        if (image.Count == 0) return ByBrightness(0.5);

        var mid = image.Where(c => Brightness(c) is > 0.08 and < 0.92).ToList();
        if (mid.Count < 50) return ByBrightness(AverageBrightness(image));

        var saturations = mid.Select(Saturation).OrderBy(s => s).ToList();
        var median = saturations[saturations.Count / 2];
        var colourful = saturations.Count(s => s > 0.15) / (double)saturations.Count;

        var edges = Edges(all, width, height);
        Rgb? accent = edges.FirstOrDefault(e => e.Saturation >= 0.25) is { } found && found.Saturation > 0 ? found.Colour : null;

        if (colourful < 0.20 || median < 0.15)
            return accent is { } edge ? Nearest(edge) : ByBrightness(AverageBrightness(image));

        var lab = mid.Select(ToLab).ToList();
        var (centres, labels) = Cluster(lab, Math.Min(10, lab.Distinct().Count()), tries: 3);
        var counts = new int[centres.Count];
        foreach (var label in labels) counts[label]++;

        var scored = new List<Candidate>();
        for (var i = 0; i < centres.Count; i++)
        {
            var colour = ToRgb(centres[i]);
            var s = Saturation(colour);
            if (s < 0.15) continue;
            var bright = Brightness(colour);
            var share = counts[i] / (double)mid.Count;
            var score = Math.Pow(s, 1.2) * Striking(Hue(colour)) * Math.Sqrt(share);
            if (s > 0.70 && share < 0.10 && bright > 0.20) score *= 4.0;
            var lift = 1.0;
            foreach (var e in edges)
            {
                var distance = Distance(colour, e.Colour);
                if (distance < 25) lift = Math.Max(lift, 1.5);
                else if (distance < 50) lift = Math.Max(lift, 1.25);
            }
            scored.Add(new Candidate(score * lift, colour, s, bright, share, IsSkin(colour)));
        }

        // A strong background colour no cluster caught (split or averaged away) gets its own say.
        foreach (var e in edges)
        {
            if (e.Saturation < 0.20 || e.Share < 0.15) continue;
            if (scored.Any(c => Distance(e.Colour, c.Colour) < 40)) continue;
            var score = Math.Pow(e.Saturation, 1.2) * Striking(Hue(e.Colour)) * Math.Sqrt(e.Share) * 1.3;
            scored.Add(new Candidate(score, e.Colour, e.Saturation, Brightness(e.Colour), e.Share, IsSkin(e.Colour)));
        }

        if (scored.Count == 0) return ByBrightness(AverageBrightness(image));
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        var winner = scored.FirstOrDefault(c => !c.Skin) ?? scored[0];
        var best = winner.Colour;

        if (winner.Brightness is < 0.08 or > 0.92)
        {
            var next = scored.Skip(1).FirstOrDefault(c => c.Brightness is >= 0.08 and <= 0.92);
            if (next is null) return ByBrightness(AverageBrightness(image));
            best = next.Colour;
        }

        var top = scored.Take(5).ToList();
        if (top.Count >= 2)
        {
            var hues = top.Select(c => Hue(c.Colour)).ToList();
            var spread = hues.Max() - hues.Min();
            if (spread > 0.5) spread = 1 - spread;
            if (spread < 0.10 && top.Sum(c => c.Share) > 0.50)
            {
                var (h, s, v) = Hsv(best);
                return Nearest(FromHsv((h + 0.5) % 1, s, v));
            }
        }
        return Nearest(best);
    }

    /// <summary>The main colours round the edges of the photo, outside the middle where the subject usually is; biggest first.</summary>
    private static List<(Rgb Colour, double Saturation, double Share)> Edges(List<(Rgb Colour, int X, int Y)> pixels, int width, int height)
    {
        int left = (int)(width * 0.20), right = (int)(width * 0.80), top = (int)(height * 0.15), bottom = (int)(height * 0.85);
        var around = pixels.Where(p => p.X < left || p.X >= right || p.Y < top || p.Y >= bottom).Select(p => p.Colour).ToList();
        if (around.Count < 30) return [];
        around = around.Where(c => Brightness(c) is > 0.08 and < 0.92).ToList();
        if (around.Count < 20) return [];
        var chromatic = around.Where(c => Saturation(c) >= 0.15 && !IsSkin(c)).ToList();
        if (chromatic.Count < 15) return [];

        var lab = chromatic.Select(ToLab).ToList();
        var k = Math.Min(3, lab.Distinct().Count());
        if (k < 1) return [];
        var (centres, labels) = Cluster(lab, k, tries: 2);
        var counts = new int[centres.Count];
        foreach (var label in labels) counts[label]++;
        return centres.Select((c, i) => (Colour: ToRgb(c), Share: counts[i] / (double)chromatic.Count))
            .Select(e => (e.Colour, Saturation: Saturation(e.Colour), e.Share))
            .Where(e => e.Saturation >= 0.15)
            .OrderByDescending(e => e.Share)
            .ToList();
    }

    /// <summary>k-means in Lab, started k-means++ style from a fixed seed so a photo always gives the same answer.</summary>
    private static (List<Lab> Centres, int[] Labels) Cluster(List<Lab> points, int k, int tries)
    {
        k = Math.Max(1, Math.Min(k, points.Count));
        var random = new Random(42);
        List<Lab>? bestCentres = null;
        int[]? bestLabels = null;
        var bestCost = double.MaxValue;
        for (var attempt = 0; attempt < tries; attempt++)
        {
            var centres = new List<Lab> { points[random.Next(points.Count)] };
            var nearest = new double[points.Count];
            while (centres.Count < k)
            {
                double total = 0;
                for (var i = 0; i < points.Count; i++)
                {
                    nearest[i] = centres.Min(c => Squared(points[i], c));
                    total += nearest[i];
                }
                if (total <= 0) break;
                var pick = random.NextDouble() * total;
                var chosen = points.Count - 1;
                for (var i = 0; i < points.Count; i++)
                {
                    pick -= nearest[i];
                    if (pick <= 0) { chosen = i; break; }
                }
                centres.Add(points[chosen]);
            }

            var labels = new int[points.Count];
            var cost = 0.0;
            for (var round = 0; round < 25; round++)
            {
                var moved = false;
                cost = 0;
                for (var i = 0; i < points.Count; i++)
                {
                    var (label, distance) = (0, double.MaxValue);
                    for (var c = 0; c < centres.Count; c++)
                    {
                        var d = Squared(points[i], centres[c]);
                        if (d < distance) (label, distance) = (c, d);
                    }
                    moved |= labels[i] != label;
                    labels[i] = label;
                    cost += distance;
                }
                var sums = new (double L, double A, double B, int N)[centres.Count];
                for (var i = 0; i < points.Count; i++)
                {
                    var s = sums[labels[i]];
                    sums[labels[i]] = (s.L + points[i].L, s.A + points[i].A, s.B + points[i].B, s.N + 1);
                }
                for (var c = 0; c < centres.Count; c++)
                    if (sums[c].N > 0) centres[c] = new Lab(sums[c].L / sums[c].N, sums[c].A / sums[c].N, sums[c].B / sums[c].N);
                if (!moved && round > 0) break;
            }
            if (cost < bestCost)
            {
                (bestCost, bestCentres, bestLabels) = (cost, centres, labels);
            }
        }
        return (bestCentres!, bestLabels!);
    }

    /// <summary>For a photo with no colour to go on: a palette neutral of the opposite brightness, so the bars still show.</summary>
    internal static uint ByBrightness(double imageBrightness)
    {
        var target = 1 - imageBrightness;
        var originals = MelodayPalette.Families.Select(f => Rgb.From(f.Original)).ToList();
        var neutral = originals
            .Where(c => Saturation(c) <= 0.20 && Math.Abs(Brightness(c) - target) <= 0.15)
            .OrderBy(c => Math.Abs(Brightness(c) - target))
            .ToList();
        if (neutral.Count > 0) return neutral[0].Packed;
        return originals.OrderBy(c => Math.Abs(Brightness(c) - target)).First().Packed;
    }

    /// <summary>The palette's own colour (never a lighter or darker shade) nearest in Lab.</summary>
    internal static uint Nearest(uint rgb) => Nearest(Rgb.From(rgb));

    private static uint Nearest(Rgb colour) =>
        MelodayPalette.Families.Select(f => Rgb.From(f.Original)).OrderBy(c => Distance(colour, c)).First().Packed;

    private static double AverageBrightness(List<Rgb> image) => image.Average(Brightness);

    private static double Brightness(Rgb c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static bool IsSkin(Rgb c)
    {
        var (h, s, v) = Hsv(c);
        return (h < 0.08 || h > 0.95) && s is > 0.15 and < 0.45 && v is > 0.3 and < 0.95;
    }

    /// <summary>Reds, magentas and purples catch the eye most, then oranges and cyans; yellow-greens least.</summary>
    private static double Striking(double hue)
    {
        var degrees = hue * 360;
        if (degrees >= 330 || degrees < 15 || degrees is >= 280 and < 330) return 1.3;
        if (degrees is >= 15 and < 45 or >= 180 and < 210) return 1.15;
        if (degrees is >= 45 and < 75 or >= 210 and < 240) return 1.0;
        return 0.85;
    }

    private static double Saturation(Rgb c) => Hsv(c).S;

    private static double Hue(Rgb c) => Hsv(c).H;

    private static (double H, double S, double V) Hsv(Rgb c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var s = max <= 0 ? 0 : d / max;
        if (d <= 0) return (0, s, max);
        var h = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h /= 6;
        if (h < 0) h += 1;
        return (h, s, max);
    }

    private static Rgb FromHsv(double h, double s, double v)
    {
        var i = (int)Math.Floor(h * 6) % 6;
        var f = h * 6 - Math.Floor(h * 6);
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return new Rgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
    }

    private static double Distance(Rgb a, Rgb b) => Math.Sqrt(Squared(ToLab(a), ToLab(b)));

    private static double Squared(Lab a, Lab b) => (a.L - b.L) * (a.L - b.L) + (a.A - b.A) * (a.A - b.A) + (a.B - b.B) * (a.B - b.B);

    private static Lab ToLab(Rgb c)
    {
        static double Linear(double v) => v > 0.04045 ? Math.Pow((v + 0.055) / 1.055, 2.4) : v / 12.92;
        double r = Linear(c.R / 255.0), g = Linear(c.G / 255.0), b = Linear(c.B / 255.0);
        var x = (r * 0.4124 + g * 0.3576 + b * 0.1805) / 0.95047;
        var y = r * 0.2126 + g * 0.7152 + b * 0.0722;
        var z = (r * 0.0193 + g * 0.1192 + b * 0.9505) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : (903.3 * t + 16) / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static Rgb ToRgb(Lab lab)
    {
        var fy = (lab.L + 16) / 116;
        var fx = lab.A / 500 + fy;
        var fz = fy - lab.B / 200;
        var xr = Math.Pow(fx, 3) > 0.008856 ? Math.Pow(fx, 3) : (116 * fx - 16) / 903.3;
        var yr = Math.Pow(fy, 3) > 0.008856 ? Math.Pow(fy, 3) : lab.L / 903.3;
        var zr = Math.Pow(fz, 3) > 0.008856 ? Math.Pow(fz, 3) : (116 * fz - 16) / 903.3;
        double x = xr * 0.95047, y = yr, z = zr * 1.08883;
        var r = x * 3.2406 + y * -1.5372 + z * -0.4986;
        var g = x * -0.9689 + y * 1.8758 + z * 0.0415;
        var b = x * 0.0557 + y * -0.2040 + z * 1.0570;
        static int Gamma(double v)
        {
            v = v > 0.0031308 ? 1.055 * Math.Pow(Math.Max(0, v), 1 / 2.4) - 0.055 : 12.92 * v;
            return (int)(Math.Clamp(v, 0, 1) * 255);
        }
        return new Rgb(Gamma(r), Gamma(g), Gamma(b));
    }
}
