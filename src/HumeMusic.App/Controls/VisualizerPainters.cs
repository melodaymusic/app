using System.Numerics;
using HumeMusic.Core.Artwork;
using HumeMusic.Core.Audio;
using HumeMusic.Core.Lyrics;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playback;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>The colours a visualizer draws in: the artwork's own, a lighter one of it, and a very dark one of it to draw on.</summary>
internal sealed record VisualPalette(Color Main, Color Light)
{
    /// <summary>The ground a picture sits on: a deep shade of the artwork's darkest colour (or, with only one colour known, of that).</summary>
    public Color Back => Deep ?? Mix(Main, Microsoft.UI.Colors.Black, 0.88);

    public Color? Deep { get; init; }

    /// <summary>The artwork's four colours (see <see cref="ArtworkTones"/>), darkest first, each brightened to show on a dark ground; empty when only one colour is known.</summary>
    public IReadOnlyList<Color> Colours { get; init; } = [];

    /// <summary>
    /// One of the four colours every picture draws with (0 darkest, 3 lightest), so they all share the artwork's colours. With
    /// only one colour known, the neighbours of it stand in.
    /// </summary>
    public Color Tone(int i) => Colours.Count == 4 ? Colours[i] : i switch { 0 => Neighbour(-46), 1 => Neighbour(-20), 2 => Main, _ => Light };

    /// <summary>The palette of the artwork's four colours (Plex's UltraBlur corners).</summary>
    public static VisualPalette FromTones(ArtworkTones tones)
    {
        static Color Of(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF));
        var colours = tones.Vivid.Select(Of).ToArray();
        // The main colour is the most colourful of the four.
        var main = colours.OrderByDescending(c => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B))).First();
        return new VisualPalette(main, Mix(main, Microsoft.UI.Colors.White, 0.5)) { Deep = Of(tones.Deep), Colours = colours };
    }

    public static VisualPalette Default { get; } = From(Color.FromArgb(255, 242, 174, 78));

    /// <summary>The palette of an artwork's colour, brightened (hue kept, a little more colour) where it is too dark to show on its own dark shade.</summary>
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

    /// <summary>The artwork's colour turned round the colour wheel by <paramref name="degrees"/> (a neighbour of it), for a line that shifts colour along its length.</summary>
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

/// <summary>Everything a painter is given for a frame: the analysed sound, the beat, the time, the colours, and what the lyric pictures need.</summary>
internal sealed class VisualInput
{
    public required SpectrumAnalyzer Sound { get; set; }
    public required BeatTracker Beat { get; init; }
    /// <summary>Seconds since the picture began (the clock the motion runs on).</summary>
    public double Time { get; set; }
    public VisualPalette Palette { get; set; } = VisualPalette.Default;
    /// <summary>Where the music is, in the song.</summary>
    public TimeSpan Position { get; set; }
    public TrackLyrics? Lyrics { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    /// <summary>How long the song is (lyrics with no timing scroll over it).</summary>
    public TimeSpan Duration { get; set; }
    /// <summary>The pictures the lyric video cycles through behind the words (the artist's backdrop, the album's cover, the artist's portrait), as files on this computer.</summary>
    public IReadOnlyList<BackdropImage> Backdrops { get; set; } = [];
}

/// <summary>A picture for behind the lyrics; a <paramref name="Soft"/> one (a square cover or portrait, not a wide backdrop) is blurred, so filling the screen with it looks like a haze rather than a stretched photograph.</summary>
internal sealed record BackdropImage(string Path, bool Soft);

/// <summary>One way of drawing the music: given a frame's input, it paints it. Painters keep their own state (history, phases, loaded pictures).</summary>
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
        VisualizerStyle.LyricVideo => new LyricPainter(backdrop: true),
        VisualizerStyle.LyricGlow => new LyricPainter(backdrop: false),
        _ => null,
    };

    /// <summary>A band of the analysis for position <paramref name="i"/> of <paramref name="count"/>, blended with its neighbours so shapes stay smooth.</summary>
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

/// <summary>The classic spectrum analyser: rounded bars rising from a baseline, a gradient up each, and peak caps that hang and fall.</summary>
internal sealed class BarsPainter : IVisualPainter
{
    private const int Count = 56;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p) = (v.Sound, v.Palette);
        var pad = w * 0.07f;
        var slot = (w - pad * 2) / Count;
        var bw = slot * 0.72f;
        var baseY = h * 0.86f;
        var tallest = h * 0.66f;
        using var fill = new CanvasLinearGradientBrush(ds, p.Tone(1), p.Tone(3)) { StartPoint = new Vector2(0, baseY), EndPoint = new Vector2(0, baseY - tallest) };
        for (var i = 0; i < Count; i++)
        {
            var x = pad + i * slot + (slot - bw) / 2;
            var level = VisualPainters.Band(sound.Bands, i, Count);
            var bh = Math.Max(bw, level * tallest);
            ds.FillRoundedRectangle(x, baseY - bh, bw, bh, bw / 2, bw / 2, fill);
            var peak = VisualPainters.Band(sound.Peaks, i, Count);
            var py = baseY - Math.Max(bw, peak * tallest) - bw * 1.5f;
            ds.FillRoundedRectangle(x, py, bw, bw * 0.55f, bw / 4, bw / 4, VisualPalette.With(Microsoft.UI.Colors.White, 0.85));
        }
    }
}

/// <summary>Spokes round a ring, the spectrum folded left to right so the picture is symmetrical, the ring swelling with the bass.</summary>
internal sealed class HaloPainter : IVisualPainter
{
    private const int Spokes = 96;
    private readonly CanvasStrokeStyle _round = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round };

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p) = (v.Sound, v.Palette);
        var c = new Vector2(w / 2, h / 2);
        var m = Math.Min(w, h);
        var r0 = m * 0.22f * (1 + 0.07f * sound.Bass);
        var reach = m * 0.26f;
        var stroke = Math.Max(2f, m * 0.0085f);
        ds.FillCircle(c, r0 * 0.97f, VisualPalette.With(p.Tone(2), 0.10 + 0.22 * sound.Bass));
        ds.DrawCircle(c, r0 * 0.97f, VisualPalette.With(p.Tone(3), 0.55), Math.Max(1.5f, m * 0.004f));
        for (var s = 0; s < Spokes; s++)
        {
            var fold = s <= Spokes / 2 ? s : Spokes - s;
            var level = VisualPainters.Band(sound.Bands, fold, Spokes / 2 + 1);
            var angle = s / (float)Spokes * MathF.Tau - MathF.PI / 2;
            var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            var length = m * 0.012f + level * reach;
            ds.DrawLine(c + dir * (r0 * 1.04f), c + dir * (r0 * 1.04f + length), VisualPalette.Mix(p.Tone(1), p.Tone(3), level), stroke, _round);
        }
    }
}

/// <summary>
/// M-Wave (the Meloday wave): a thick rounded line drawn as the logo's wave is in the startup animation, but loose and liquid. It is
/// not the raw waveform (which is all edges) but a few slow waves laid over each other, each carried by its own part of
/// the sound: the bass, the low mids, the voice and the air. The sound enters at the left and flows to the right (what is
/// heard now is at the left, what was heard a moment ago further along), the waves themselves travel the same way at a
/// pace set by the song's tempo, and each beat gives the whole line a soft swell. With a soft glow, fading echoes
/// trailing the line and a colour that shifts along its length, a brighter band of which runs through it from the left
/// on each beat and each new line of the lyrics. In silence it settles to the flat bar the animation starts from.
/// </summary>
internal sealed class WavePainter : IVisualPainter
{
    private const int Points = 48, Echoes = 3, History = 20, EchoGap = 5;
    // How the flow is remembered: a sample of the four parts of the sound every Interval seconds; the sound takes Crossing seconds to flow across.
    private const int Memory = 160, Layers = 4;
    private const double Interval = 1.0 / 50, Crossing = 1.5;
    private static readonly float[] Cycles = [0.8f, 1.5f, 2.3f, 3.4f];
    // How fast each wave travels, as a share of the slowest.
    private static readonly float[] Pace = [1f, 1.35f, 1.8f, 2.4f];
    private static readonly float[] Reach = [0.50f, 0.36f, 0.30f, 0.16f];
    private static readonly float[] EchoAlpha = [0.22f, 0.14f, 0.07f];
    private readonly CanvasStrokeStyle _round = new() { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };
    private readonly float[][] _history = Enumerable.Range(0, History).Select(_ => new float[Points]).ToArray();
    private readonly float[][] _flow = Enumerable.Range(0, Memory).Select(_ => new float[Layers]).ToArray();
    private readonly float[] _amplitude = new float[Layers];
    private readonly float[] _phase = [0, 1.7f, 3.1f, 4.6f];
    private int _newest, _flowNewest;
    private double _last = -1, _sinceSample, _tempo = 110;
    private VisualPalette? _coloursOf;
    private readonly Color[] _tones = new Color[4];
    private readonly List<CanvasGradientStop> _stops = new(6);

    /// <summary>The colour along the line: the artwork's neighbour on one side, the artwork's colour, the neighbour on the other.</summary>
    private Color Along(VisualPalette p, float x)
    {
        var at = Math.Clamp(x, 0, 1) * 3;
        var i = Math.Min(2, (int)at);
        return VisualPalette.Mix(_tones[i], _tones[i + 1], at - i);
    }

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p, t) = (v.Sound, v.Palette, v.Time);
        var dt = _last < 0 ? 1 / 60.0 : Math.Clamp(t - _last, 0.001, 0.1);
        _last = t;

        var bands = sound.Bands;
        var n = bands.Length;
        // The song's own beat: a soft swell on each, and the tempo (smoothed, so a misread beat doesn't lurch the flow).
        var pulse = (float)v.Beat.Pulse(t);
        if (v.Beat.IsLocked) _tempo += (Math.Clamp(v.Beat.Bpm, 60, 180) - _tempo) * (1 - Math.Exp(-dt / 2));
        // The parts of the sound: bass (to about 150 Hz), the low mids, the voice (about 250 Hz to 3.5 kHz) and the air above it.
        Span<float> target = stackalloc float[Layers];
        target[0] = Math.Clamp(sound.Bass * 1.1f, 0, 1);
        target[1] = Math.Clamp(VisualPainters.Average(bands, n / 4, n * 3 / 8) * 1.4f, 0, 1);
        target[2] = Math.Clamp(VisualPainters.Average(bands, n * 3 / 8, n * 3 / 4) * 1.8f, 0, 1);
        target[3] = Math.Clamp(VisualPainters.Average(bands, n * 3 / 4, n) * 2.4f, 0, 1);
        var ease = 1 - (float)Math.Exp(-dt / 0.16);
        for (var k = 0; k < Layers; k++) _amplitude[k] += (target[k] - _amplitude[k]) * ease;

        // The flow: what is heard now enters at the left, and moves along as time passes.
        _sinceSample += dt;
        while (_sinceSample >= Interval)
        {
            _sinceSample -= Interval;
            _flowNewest = (_flowNewest + 1) % Memory;
            Array.Copy(_amplitude, _flow[_flowNewest], Layers);
        }
        // The waves travel left to right at a pace set by the tempo (an eighth of a turn a beat at the slowest), a little quicker as the sound swells.
        for (var k = 0; k < Layers; k++)
            _phase[k] -= (float)(dt * MathF.Tau * 0.25 * 0.5 * (_tempo / 60) * Pace[k] * (0.8 + 0.5 * _amplitude[k]));

        var reach = h * 0.30f * (1 + 0.18f * pulse);
        _newest = (_newest + 1) % History;
        var shape = _history[_newest];
        var flowAt = (float)(Crossing / Interval);
        for (var i = 0; i < Points; i++)
        {
            var u = i / (float)(Points - 1);
            var age = u * flowAt;
            var a = (int)age;
            var f = age - a;
            var nearer = _flow[(_flowNewest - a + Memory * 2) % Memory];
            var older = _flow[(_flowNewest - a - 1 + Memory * 2) % Memory];
            float y = 0;
            for (var k = 0; k < Layers; k++)
            {
                var level = nearer[k] * (1 - f) + older[k] * f;
                y += level * Reach[k] * MathF.Sin(MathF.Tau * Cycles[k] * u + _phase[k]);
            }
            // Soft ends while it flows, as the animation has.
            shape[i] = y * reach * VisualPainters.Smooth(0, 0.2f, u) * VisualPainters.Smooth(0, 0.2f, 1 - u);
        }

        var m = Math.Min(w, h);
        var width = m * 0.062f;
        var left = w * 0.10f;
        var span = w - left * 2;
        var mid = h / 2;
        if (!ReferenceEquals(_coloursOf, p))
        {
            _coloursOf = p;
            for (var i = 0; i < 4; i++) _tones[i] = p.Tone(i);
        }
        // The colour pulse: a brighter band runs along the line from the left on each beat, and on each new line of the lyrics.
        var since = t - v.Beat.LastHit;
        if (v.Lyrics is { IsSynced: true } lyrics && lyrics.IndexAt(v.Position) is var atLine and >= 0)
        {
            var sung = (v.Position - lyrics.Lines[atLine].Start!.Value).TotalSeconds;
            if (sung >= 0) since = Math.Min(since, sung);
        }
        var travel = v.Beat.IsLocked ? Math.Clamp(v.Beat.Period * 1.6, 0.6, 1.4) : 0.9;
        var band = (float)(-0.18 + 1.36 * since / travel);
        var strength = (float)Math.Exp(-since / (travel * 0.9));
        _stops.Clear();
        _stops.Add(new CanvasGradientStop { Position = 0, Color = Along(p, 0) });
        if (since >= 0 && strength > 0.03f && band > -0.2f && band < 1.2f)
        {
            var (lo, centre, hi) = (Math.Clamp(band - 0.2f, 0, 1), Math.Clamp(band, 0, 1), Math.Clamp(band + 0.2f, 0, 1));
            var glow = VisualPalette.Mix(p.Light, Microsoft.UI.Colors.White, 0.5);
            foreach (var (at, colour) in new[] { (lo, Along(p, lo)), (centre, VisualPalette.Mix(Along(p, centre), glow, 0.8 * strength)), (hi, Along(p, hi)) })
                if (at > _stops[^1].Position + 1e-3f) _stops.Add(new CanvasGradientStop { Position = at, Color = colour });
        }
        if (_stops[^1].Position < 1 - 1e-3f) _stops.Add(new CanvasGradientStop { Position = 1, Color = Along(p, 1) });
        using var spectrum = new CanvasLinearGradientBrush(ds, _stops.ToArray()) { StartPoint = new Vector2(left, 0), EndPoint = new Vector2(left + span, 0) };

        // The echoes first (the line as it was a few frames ago), adding light to what is behind them.
        ds.Blend = CanvasBlend.Add;
        for (var e = Echoes; e >= 1; e--)
        {
            spectrum.Opacity = EchoAlpha[e - 1];
            using var echo = Line(ds, _history[(_newest - e * EchoGap + History * 2) % History], left, span, mid);
            ds.DrawGeometry(echo, spectrum, width * 0.8f, _round);
        }
        ds.Blend = CanvasBlend.SourceOver;
        spectrum.Opacity = 1;

        using var line = Line(ds, shape, left, span, mid);
        ds.DrawGeometry(line, VisualPalette.With(_tones[2], 0.16), width * 2.1f, _round);
        ds.DrawGeometry(line, spectrum, width, _round);
    }

    private static CanvasGeometry Line(CanvasDrawingSession ds, float[] heights, float left, float span, float mid)
    {
        using var path = new CanvasPathBuilder(ds);
        for (var i = 0; i < Points; i++)
        {
            var point = new Vector2(left + span * i / (Points - 1), mid + heights[i]);
            if (i == 0) path.BeginFigure(point);
            else path.AddLine(point);
        }
        path.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(path);
    }
}

/// <summary>
/// Meloday's sun (the cover art of the playlist): four concentric circles of one colour, the outer ones faint, the
/// core solid. Each ring swells with its own part of the sound (the core with the bass, the next rings with the
/// mids, the outermost with the highs), and all breathe slowly as the cover's do.
/// </summary>
internal sealed class SunPainter : IVisualPainter
{
    // The rings as the cover has them: size (a share of the sun), opacity, and the breathing (seconds, how far in it starts).
    private static readonly (float Size, float Opacity, double Seconds, double Delay)[] Rings =
    [
        (1.00f, 0.30f, 8.8, 1.4),
        (0.75f, 0.40f, 7.6, 3.1),
        (0.50f, 0.65f, 9.5, 5.2),
        (0.25f, 1.00f, 7.9, 2.6),
    ];

    private readonly float[] _energy = new float[4];

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p, t) = (v.Sound, v.Palette, v.Time);
        var bands = sound.Bands;
        var n = bands.Length;
        // Outermost to core: highs, upper mids, lower mids, bass (the higher bands are quieter, so they are lifted).
        _energy[0] = Math.Clamp(VisualPainters.Average(bands, n * 5 / 8, n) * 2.0f, 0, 1);
        _energy[1] = Math.Clamp(VisualPainters.Average(bands, n * 3 / 8, n * 5 / 8) * 1.6f, 0, 1);
        _energy[2] = Math.Clamp(VisualPainters.Average(bands, n / 8, n * 3 / 8) * 1.25f, 0, 1);
        _energy[3] = Math.Clamp(sound.Bass * 1.15f, 0, 1);

        var c = new Vector2(w / 2, h / 2);
        var sun = Math.Min(w, h) * 0.78f;
        for (var i = 0; i < Rings.Length; i++)
        {
            var (size, opacity, seconds, delay) = Rings[i];
            var breathe = 1 + 0.025f * (float)Math.Sin((t + delay) * Math.PI * 2 / seconds);
            var e = _energy[i];
            var radius = sun * size / 2 * breathe * (0.88f + 0.30f * e);
            var alpha = Math.Clamp(opacity * (0.8f + 0.4f * e), 0, 1);
            // Each ring its own colour of the artwork, the darkest outermost and the lightest at the core.
            var colour = i == Rings.Length - 1 ? VisualPalette.Mix(p.Tone(3), Microsoft.UI.Colors.White, 0.18) : p.Tone(i);
            ds.FillCircle(c, radius, VisualPalette.With(colour, alpha));
        }
    }
}

/// <summary>The typefaces of the lyric videos (see <see cref="LyricFonts"/>), each the first form of it Win2D can load: its file, the packaged one, then a system face of the same character.</summary>
internal static class VisualFont
{
    // The form of each file that worked (the file's own address, or the package's), so the other isn't tried again.
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
                // Not loadable here: try the next.
            }
        }
        throw new InvalidOperationException("No typeface for the visualizer.");
    }
}

/// <summary>
/// The lyric video, as artists put them out: the line being sung in big type, each word arriving as it is sung (timed
/// across the line, see <see cref="LyricTiming"/>), the line before it fading away before the next one begins, everything
/// kicking with the beat and the pace of the arrivals following the tempo (see <see cref="BeatTracker"/>). The type is
/// part of the picture, never shadowed above it. Behind it, either the artist's backdrop, the album's cover and the
/// artist's portrait, each in turn drifting and breathing slowly under a wash of the artwork's colour, or (always, when
/// there is nothing to show) a lively gradient of soft colour clouds in the artwork's colours. Without timed lyrics the
/// song's name and artist are set the same way.
/// </summary>
internal sealed class LyricPainter(bool backdrop) : IVisualPainter, IDisposable
{
    /// <summary>One line set in type: each word alone (to animate), where it sits, and when it is sung.</summary>
    private sealed class SetLine : IDisposable
    {
        public required CanvasTextLayout[] Words { get; init; }
        public required Vector2[] Positions { get; init; }
        public required double[] Starts { get; init; }
        /// <summary>How long each word is held (seconds; 0 for a word that is simply sung).</summary>
        public required double[] Holds { get; init; }
        public required double Seconds { get; init; }
        public required float Height { get; init; }

        public void Dispose()
        {
            foreach (var w in Words) w.Dispose();
        }
    }

    // The lines on show (a lyric is one; the title card is the name and, smaller beneath it, the artist) and those lifting away.
    // The weight belongs to the song, not to a picture: the page's lyric video and the full-screen one (two painters, made at
    // different moments) share it, so a song is as heavy wherever it is shown. The face is always Manrope.
    private static readonly object TypeGate = new();
    private static double s_energy = 0.4, s_energyAt = -1;
    private readonly LyricFont _font = LyricFont.Manrope;
    private double _energy = 0.4;
    // How visible the line on show was on the last frame (a line that has already faded isn't brought back to fade again).
    private float _shownAlpha = 1;
    private SetLine?[] _lines = [];
    private SetLine?[] _leaving = [];
    private string _lineKey = string.Empty;
    private double _changedAt;
    private double _lineStartSeconds;
    private float _setFor;
    // The pictures behind the words: each loaded in the background, shown in turn.
    private sealed class Backdrop(BackdropImage image) : IDisposable
    {
        private readonly object _gate = new();
        private bool _dead;
        public BackdropImage Image { get; } = image;
        public volatile CanvasBitmap? Bitmap;

        /// <summary>Takes a loaded picture, unless this one has been let go of meanwhile.</summary>
        public void Give(CanvasBitmap bitmap)
        {
            lock (_gate)
            {
                if (_dead) bitmap.Dispose();
                else Bitmap = bitmap;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _dead = true;
                Bitmap?.Dispose();
                Bitmap = null;
            }
        }
    }

    private const double Hold = 15, Crossfade = 2.5;
    private List<Backdrop> _backdrops = [];
    private IReadOnlyList<BackdropImage>? _wanted;
    private double _cycleAt = -1, _firstAt = -1;

    public void Dispose()
    {
        DisposeAll(_lines);
        DisposeAll(_leaving);
        foreach (var b in _backdrops) b.Dispose();
        _backdrops = [];
        _scroll?.Dispose();
        _fade?.Dispose();
        DisposeClouds();
    }

    private static void DisposeAll(SetLine?[] lines)
    {
        foreach (var line in lines) line?.Dispose();
    }

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        DrawBackground(ds, v, w, h);
        ChooseType(v, t);

        var beat = v.Beat;
        var period = beat.Period;
        var pop = Math.Clamp(period * 0.45, 0.16, 0.32);
        // The line before goes completely (a quick fade, in place) before the next begins to show.
        var away = Math.Clamp(period * 0.5, 0.18, 0.3);
        var position = v.Position.TotalSeconds;

        // Which line is on: the lyrics' (timed), else the song's name and artist (a title card).
        string key;
        string text;
        double lineStart;
        double available;
        IReadOnlyList<TimedWord> timed;
        var lyrics = v.Lyrics;
        // Lyrics with no timing at all roll up the picture over the length of the song, as credits do.
        if (lyrics is { IsSynced: false, IsInstrumental: false, Lines.Count: > 0 } && lyrics.Lines.Any(l => l.Text.Length > 0))
        {
            DrawScroll(ds, v, w, h);
            return;
        }
        var index = lyrics is { IsSynced: true } ? lyrics.IndexAt(v.Position) : -1;
        var sung = lyrics is { IsSynced: true } && index >= 0 && !string.IsNullOrWhiteSpace(lyrics.Lines[index].Text);
        if (sung)
        {
            key = "L" + index + ":" + lyrics!.Lines[index].Text;
            text = lyrics.Lines[index].Text.Trim();
            lineStart = lyrics.Lines[index].Start!.Value.TotalSeconds;
            available = LyricTiming.Available(lyrics, index);
            // Words arrive one by one: at the times the lyrics give when they time each word, else spread across the line as they would be sung.
            var words = lyrics.Lines[index].Words;
            timed = words is null
                ? LyricTiming.Words(text, available)
                : words.Select(x => new TimedWord(x.Text, Math.Max(0, (x.Start - lyrics.Lines[index].Start!.Value).TotalSeconds),
                    x.End is { } end && (end - x.Start).TotalSeconds >= 0.45 ? (end - x.Start).TotalSeconds : 0)).ToList();
        }
        else if (lyrics is { IsSynced: true, IsInstrumental: false } && lyrics.Lines.Any(l => l.Text.Length > 0))
        {
            // A break in the singing (the intro, an instrumental, the end): nothing on screen, not even the song's name.
            if (_lineKey != "B")
            {
                DisposeAll(_leaving);
                // A line that had already faded away isn't brought back to leave again.
                if (_shownAlpha > 0.05f) _leaving = _lines;
                else DisposeAll(_lines);
                _lines = [];
                _lineKey = "B";
                _changedAt = t;
            }
            DrawLeaving(ds, away, t, Math.Min(h * 0.112f, Math.Min(w, h * 1.5f) * 0.086f), p);
            return;
        }
        else
        {
            // No lyrics at all: the song's name (and its artist) is the picture.
            key = "T" + v.Title + "|" + v.Artist;
            text = v.Title;
            lineStart = 0;
            available = 2.2;
            timed = LyricTiming.Words(text, available);
        }
        if (string.IsNullOrWhiteSpace(text)) return;

        var size = Math.Min(h * 0.112f, Math.Min(w, h * 1.5f) * 0.086f);
        if (key != _lineKey || Math.Abs(size - _setFor) > 0.5f)
        {
            var sameLine = key == _lineKey;
            if (sameLine) DisposeAll(_lines);
            else
            {
                DisposeAll(_leaving);
                _leaving = _lines;
                _changedAt = t;
            }
            if (sung)
            {
                _lines = [Set(ds, timed, available, w, h, size, h / 2, 0, _font, LyricFonts.Weight(_font, _energy))];
            }
            else
            {
                // The name, and the artist smaller beneath it a moment later.
                var weight = LyricFonts.Weight(_font, Math.Max(_energy, 0.7));
                var title = Set(ds, timed, available, w, h, size, h * 0.46f, 0, _font, weight);
                var artist = string.IsNullOrWhiteSpace(v.Artist) || title is null
                    ? null
                    : Set(ds, LyricTiming.Words(v.Artist, 1.6), 1.6, w, h, size * 0.46f, h * 0.46f + title.Height / 2 + size * 0.62f, 0.45, _font, LyricFonts.Weight(_font, 0.3));
                _lines = [title, artist];
                if (sameLine) _changedAt = t;
            }
            _lineKey = key;
            _setFor = size;
            _lineStartSeconds = lineStart;
        }
        if (_lines.Length == 0 || _lines[0] is null) return;

        // The line's own clock: seconds since it began being sung (the title card's begins as it appears).
        var into = sung ? position - _lineStartSeconds : t - _changedAt;
        // A sung line left hanging in a long gap fades out.
        var alpha = sung ? 1 - VisualPainters.Smooth(0, 0.7f, (float)(into - (_lines[0]!.Seconds + 1.8))) : 1f;

        var arrive = _leaving.Length > 0 ? VisualPainters.Smooth((float)(away * 0.75), (float)away, (float)(t - _changedAt)) : 1f;
        alpha *= arrive;
        _shownAlpha = alpha;
        DrawLeaving(ds, away, t, size, p);
        foreach (var line in _lines)
            if (line is not null) DrawLine(ds, line, into, pop, alpha, 0, 1, p);
    }

    /// <summary>The line (or lines) lifting away and fading as the next comes, or as a break begins.</summary>
    private void DrawLeaving(CanvasDrawingSession ds, double away, double t, float size, VisualPalette p)
    {
        if (_leaving.Length == 0) return;
        var s = (float)((t - _changedAt) / away);
        if (s >= 1)
        {
            DisposeAll(_leaving);
            _leaving = [];
            return;
        }
        var e = s * s;
        foreach (var gone in _leaving)
            if (gone is not null) DrawLine(ds, gone, double.MaxValue, 0.2, 1 - s, -size * 0.5f * e, 0.98f, p);
    }

    /// <summary>Keeps a running sense of how loud the song is, which sets how heavy each line is set.</summary>
    private void ChooseType(VisualInput v, double t)
    {
        lock (TypeGate)
        {
            var level = LyricFonts.Energy(v.Sound.Level);
            var dt = s_energyAt < 0 ? 0 : Math.Clamp(t - s_energyAt, 0, 0.5);
            s_energyAt = t;
            s_energy += (level - s_energy) * (1 - Math.Exp(-dt / 3.0));
            _energy = s_energy;
        }
    }

    /// <summary>Sets a line of text for a view of the given size: its words laid out, each with its time.</summary>
    private static SetLine? Set(CanvasDrawingSession ds, IReadOnlyList<TimedWord> timed, double available, float w, float h, float size, float centreY, double delay, LyricFont font, int weight)
    {
        if (timed.Count == 0) return null;
        var whole = string.Join(' ', timed.Select(x => x.Text));
        var wrap = w * 0.82f;
        CanvasTextLayout layout;
        var fit = size;
        for (var attempt = 0; ; attempt++)
        {
            layout = VisualFont.Layout(ds, whole, fit, weight, font, wrap, h, CanvasHorizontalAlignment.Center);
            if (layout.LayoutBounds.Height <= h * 0.58 || attempt >= 6) break;
            layout.Dispose();
            fit *= 0.88f;
        }
        using (layout)
        {
            var bounds = layout.LayoutBounds;
            var originX = (w - wrap) / 2;
            var originY = centreY - (float)bounds.Height / 2 - (float)bounds.Y;
            var words = new CanvasTextLayout[timed.Count];
            var positions = new Vector2[timed.Count];
            var starts = new double[timed.Count];
            var holds = new double[timed.Count];
            var at = 0;
            for (var i = 0; i < timed.Count; i++)
            {
                var region = layout.GetCharacterRegions(at, timed[i].Text.Length)[0].LayoutBounds;
                words[i] = VisualFont.Layout(ds, timed[i].Text, fit, weight, font, w, h, CanvasHorizontalAlignment.Left);
                positions[i] = new Vector2(originX + (float)region.X, originY + (float)region.Y);
                starts[i] = timed[i].Start + delay;
                holds[i] = timed[i].Hold;
                at += timed[i].Text.Length + 1;
            }
            // Where words are timed (and some held) the line lasts until the last of them lets go.
            var seconds = Math.Max(LyricTiming.SingingSeconds(timed.Count, available) + delay, starts[^1] + holds[^1]);
            return new SetLine { Words = words, Positions = positions, Starts = starts, Holds = holds, Seconds = seconds, Height = (float)bounds.Height };
        }
    }

    // Lyrics with no timing: all of them as one block of type, rolled up over the song, faded at the top and the foot.
    private CanvasTextLayout? _scroll;
    private string _scrollKey = string.Empty;
    private CanvasLinearGradientBrush? _fade;
    private float _fadeFor;

    private void DrawScroll(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var lyrics = v.Lyrics!;
        var key = v.Title + "|" + lyrics.Lines.Count + "|" + (int)w + "x" + (int)h + "|" + _font;
        if (key != _scrollKey)
        {
            _scroll?.Dispose();
            var size = Math.Clamp(Math.Min(w * 0.045f, h * 0.06f), 16f, 60f);
            _scroll = VisualFont.Layout(ds, string.Join('\n', lyrics.Lines.Select(l => l.Text)), size, LyricFonts.Weight(_font, 0.35), _font, w * 0.7f, 60000f, CanvasHorizontalAlignment.Center);
            _scrollKey = key;
        }
        if (_fade is null || Math.Abs(_fadeFor - h) > 0.5f)
        {
            _fade?.Dispose();
            _fadeFor = h;
            _fade = new CanvasLinearGradientBrush(ds, new[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.2f, Color = Color.FromArgb(255, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.8f, Color = Color.FromArgb(255, 255, 255, 255) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, 255, 255, 255) },
            }) { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, h) };
        }
        var bounds = _scroll!.LayoutBounds;
        var travel = (float)bounds.Height + h * 0.9f;
        var seconds = v.Duration.TotalSeconds > 5 ? v.Duration.TotalSeconds : 210;
        // It starts after a short while (the intro) and is done a little before the song is.
        var progress = (float)Math.Clamp((v.Position.TotalSeconds - seconds * 0.04) / (seconds * 0.9), 0, 1);
        var top = h * 0.95f - progress * travel - (float)bounds.Y;
        using (ds.CreateLayer(_fade))
            ds.DrawTextLayout(_scroll, w * 0.15f, top, VisualPalette.With(Microsoft.UI.Colors.White, 0.92));
    }

    /// <summary>Draws a set line at <paramref name="into"/> seconds in: each word rising and growing in at its time, then settling to white.</summary>
    private static void DrawLine(CanvasDrawingSession ds, SetLine line, double into, double pop, float alpha, float lift, float scale, VisualPalette p)
    {
        if (alpha <= 0.002f) return;
        var saved = ds.Transform;
        for (var i = 0; i < line.Words.Length; i++)
        {
            var held = line.Holds[i];
            var progress = (float)Math.Clamp((into - line.Starts[i]) / Math.Max(0.05, pop), 0, 1);
            // A word held on a long note stays in the artwork's colour until it lets go, then cools to white.
            var settle = (float)Math.Clamp((into - line.Starts[i] - held) / 0.7, 0, 1);
            // Unsung words wait, faint; a sung word springs in (a little past its size, then back) and cools from the artwork's colour to white.
            var eased = 1 - MathF.Pow(1 - progress, 3);
            var spring = 1 + 0.12f * MathF.Sin(progress * MathF.PI) * (1 - progress);
            var wordScale = scale * (0.84f + 0.16f * eased) * spring;
            // ...and swells a little while it is held.
            if (held > 0)
            {
                var swell = (float)(Math.Clamp((into - line.Starts[i]) / 0.3, 0, 1) * (1 - Math.Clamp((into - line.Starts[i] - held) / 0.35, 0, 1)));
                wordScale *= 1 + 0.06f * swell;
            }
            var rise = (1 - eased) * line.Height * 0.12f + lift;
            // Words not yet sung aren't there at all (nothing of the next line shows before it is sung).
            var opacity = eased * alpha;
            var colour = VisualPalette.Mix(p.Light, Microsoft.UI.Colors.White, VisualPainters.Smooth(0, 1, settle));
            var bounds = line.Words[i].LayoutBounds;
            var centre = line.Positions[i] + new Vector2((float)bounds.Width / 2, (float)bounds.Height / 2);
            ds.Transform = Matrix3x2.CreateScale(wordScale, centre) * Matrix3x2.CreateTranslation(0, rise) * saved;
            ds.DrawTextLayout(line.Words[i], line.Positions[i], VisualPalette.With(colour, opacity));
        }
        ds.Transform = saved;
    }

    // ------------------------------------------------------------------ behind the words

    private void DrawBackground(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        DrawClouds(ds, v, w, h);
        if (!backdrop) return;
        if (!ReferenceEquals(v.Backdrops, _wanted)) Want(ds.Device, v.Backdrops);
        var ready = _backdrops.Where(b => b.Bitmap is not null).ToList();
        if (ready.Count == 0) return;
        if (_firstAt < 0)
        {
            _firstAt = t;
            _cycleAt = t;
        }
        var fade = VisualPainters.Smooth(0, 1.4f, (float)(t - _firstAt));
        // Each picture in turn: held a while, then the next fades in over it.
        var elapsed = t - _cycleAt;
        var slot = (int)(elapsed / Hold);
        var at = slot % ready.Count;
        var into = elapsed - slot * Hold;
        DrawPicture(ds, ready[at], t, w, h, fade, at);
        if (ready.Count > 1 && into > Hold - Crossfade)
        {
            var next = (at + 1) % ready.Count;
            DrawPicture(ds, ready[next], t, w, h, fade * VisualPainters.Smooth(0, 1, (float)((into - (Hold - Crossfade)) / Crossfade)), next);
        }
        // A wash of the artwork's colour and a dark veil keep the type legible.
        ds.FillRectangle(0, 0, w, h, VisualPalette.With(p.Back, 0.60 * fade));
        ds.FillRectangle(0, 0, w, h, VisualPalette.With(p.Main, 0.10 * fade));
    }

    /// <summary>The pictures wanted behind the words have changed (a new song): let go of the old ones and load these.</summary>
    private void Want(CanvasDevice device, IReadOnlyList<BackdropImage> images)
    {
        _wanted = images;
        var old = _backdrops;
        var mine = _backdrops = [];
        foreach (var b in old) b.Dispose();
        _cycleAt = _firstAt = -1;
        foreach (var image in images)
        {
            var slot = new Backdrop(image);
            mine.Add(slot);
            _ = Task.Run(async () =>
            {
                try
                {
                    var loaded = await CanvasBitmap.LoadAsync(device, image.Path);
                    if (image.Soft)
                    {
                        try
                        {
                            slot.Give(Haze(device, loaded));
                        }
                        finally
                        {
                            loaded.Dispose();
                        }
                    }
                    else slot.Give(loaded);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException)
                {
                    // No picture then: the others (or the clouds) stand in.
                }
            });
        }
    }

    /// <summary>A square cover or portrait as a soft haze (small, blurred once), fit to fill a screen without showing its pixels.</summary>
    private static CanvasBitmap Haze(CanvasDevice device, CanvasBitmap source)
    {
        var size = source.Size;
        var scale = Math.Min(1.0, 480 / Math.Max(size.Width, size.Height));
        var (tw, th) = ((float)(size.Width * scale), (float)(size.Height * scale));
        using var target = new CanvasRenderTarget(device, tw, th, 96);
        using (var ds = target.CreateDrawingSession())
        {
            using var blur = new GaussianBlurEffect { Source = source, BlurAmount = (float)(6 * scale), BorderMode = EffectBorderMode.Hard };
            ds.DrawImage(blur, new Windows.Foundation.Rect(0, 0, tw, th), new Windows.Foundation.Rect(0, 0, size.Width, size.Height));
        }
        return CanvasBitmap.CreateFromBytes(device, target.GetPixelBytes(), (int)target.SizeInPixels.Width, (int)target.SizeInPixels.Height, target.Format);
    }

    /// <summary>One picture, filling the view, drifting and breathing slowly (a zoom in and out, a wander): never still, never hurried.</summary>
    private static void DrawPicture(CanvasDrawingSession ds, Backdrop picture, double t, float w, float h, float opacity, int seed)
    {
        if (picture.Bitmap is not { } image || opacity <= 0.002f) return;
        var size = image.Size;
        var cover = Math.Max(w / size.Width, h / size.Height);
        var shift = seed * 17.0;
        var zoom = (float)(cover * (1.16 + 0.07 * Math.Sin((t + shift) * Math.PI * 2 / 52)));
        var dw = (float)size.Width * zoom;
        var dh = (float)size.Height * zoom;
        var cx = w / 2 + w * 0.035f * (float)Math.Sin((t + shift) * Math.PI * 2 / 37);
        var cy = h / 2 + h * 0.03f * (float)Math.Cos((t + shift) * Math.PI * 2 / 43);
        ds.DrawImage(image, new Windows.Foundation.Rect(cx - dw / 2, cy - dh / 2, dw, dh), new Windows.Foundation.Rect(0, 0, size.Width, size.Height), opacity, CanvasImageInterpolation.Linear);
    }

    // The clouds' brushes are made once for a palette (not each frame); each frame only moves them.
    private CanvasRadialGradientBrush[]? _clouds;
    private VisualPalette? _cloudsOf;

    private void DisposeClouds()
    {
        if (_clouds is null) return;
        foreach (var b in _clouds) b.Dispose();
        _clouds = null;
    }

    /// <summary>Soft clouds of the artwork's colours drifting over a dark shade of it, swelling a little with the bass.</summary>
    private void DrawClouds(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        ds.FillRectangle(0, 0, w, h, p.Back);
        var m = Math.Max(w, h);
        var bass = v.Sound.Bass;
        // The ground is one of the artwork's four colours; the other three are the clouds drifting over it.
        (Color Colour, float Fx, float Fy, float Px, float Py, float Radius, float Alpha)[] clouds =
        [
            (p.Tone(1), 0.13f, 0.17f, 0.0f, 1.0f, 0.66f, 0.62f),
            (p.Tone(2), 0.19f, 0.11f, 2.1f, 3.2f, 0.60f, 0.55f),
            (p.Tone(3), 0.11f, 0.21f, 4.0f, 0.4f, 0.58f, 0.50f),
        ];
        if (_clouds is null || !ReferenceEquals(_cloudsOf, p))
        {
            DisposeClouds();
            _cloudsOf = p;
            _clouds = clouds.Select(c => new CanvasRadialGradientBrush(ds, VisualPalette.With(c.Colour, c.Alpha), VisualPalette.With(c.Colour, 0))).ToArray();
        }
        for (var i = 0; i < clouds.Length; i++)
        {
            var (_, fx, fy, px, py, radius, _) = clouds[i];
            var c = new Vector2(w / 2 + w * 0.34f * MathF.Sin((float)t * fx * MathF.Tau + px), h / 2 + h * 0.30f * MathF.Sin((float)t * fy * MathF.Tau + py));
            var r = m * radius * (1 + 0.10f * bass);
            var brush = _clouds[i];
            (brush.Center, brush.RadiusX, brush.RadiusY) = (c, r, r);
            // Only the part of the picture the cloud reaches is painted.
            var (x0, y0) = (Math.Max(0, c.X - r), Math.Max(0, c.Y - r));
            var (x1, y1) = (Math.Min(w, c.X + r), Math.Min(h, c.Y + r));
            if (x1 > x0 && y1 > y0) ds.FillRectangle(x0, y0, x1 - x0, y1 - y0, brush);
        }
    }
}
