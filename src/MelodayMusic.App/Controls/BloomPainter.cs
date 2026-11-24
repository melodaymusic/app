using System.Numerics;
using MelodayMusic.Core.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>
/// Bloom: a disc of fine strands on a soft pastel ground, with faint streaks running out across the whole view. Each
/// strand reaches out with its part of the sound (low at the top, high at the bottom, the same on both sides), so the
/// disc's edge bristles with the music; a dark ring round the middle shivers with the bass, and the time so far sits in
/// a white centre with the song and artist below. The disc takes its first colour from the artwork and moves round the
/// colour wheel each bar, the ground always in a colour opposite it, easing from one to the next.
/// </summary>
internal sealed class BloomPainter : IVisualPainter, IDisposable
{
    private const int Strands = 640, Streaks = 170;
    private static readonly Color Ring = Color.FromArgb(255, 28, 42, 58), Centre = Color.FromArgb(255, 245, 247, 248), Words = Color.FromArgb(255, 38, 50, 62);

    private readonly float[] _length = new float[Strands];
    private double _last = -1, _lastHit, _nextTurn;
    private int _beats, _turn;
    private float _baseHue = -1, _bass;
    private Vector3 _disc, _ground, _discTarget, _groundTarget;
    private CanvasTextLayout? _time, _title, _artist;
    private string _timeText = string.Empty, _titleText = "\0", _artistText = "\0";
    private float _timeSize, _wordsSize;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var t = v.Time;
        var dt = (float)(_last < 0 ? 1 / 60.0 : Math.Clamp(t - _last, 0.001, 0.1));
        _last = t;
        Turn(v);
        _disc = Vector3.Lerp(_disc, _discTarget, 1 - MathF.Exp(-dt / 0.35f));
        _ground = Vector3.Lerp(_ground, _groundTarget, 1 - MathF.Exp(-dt / 0.35f));
        var disc = Rgb(_disc);
        var ground = Rgb(_ground);
        var sound = v.Sound;
        _bass += (sound.Bass - _bass) * (1 - MathF.Exp(-dt / (sound.Bass > _bass ? 0.04f : 0.2f)));
        var pulse = (float)v.Beat.Pulse(t);

        var m = Math.Min(w, h);
        var words = !string.IsNullOrEmpty(v.Title);
        var c = new Vector2(w / 2 + m * 0.01f * MathF.Sin((float)t * 0.2f), h * (words ? 0.43f : 0.5f));
        var radius = m * 0.23f * (1 + 0.05f * _bass + 0.03f * pulse);
        var inner = radius * 0.42f;
        var line = Math.Max(0.8f, m / 800f);

        // The ground, lighter round the disc, with long streaks running out from it.
        ds.FillRectangle(0, 0, w, h, ground);
        using (var light = new CanvasRadialGradientBrush(ds, Mix(ground, Colors(255, 255, 255), 0.55f), Color.FromArgb(0, ground.R, ground.G, ground.B)) { Center = c, RadiusX = radius * 2.6f, RadiusY = radius * 2.6f })
            ds.FillRectangle(0, 0, w, h, light);
        var far = MathF.Sqrt(w * w + h * h);
        using (var streaks = new CanvasPathBuilder(ds))
        {
            for (var i = 0; i < Streaks; i++)
            {
                var a = VisualNoise.Hash(i, 1) * MathF.Tau + (float)t * 0.012f;
                var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                streaks.BeginFigure(c + dir * radius * (1.15f + 0.6f * VisualNoise.Hash(i, 2)));
                streaks.AddLine(c + dir * far * (0.5f + 0.5f * VisualNoise.Hash(i, 3)));
                streaks.EndFigure(CanvasFigureLoop.Open);
            }
            using var g = CanvasGeometry.CreatePath(streaks);
            ds.DrawGeometry(g, Color.FromArgb(34, (byte)(ground.R * 0.55f), (byte)(ground.G * 0.55f), (byte)(ground.B * 0.6f)), line);
        }

        // The strands: each reaches out with its part of the sound, its tip shivering a little.
        var bands = sound.Bands;
        var tips = new Vector2[Strands];
        for (var i = 0; i < Strands; i++)
        {
            var a = i * MathF.Tau / Strands - MathF.PI / 2;
            var u = MathF.Abs(((i * 2f / Strands) + 1) % 2 - 1);
            var at = u * (bands.Length - 1);
            var level = (VisualPainters.Band(bands, (int)at, bands.Length) * 2 + VisualPainters.Band(bands, Math.Max(0, (int)at - 2), bands.Length) + VisualPainters.Band(bands, Math.Min(bands.Length - 1, (int)at + 2), bands.Length)) / 4;
            var shiver = (VisualNoise.At(i * 0.35f, (float)t * 5, 2) - 0.5f) * 0.08f + (VisualNoise.Hash(i, 9) - 0.5f) * 0.07f;
            var target = 0.84f + 0.16f * level + shiver;
            _length[i] += (target - _length[i]) * (1 - MathF.Exp(-dt / (target > _length[i] ? 0.04f : 0.2f)));
            tips[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * radius * _length[i];
        }
        using (var body = CanvasGeometry.CreatePolygon(ds, [.. tips.Select(p => c + (p - c) * 0.96f)]))
            ds.FillGeometry(body, disc);
        using (var light = new CanvasPathBuilder(ds))
        using (var dark = new CanvasPathBuilder(ds))
        {
            for (var i = 0; i < Strands; i++)
            {
                var dir = Vector2.Normalize(tips[i] - c);
                var path = i % 3 == 0 ? dark : light;
                var whisker = i % 7 == 0 ? 1.05f : 1;
                path.BeginFigure(c + dir * inner * (1.02f + 0.3f * VisualNoise.Hash(i, 4)));
                path.AddLine(c + (tips[i] - c) * whisker);
                path.EndFigure(CanvasFigureLoop.Open);
            }
            using var lit = CanvasGeometry.CreatePath(light);
            using var shade = CanvasGeometry.CreatePath(dark);
            ds.DrawGeometry(lit, Color.FromArgb(80, (byte)Math.Min(255, disc.R + 60), (byte)Math.Min(255, disc.G + 60), (byte)Math.Min(255, disc.B + 60)), line);
            ds.DrawGeometry(shade, Color.FromArgb(70, (byte)(disc.R * 0.62f), (byte)(disc.G * 0.62f), (byte)(disc.B * 0.62f)), line);
        }

        // The dark ring, its edge rough and shaking with the bass, and the white centre with the time.
        var ring = new Vector2[96];
        for (var k = 0; k < ring.Length; k++)
        {
            var a = k * MathF.Tau / ring.Length;
            var rough = (VisualNoise.At(k * 0.8f, (float)t * 4, 4) - 0.5f) * (0.12f + 0.25f * _bass) + (VisualNoise.Hash(k, 6) - 0.5f) * 0.05f;
            ring[k] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * inner * (1 + rough);
        }
        using (var shape = CanvasGeometry.CreatePolygon(ds, ring)) ds.FillGeometry(shape, Ring);
        var centre = inner * 0.68f;
        ds.FillCircle(c, centre, Centre);

        var seconds = (int)Math.Max(0, v.Position.TotalSeconds);
        var time = $"{seconds / 60:00}:{seconds % 60:00}";
        if (time != _timeText || MathF.Abs(_timeSize - centre * 0.5f) > 0.5f)
        {
            _time?.Dispose();
            (_timeText, _timeSize) = (time, centre * 0.5f);
            _time = Text(ds, time, _timeSize, 500, centre * 2, 0);
        }
        Centred(ds, _time!, c);

        if (!words) return;
        var size = Math.Max(11, m * 0.032f);
        if (v.Title != _titleText || v.Artist != _artistText || MathF.Abs(size - _wordsSize) > 0.5f)
        {
            _title?.Dispose();
            _artist?.Dispose();
            (_titleText, _artistText, _wordsSize) = (v.Title, v.Artist, size);
            _title = Text(ds, v.Title.ToUpperInvariant(), size, 500, w * 0.8f, size * 0.12f);
            _artist = Text(ds, v.Artist.ToUpperInvariant(), size, 400, w * 0.8f, size * 0.12f);
        }
        var below = c.Y + radius * 1.2f + size * 0.6f;
        Centred(ds, _title!, new(w / 2, below), Words, 0.9f);
        Centred(ds, _artist!, new(w / 2, below + size * 1.35f), Words, 0.75f);
    }

    /// <summary>Moves the colours round the wheel: every four beats when the beat is steady, every five seconds otherwise.</summary>
    private void Turn(VisualInput v)
    {
        var p = v.Palette;
        if (_baseHue < 0)
        {
            _baseHue = Hue(p.Main);
            _nextTurn = v.Time + 5;
            Aim(snap: true);
        }
        var hit = v.Beat.LastHit;
        if (v.Beat.IsLocked)
        {
            if (hit != _lastHit)
            {
                _lastHit = hit;
                if (++_beats % 4 == 0) Step();
            }
            _nextTurn = v.Time + 5;
        }
        else if (v.Time >= _nextTurn)
        {
            _nextTurn = v.Time + 5;
            Step();
        }

        void Step()
        {
            _turn++;
            Aim(snap: false);
        }
    }

    private void Aim(bool snap)
    {
        var hue = (_baseHue + _turn * 62) % 360;
        _discTarget = Hsv(hue, 0.6f, 0.95f);
        _groundTarget = Hsv((hue + 165) % 360, 0.3f, 0.8f);
        if (snap) (_disc, _ground) = (_discTarget, _groundTarget);
    }

    private static CanvasTextLayout Text(CanvasDrawingSession ds, string text, float size, int weight, float width, float spacing)
    {
        var layout = VisualFont.Layout(ds, text, size, weight, LyricFont.DMSans, width, size * 2, CanvasHorizontalAlignment.Center);
        layout.WordWrapping = CanvasWordWrapping.NoWrap;
        layout.TrimmingGranularity = CanvasTextTrimmingGranularity.Character;
        layout.TrimmingSign = CanvasTrimmingSign.Ellipsis;
        if (spacing > 0) layout.SetCharacterSpacing(0, text.Length, 0, spacing, 0);
        return layout;
    }

    private static void Centred(CanvasDrawingSession ds, CanvasTextLayout layout, Vector2 at, Color? colour = null, float alpha = 1)
    {
        var box = layout.LayoutBounds;
        var ink = colour ?? Words;
        ds.DrawTextLayout(layout, at.X - (float)layout.RequestedSize.Width / 2, at.Y - (float)(box.Y + box.Height / 2), Color.FromArgb((byte)(255 * alpha), ink.R, ink.G, ink.B));
    }

    private static float Hue(Color c)
    {
        float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var d = max - MathF.Min(r, MathF.Min(g, b));
        if (d < 1e-5f) return 200;
        var hue = max == r ? (g - b) / d % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (hue * 60 + 360) % 360;
    }

    private static Vector3 Hsv(float hue, float s, float v)
    {
        var c = v * s;
        var x = c * (1 - MathF.Abs(hue / 60 % 2 - 1));
        var m = v - c;
        var (r, g, b) = (int)(hue / 60) switch { 0 => (c, x, 0f), 1 => (x, c, 0f), 2 => (0f, c, x), 3 => (0f, x, c), 4 => (x, 0f, c), _ => (c, 0f, x) };
        return new(r + m, g + m, b + m);
    }

    private static Color Rgb(Vector3 c) => Color.FromArgb(255, (byte)(Math.Clamp(c.X, 0, 1) * 255), (byte)(Math.Clamp(c.Y, 0, 1) * 255), (byte)(Math.Clamp(c.Z, 0, 1) * 255));

    private static Color Colors(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);

    private static Color Mix(Color a, Color b, float t) => VisualPalette.Mix(a, b, t);

    public void Dispose()
    {
        _time?.Dispose();
        _title?.Dispose();
        _artist?.Dispose();
        (_time, _title, _artist) = (null, null, null);
    }
}
