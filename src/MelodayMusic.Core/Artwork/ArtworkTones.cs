using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Artwork;

public readonly record struct ArtworkTones(uint[] Vivid, uint Deep)
{
    public static ArtworkTones From(ArtworkPalette palette)
    {
        var raw = new[] { palette.TopLeft, palette.TopRight, palette.BottomRight, palette.BottomLeft }
            .Select(c => c & 0xFFFFFF)
            .OrderBy(Lightness)
            .ToArray();
        return new ArtworkTones(raw.Select(Vividly).ToArray(), Darkened(raw[0], 0.4));
    }

    public static double Lightness(uint rgb) => 0.2126 * (rgb >> 16 & 0xFF) + 0.7152 * (rgb >> 8 & 0xFF) + 0.0722 * (rgb & 0xFF);

    public static uint Vividly(uint rgb)
    {
        var (h, s, v) = ToHsv(rgb);
        var saturation = s < 0.12 ? s : Math.Min(1, s * 1.12 + 0.04);
        return FromHsv(h, saturation, Math.Max(v, 0.78));
    }

    public static uint Darkened(uint rgb, double amount)
    {
        uint Part(int shift) => (uint)Math.Round((rgb >> shift & 0xFF) * (1 - amount));
        return Part(16) << 16 | Part(8) << 8 | Part(0);
    }

    private static (double H, double S, double V) ToHsv(uint rgb)
    {
        double r = (rgb >> 16 & 0xFF) / 255.0, g = (rgb >> 8 & 0xFF) / 255.0, b = (rgb & 0xFF) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var d = max - Math.Min(r, Math.Min(g, b));
        var h = d < 1e-9 ? 0 : (max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
        return ((h + 360) % 360, max <= 0 ? 0 : d / max, max);
    }

    private static uint FromHsv(double h, double s, double v)
    {
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        uint To(double p) => (uint)Math.Clamp(Math.Round((p + m) * 255), 0, 255);
        return To(r) << 16 | To(g) << 8 | To(b);
    }
}
