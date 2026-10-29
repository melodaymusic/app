using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Playlists;

public static class Essentials
{
    public const int Size = 25;

    public const int Smallest = 3;

    private const string Prefix = "essentials:";

    public static string KeyFor(string artistRatingKey) => Prefix + artistRatingKey;

    public static bool IsKey(string? ratingKey) => ratingKey is { Length: > 11 } && ratingKey.StartsWith(Prefix, StringComparison.Ordinal);

    public static string? ArtistOf(string? ratingKey) => IsKey(ratingKey) ? ratingKey![Prefix.Length..] : null;

    public static string TitleFor(string artistName) => artistName + " Essentials";

    public static IReadOnlyList<Track> Pick(IEnumerable<Track> byPopularity, int size = Size)
    {
        var seen = new SongSet();
        var picked = new List<Track>();
        foreach (var track in byPopularity)
        {
            if (track.UserRating is > 0 and <= 2) continue;
            if (!seen.Add(track)) continue;
            picked.Add(track);
            if (picked.Count == size) break;
        }
        return picked;
    }

    public static uint? MostCommonColour(byte[] bgra)
    {
        const int Steps = 16;
        var counts = new int[Steps * Steps * Steps];
        var sums = new long[Steps * Steps * Steps * 3];
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (max < 36 || min > 225) continue;
            var bin = (r >> 4) * Steps * Steps + (g >> 4) * Steps + (b >> 4);
            counts[bin]++;
            sums[bin * 3] += r;
            sums[bin * 3 + 1] += g;
            sums[bin * 3 + 2] += b;
        }
        int Near(int r, int g, int b, Func<int, long> read)
        {
            long total = 0;
            for (var dr = -1; dr <= 1; dr++)
                for (var dg = -1; dg <= 1; dg++)
                    for (var db = -1; db <= 1; db++)
                    {
                        int rr = r + dr, gg = g + dg, bb = b + db;
                        if (rr is < 0 or >= Steps || gg is < 0 or >= Steps || bb is < 0 or >= Steps) continue;
                        total += read(rr * Steps * Steps + gg * Steps + bb);
                    }
            return (int)Math.Min(total, int.MaxValue);
        }
        var best = -1;
        var bestCount = 0;
        for (var r = 0; r < Steps; r++)
            for (var g = 0; g < Steps; g++)
                for (var b = 0; b < Steps; b++)
                {
                    var here = r * Steps * Steps + g * Steps + b;
                    if (counts[here] == 0) continue;
                    var score = Near(r, g, b, k => counts[k]);
                    if (score > bestCount) { bestCount = score; best = here; }
                }
        if (best < 0) return null;
        int br = best / (Steps * Steps), bg = best / Steps % Steps, bb2 = best % Steps;
        long n = 0, sr = 0, sg = 0, sb = 0;
        for (var dr = -1; dr <= 1; dr++)
            for (var dg = -1; dg <= 1; dg++)
                for (var db = -1; db <= 1; db++)
                {
                    int rr = br + dr, gg = bg + dg, bb = bb2 + db;
                    if (rr is < 0 or >= Steps || gg is < 0 or >= Steps || bb is < 0 or >= Steps) continue;
                    var k = rr * Steps * Steps + gg * Steps + bb;
                    n += counts[k];
                    sr += sums[k * 3];
                    sg += sums[k * 3 + 1];
                    sb += sums[k * 3 + 2];
                }
        return n == 0 ? null : (uint)(sr / n) << 16 | (uint)(sg / n) << 8 | (uint)(sb / n);
    }

    public static uint CoverColour(byte[] bgra)
    {
        var found = MostCommonColour(bgra);
        if (found is null)
        {
            long r = 0, g = 0, b = 0;
            var n = bgra.Length / 4;
            for (var i = 0; i + 3 < bgra.Length; i += 4) { b += bgra[i]; g += bgra[i + 1]; r += bgra[i + 2]; }
            found = n == 0 ? 0xA6C9BEu : (uint)(r / n) << 16 | (uint)(g / n) << 8 | (uint)(b / n);
        }
        return Tint(found.Value);
    }

    public static uint Tint(uint rgb)
    {
        var (h, s, v) = DominantColor.ToHsv(((rgb >> 16) & 0xFF) / 255.0, ((rgb >> 8) & 0xFF) / 255.0, (rgb & 0xFF) / 255.0);
        s = Math.Max(s, 0.24);
        v = Math.Clamp(v, 0.5, 0.9);
        var sector = h * 6 % 6;
        var c = v * s;
        var x = c * (1 - Math.Abs(sector % 2 - 1));
        var m = v - c;
        var (r1, g1, b1) = (int)sector switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static uint Byte(double d) => (uint)Math.Clamp(Math.Round(d * 255), 0, 255);
        return Byte(r1 + m) << 16 | Byte(g1 + m) << 8 | Byte(b1 + m);
    }

    public static bool InkIsDark(uint rgb)
    {
        var luminance = DominantColor.Luminance(rgb);
        return (luminance + 0.05) / 0.05 > 1.05 / (luminance + 0.05);
    }
}
