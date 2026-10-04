using HumeMusic.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace HumeMusic.Helpers;

/// <summary>
/// Ambient page colour taken from an item's artwork palette (Apple Music style tinted headers).
/// The raw artwork colour is tamed so text stays readable: dark and muted in the dark theme,
/// pale in the light theme.
/// </summary>
public static class Ambient
{
    public static Color? TintFor(ArtworkPalette? palette, ElementTheme theme)
    {
        if (palette is null) return null;
        // The top edge of the artwork is what sits behind the header.
        var (r, g, b) = Blend(palette.TopLeft, palette.TopRight);
        var (h, s, l) = ToHsl(r, g, b);
        var dark = theme != ElementTheme.Light;
        s = Math.Min(s, dark ? 0.55 : 0.45);
        l = dark ? Math.Clamp(l, 0.12, 0.24) : Math.Clamp(l, 0.86, 0.92);
        var (nr, ng, nb) = FromHsl(h, s, l);
        return Color.FromArgb(255, nr, ng, nb);
    }

    /// <summary>A vertical wash: full tint at the top fading to transparent, over the normal page colour.</summary>
    public static Brush? WashFor(ArtworkPalette? palette, ElementTheme theme)
    {
        if (TintFor(palette, theme) is not { } tint) return null;
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop { Color = tint, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(170, tint.R, tint.G, tint.B), Offset = 0.55 });
        brush.GradientStops.Add(new GradientStop { Color = Color.FromArgb(0, tint.R, tint.G, tint.B), Offset = 1 });
        return brush;
    }

    private static (double R, double G, double B) Blend(uint a, uint b) =>
        ((((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) / 2.0 / 255,
         (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) / 2.0 / 255,
         ((a & 0xFF) + (b & 0xFF)) / 2.0 / 255);

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

    private static (byte R, byte G, byte B) FromHsl(double h, double s, double l)
    {
        if (s < 1e-6)
        {
            var v = (byte)Math.Round(l * 255);
            return (v, v, v);
        }
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        var q2 = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var p2 = 2 * l - q2;
        return ((byte)Math.Round(Hue(p2, q2, h + 1.0 / 3) * 255),
                (byte)Math.Round(Hue(p2, q2, h) * 255),
                (byte)Math.Round(Hue(p2, q2, h - 1.0 / 3) * 255));
    }
}
