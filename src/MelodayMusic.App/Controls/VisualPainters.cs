using System.Numerics;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Audio;
using MelodayMusic.Core.Lyrics;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playback;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI;

namespace MelodayMusic.Controls;

internal sealed record VisualPalette(Color Main, Color Light)
{
    public Color Back => Deep ?? Mix(Main, Microsoft.UI.Colors.Black, 0.88);

    public Color? Deep { get; init; }

    public IReadOnlyList<Color> Colours { get; init; } = [];

    public Color Tone(int i) => Colours.Count == 4 ? Colours[i] : i switch { 0 => Neighbour(-46), 1 => Neighbour(-20), 2 => Main, _ => Light };

    public static VisualPalette FromTones(ArtworkTones tones)
    {
        static Color Of(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
        var colours = tones.Vivid.Select(Of).ToArray();
        var main = colours.OrderByDescending(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))).First();
        return new VisualPalette(main, Mix(main, Microsoft.UI.Colors.White, 0.5)) { Deep = Of(tones.Deep), Colours = colours };
    }

    public static VisualPalette Default { get; } = From(Color.FromArgb(255, 242, 174, 78));

    public static VisualPalette From(Color colour)
    {
        double r = colour.R / 255.0, g = colour.G / 255.0, b = colour.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var hue = d < 1e-6 ? 0 : (max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
        hue = (hue + 360) % 360;
        var saturation = max <= 0 ? 0 : Math.Min(1, d / max * 1.2);
        var value = Math.Max(max, 0.82);
        var main = FromHsv(hue, saturation, value);
        return new VisualPalette(main, Mix(main, Microsoft.UI.Colors.White, 0.5));
    }

    public Color Neighbour(double degrees)
    {
        double r = Main.R / 255.0, g = Main.G / 255.0, b = Main.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        var hue = d < 1e-6 ? 0 : (max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4) * 60;
        return FromHsv(((hue + degrees) % 360 + 360) % 360, max <= 0 ? 0 : Math.Min(1, d / max), max);
    }

    private static Color FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;
        var (r, g, b) = (int)(hue / 60) switch { 0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x), 3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x) };
        return Color.FromArgb(255, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public static Color Mix(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    }

    public static Color With(Color c, double alpha) => Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
}

internal sealed class VisualInput
{
    public float[] Pcm { get; set; } = [];
    public int SampleRate { get; set; } = 44100;
    public float RenderScale { get; set; } = 1;
    public int FramesPerSecond { get; set; } = 60;
    public required SpectrumAnalyzer Sound { get; set; }
    public required BeatTracker Beat { get; init; }
    public double Time { get; set; }
    public VisualPalette Palette { get; set; } = VisualPalette.Default;
    public TimeSpan Position { get; set; }
    public TrackLyrics? Lyrics { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public IReadOnlyList<BackdropImage> Backdrops { get; set; } = [];
}

internal sealed record BackdropImage(string Path, bool Soft);

internal interface IVisualPainter
{
    void Draw(CanvasDrawingSession ds, VisualInput input, float width, float height);
}

internal static class VisualPainters
{
    public static IVisualPainter? For(VisualizerStyle style) => style switch
    {
        VisualizerStyle.Bars => new BarsPainter(),
        VisualizerStyle.Halo => new HaloPainter(),
        VisualizerStyle.Wave => new WavePainter(),
        VisualizerStyle.Sun => new SunPainter(),
        VisualizerStyle.Pillars => new PillarsPainter(),
        VisualizerStyle.Tiles => new TilesPainter(),
        VisualizerStyle.LyricVideo => new LyricPainter(backdrop: true),
        VisualizerStyle.LyricGlow => new LyricPainter(backdrop: false),
        _ when VisualizerStyles.IsProjectM(style) => new ProjectMPainter(style),
        _ => null,
    };

    public static float Band(float[] bands, int i, int count)
    {
        var at = i * (bands.Length - 1) / (float)Math.Max(1, count - 1);
        var lo = (int)at;
        var hi = Math.Min(bands.Length - 1, lo + 1);
        var t = at - lo;
        return bands[lo] * (1 - t) + bands[hi] * t;
    }

    public static float Average(float[] values, int from, int to)
    {
        float sum = 0;
        for (var i = from; i < to; i++) sum += values[i];
        return sum / Math.Max(1, to - from);
    }

    public static float Smooth(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3 - 2 * t);
    }
}

internal static class VisualFont
{
    private static readonly Dictionary<string, bool> PackageForm = [];

    public static CanvasTextLayout Layout(ICanvasResourceCreator creator, string text, float size, int weight, LyricFont font, float width, float height, CanvasHorizontalAlignment align)
    {
        var type = LyricFonts.For(font);
        var (file, family, asked) = type.Resolve(weight);
        var fontWeight = new Windows.UI.Text.FontWeight { Weight = (ushort)asked };
        var own = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", file)).AbsoluteUri + "#" + family;
        var package = "ms-appx:///Assets/Fonts/" + file + "#" + family;
        string[] candidates = PackageForm.TryGetValue(file, out var usePackage) ? [usePackage ? package : own] : [own, package, type.SystemFallback];
        foreach (var name in candidates)
        {
            using var format = new CanvasTextFormat { FontFamily = name, FontSize = size, FontWeight = fontWeight, HorizontalAlignment = align, WordWrapping = CanvasWordWrapping.Wrap };
            try
            {
                var layout = new CanvasTextLayout(creator, text, format, width, height);
                if (name != type.SystemFallback) PackageForm[file] = name == package;
                return layout;
            }
            catch (ArgumentException) when (name != type.SystemFallback)
            {
            }
        }
        throw new InvalidOperationException("No typeface for the visualizer.");
    }
}
