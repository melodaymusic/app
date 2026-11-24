using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>
/// Ribbons: bands of colour stacked inside one another along the middle of the view, mirrored above and below, that swell
/// into soft lobes where the music is loudest: the low end on the left, the high end on the right. The innermost band
/// follows the sound as it is now; each band outside it holds on to what was just heard a little longer, so a loud moment
/// leaves rings of colour that ease back in after it. The outer bands take the artwork's colours, the inner one the
/// colours opposite them, so it always stands out.
/// </summary>
internal sealed class RibbonsPainter : IVisualPainter
{
    private const int Lobes = 5, Layers = 4, Points = 140;
    private static readonly float[] Hold = [1.7f, 1.1f, 0.6f, 0.12f];
    private static readonly float[] Size = [1f, 0.94f, 0.88f, 0.84f];
    private static readonly float[] Thin = [0.0022f, 0.0026f, 0.003f, 0.0045f];

    private readonly BandFollower _bands = new(Lobes);
    private readonly float[][] _held = Enumerable.Range(0, Layers).Select(_ => new float[Lobes]).ToArray();
    private readonly float[] _reach = new float[Points];
    private readonly Vector2[] _top = new Vector2[Points], _bottom = new Vector2[Points];
    private VisualPalette? _coloursOf;
    private readonly Color[] _inks = new Color[Layers + 1];
    private double _last = -1;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var t = (float)v.Time;
        var dt = _last < 0 ? 1 / 60.0 : Math.Clamp(v.Time - _last, 0.001, 0.1);
        _last = v.Time;
        Listen(v, dt);
        Colours(v.Palette);

        ds.FillRectangle(0, 0, w, h, Color.FromArgb(255, 22, 22, 24));
        using (var lift = new CanvasRadialGradientBrush(ds, Color.FromArgb(255, 38, 38, 42), Color.FromArgb(0, 22, 22, 24)) { Center = new(w / 2, h / 2), RadiusX = w * 0.6f, RadiusY = h * 0.6f })
            ds.FillRectangle(0, 0, w, h, lift);

        var mid = h / 2;
        var tallest = Math.Min(h * 0.42f, w * 0.3f) * (1 + 0.06f * (float)v.Beat.Pulse(v.Time));
        for (var l = 0; l < Layers; l++)
        {
            Shape(l, w, h, mid, tallest, t);
            using var shape = Ribbon(ds);
            if (l > 0)
            {
                // A soft dark edge where each band sits inside the one around it.
                using var rim = Ribbon(ds, 1.03f, h * 0.002f, mid);
                ds.FillGeometry(rim, Color.FromArgb(38, 0, 0, 0));
            }
            if (l == Layers - 1)
            {
                using var across = new CanvasLinearGradientBrush(ds, _inks[Layers - 1], _inks[Layers]) { StartPoint = new(0, mid), EndPoint = new(w, mid) };
                ds.FillGeometry(shape, across);
            }
            else
            {
                ds.FillGeometry(shape, _inks[l]);
            }
            // Each band is a touch lighter along its middle, which rounds it.
            using var sheen = new CanvasLinearGradientBrush(ds,
            [
                new CanvasGradientStop { Position = 0, Color = Color.FromArgb(0, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.5f, Color = Color.FromArgb(34, 255, 255, 255) },
                new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
            ]) { StartPoint = new(0, mid - tallest), EndPoint = new(0, mid + tallest) };
            ds.FillGeometry(shape, sheen);
        }
    }

    private void Listen(VisualInput v, double dt)
    {
        var sound = v.Sound;
        _bands.Update(sound.Bands, Lobes, dt);
        for (var k = 0; k < Lobes; k++)
        {
            var steady = VisualPainters.Average(sound.Bands, k * sound.Bands.Length / Lobes, (k + 1) * sound.Bands.Length / Lobes);
            var now = MathF.Pow(Math.Clamp(0.35f * steady + 0.8f * _bands[k], 0, 1), 1.8f);
            for (var l = 0; l < Layers; l++)
            {
                var held = _held[l][k];
                _held[l][k] = now > held
                    ? held + (now - held) * (1 - MathF.Exp(-(float)dt / 0.05f))
                    : held + (now - held) * (1 - MathF.Exp(-(float)dt / Hold[l]));
            }
        }
    }

    /// <summary>How far band <paramref name="l"/> reaches from the middle at each point across.</summary>
    private void Shape(int l, float w, float h, float mid, float tallest, float t)
    {
        var lean = (l - 1.5f) * 0.012f;
        for (var i = 0; i < Points; i++)
        {
            var u = i / (Points - 1f);
            var reach = 0f;
            for (var k = 0; k < Lobes; k++)
            {
                var centre = 0.1f + 0.8f * k / (Lobes - 1) + lean + 0.025f * (VisualNoise.At(t * 0.15f, k * 3.1f, 5) - 0.5f) * 2;
                var width = 0.058f + 0.016f * VisualNoise.Hash(k, l + 3);
                var d = (u - centre) / width;
                var idle = 0.04f + 0.03f * MathF.Sin(t * 0.6f + k * 1.7f);
                reach += MathF.Max(idle, _held[l][k]) * MathF.Exp(-d * d * 0.5f);
            }
            _reach[i] = h * Thin[l] + Math.Min(1.15f, reach) * tallest * Size[l];
            var x = u * w;
            _top[i] = new Vector2(x, mid - _reach[i]);
            _bottom[i] = new Vector2(x, mid + _reach[i]);
        }
    }

    /// <summary>The band's outline as a smooth closed shape, optionally grown by a factor and a margin.</summary>
    private CanvasGeometry Ribbon(CanvasDrawingSession ds, float grow = 1, float margin = 0, float mid = 0)
    {
        Vector2 Out(Vector2 p, int side) => grow == 1 && margin == 0 ? p : new(p.X, mid + (p.Y - mid) * grow + side * margin);
        using var path = new CanvasPathBuilder(ds);
        path.BeginFigure(Out(_top[0], -1));
        Curve(path, _top, -1, Out);
        path.AddLine(Out(_bottom[Points - 1], 1));
        var back = new Vector2[Points];
        for (var i = 0; i < Points; i++) back[i] = _bottom[Points - 1 - i];
        Curve(path, back, 1, Out);
        path.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(path);
    }

    private static void Curve(CanvasPathBuilder path, Vector2[] points, int side, Func<Vector2, int, Vector2> at)
    {
        for (var i = 1; i < points.Length - 1; i++)
        {
            var a = at(points[i], side);
            var b = at(points[i + 1], side);
            path.AddQuadraticBezier(a, (a + b) / 2);
        }
        path.AddLine(at(points[^1], side));
    }

    private void Colours(VisualPalette p)
    {
        if (ReferenceEquals(_coloursOf, p)) return;
        _coloursOf = p;
        for (var l = 0; l < Layers - 1; l++) _inks[l] = Vivid(p.Tone(l), 0.62f, 0.86f);
        var opposite = new VisualPalette(p.Main, p.Light);
        _inks[Layers - 1] = Vivid(opposite.Neighbour(160), 0.7f, 0.92f);
        _inks[Layers] = Vivid(opposite.Neighbour(205), 0.75f, 0.95f);
    }

    /// <summary>The colour made at least this saturated and this bright, keeping its hue.</summary>
    private static Color Vivid(Color c, float saturation, float value)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var d = max - min;
        var hue = d < 1e-5f ? 0 : max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        hue = (hue * 60 + 360) % 360;
        var s = MathF.Max(saturation, max <= 0 ? 0 : d / max);
        var v = MathF.Max(value, max);
        var cc = v * s;
        var x = cc * (1 - MathF.Abs(hue / 60 % 2 - 1));
        var m = v - cc;
        var (rr, gg, bb) = (int)(hue / 60) switch { 0 => (cc, x, 0f), 1 => (x, cc, 0f), 2 => (0f, cc, x), 3 => (0f, x, cc), 4 => (x, 0f, cc), _ => (cc, 0f, x) };
        return Color.FromArgb(255, (byte)((rr + m) * 255), (byte)((gg + m) * 255), (byte)((bb + m) * 255));
    }
}
