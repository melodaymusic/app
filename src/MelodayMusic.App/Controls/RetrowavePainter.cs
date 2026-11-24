using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>
/// Retrowave: a flight down a valley of glowing grid towards a setting sun, between low-poly mountains drawn as black
/// faces with gold edges. The land is a height map in rows across the view. Each new row is made at the horizon from what
/// the music is doing at that moment, with the bass raising the slopes nearest the valley and the treble the far ridges,
/// so the mountains passing by are the last few seconds of the song. The flight keeps pace with the tempo and the grid and
/// sun brighten on the beat.
/// </summary>
/// <remarks>
/// Rows are drawn from the horizon forwards, and each side of a row from the outside in, split where the ridge turns, so
/// nearer faces always cover what lies behind them without sorting every face.
/// </remarks>
internal sealed class RetrowavePainter : IVisualPainter
{
    private const int Valley = 8, Ridge = 18, Across = Valley + Ridge, Columns = Across * 2 + 1;
    private const int Rows = 40, Keep = 56;
    private const float Eye = 2.6f, Nearest = 2.2f, Far = Rows - 2;

    private static readonly float[] Spread = MakeSpread();
    private static readonly Color Night = Color.FromArgb(255, 5, 2, 8), Haze = Color.FromArgb(255, 74, 34, 14), Gold = Color.FromArgb(255, 238, 196, 72);
    private static readonly Color HazeGlow = Color.FromArgb(255, 255, 176, 64);

    private readonly float[][] _heights = Enumerable.Range(0, Keep).Select(_ => new float[Columns]).ToArray();
    private readonly long[] _rowOf = Enumerable.Repeat(long.MinValue, Keep).ToArray();
    private readonly BandFollower _bands = new(Ridge);
    private readonly Vector2[][] _screen = Enumerable.Range(0, Rows + 1).Select(_ => new Vector2[Columns]).ToArray();
    private readonly List<(int From, int To, bool Rising)> _runs = [];
    private double _travel, _speed = 2.6, _last = -1;
    private float _level, _bass, _camera, _w, _h, _horizon, _focal;

    /// <summary>How far out each column lies from the middle of the valley: one unit apart across the valley, wider and wider beyond it.</summary>
    private static float[] MakeSpread()
    {
        var spread = new float[Across + 1];
        for (var k = 0; k <= Across; k++)
        {
            var j = Math.Max(0, k - Valley);
            spread[k] = Math.Min(k, Valley) + 1.6f * j + 0.2f * j * j;
        }
        return spread;
    }

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var t = v.Time;
        var dt = _last < 0 ? 1 / 60.0 : Math.Clamp(t - _last, 0.001, 0.1);
        _last = t;
        Listen(v, dt);
        (_w, _h) = (w, h);
        _horizon = h * 0.5f;
        _focal = (h - _horizon) * Nearest / Eye;
        _camera = 0.55f * MathF.Sin((float)t * 0.11f) + 0.25f * MathF.Sin((float)t * 0.27f + 1.3f);
        var pulse = (float)v.Beat.Pulse(t);

        var first = (long)Math.Floor(_travel + Nearest - 1.2);
        for (var r = first; r <= first + Rows; r++) Grow(r);

        Sky(ds, (float)t, pulse);
        Land(ds, first, pulse);
    }

    private void Listen(VisualInput v, double dt)
    {
        var sound = v.Sound;
        _bands.Update(sound.Bands, Ridge, dt);
        _level += (Math.Clamp(sound.Level * 3f, 0, 1) - _level) * (1 - MathF.Exp(-(float)dt / 0.6f));
        _bass += (sound.Bass - _bass) * (1 - MathF.Exp(-(float)dt / (sound.Bass > _bass ? 0.05f : 0.3f)));
        var beat = v.Beat.IsLocked ? Math.Clamp(v.Beat.Bpm, 70, 170) : 110;
        var target = beat / 60 * 1.4 + 1.2 * _level;
        _speed += (target - _speed) * (1 - Math.Exp(-dt / 1.5));
        _travel += _speed * dt;
    }

    /// <summary>Makes row <paramref name="r"/> the first time it comes into view, from the music as it is now.</summary>
    private void Grow(long r)
    {
        var slot = (int)(((r % Keep) + Keep) % Keep);
        if (_rowOf[slot] == r) return;
        _rowOf[slot] = r;
        var row = _heights[slot];
        foreach (var side in new[] { -1, 1 })
        {
            var before = 0f;
            for (var k = 0; k <= Across; k++)
            {
                var c = Across + side * k;
                if (k <= Valley)
                {
                    row[c] = 0;
                    continue;
                }
                var rise = VisualPainters.Smooth(0, 1, (k - Valley) / 4f);
                var broad = 1 + 0.03f * (Spread[k] - Valley);
                var shape = MathF.Pow(VisualNoise.Layered(r * 0.17f + side * 41, Spread[k] * 0.06f, 3), 1.8f) * 1.9f;
                var music = 0.35f + 1.2f * _bands[Math.Min(Ridge - 1, k - Valley - 1)] + 0.3f * _level;
                var height = 4.2f * rise * broad * shape * music;
                // The first slope out of the valley never dips, so the valley floor stays a clean grid.
                row[c] = k <= Valley + 2 ? Math.Max(height, before * 0.9f) : height;
                before = row[c];
            }
        }
    }

    private float Height(long r, int c) => _heights[(int)(((r % Keep) + Keep) % Keep)][c];

    private Vector2 Project(float x, float y, float z)
    {
        z = Math.Max(0.35f, z);
        return new(_w / 2 + _focal * (x - _camera) / z, _horizon + _focal * (Eye - y) / z);
    }

    // ---- the sky and the sun

    private void Sky(CanvasDrawingSession ds, float t, float pulse)
    {
        var w = _w;
        var hy = _horizon;
        using (var sky = new CanvasLinearGradientBrush(ds,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 10, 4, 16) },
            new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(255, 40, 9, 44) },
            new CanvasGradientStop { Position = 0.8f, Color = Color.FromArgb(255, 104, 14, 44) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(255, 170, 30, 40) },
        ]) { StartPoint = new(0, 0), EndPoint = new(0, hy) })
            ds.FillRectangle(0, 0, w, hy + 1, sky);

        for (var i = 0; i < 90; i++)
        {
            var x = VisualNoise.Hash(i, 1) * w;
            var y = VisualNoise.Hash(i, 2) * VisualNoise.Hash(i, 3) * hy * 0.85f;
            var twinkle = 0.55f + 0.45f * MathF.Sin(t * (0.8f + 1.7f * VisualNoise.Hash(i, 4)) + i);
            var size = 0.6f + 1.2f * VisualNoise.Hash(i, 5);
            ds.FillCircle(x, y, size, Color.FromArgb((byte)(200 * twinkle * (1 - y / hy * 0.6f)), 255, 236, 220));
        }

        var radius = Math.Min(w * 0.2f, _h * 0.36f);
        var sun = new Vector2(w / 2 - _camera * 6, hy - radius * 0.58f);
        var glow = 0.4f + 0.35f * _bass + 0.15f * pulse;
        using (var halo = new CanvasRadialGradientBrush(ds,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 255, 90, 40) },
            new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(110, 230, 40, 50) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 160, 20, 60) },
        ], CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied) { Center = sun, RadiusX = radius * 2.4f, RadiusY = radius * 2.4f, Opacity = Math.Min(1, glow) })
        {
            ds.Blend = CanvasBlend.Add;
            ds.FillRectangle(sun.X - radius * 2.4f, sun.Y - radius * 2.4f, radius * 4.8f, hy - (sun.Y - radius * 2.4f), halo);
            ds.Blend = CanvasBlend.SourceOver;
        }
        using (var disc = new CanvasLinearGradientBrush(ds,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 232, 44, 36) },
            new CanvasGradientStop { Position = 0.5f, Color = Color.FromArgb(255, 255, 104, 22) },
            new CanvasGradientStop { Position = 0.85f, Color = Color.FromArgb(255, 255, 182, 30) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(255, 255, 214, 70) },
        ]) { StartPoint = sun - new Vector2(0, radius), EndPoint = sun + new Vector2(0, radius) })
            ds.FillCircle(sun, radius, disc);
        // The ground in front of the sun.
        ds.FillRectangle(0, hy, w, _h - hy, Night);

        // The haze along the horizon, gold under the sun.
        using var band = new CanvasRadialGradientBrush(ds,
        [
            new CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 255, 200, 90) },
            new CanvasGradientStop { Position = 0.4f, Color = Color.FromArgb(150, 255, 140, 40) },
            new CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 200, 60, 30) },
        ], CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied) { Center = new(sun.X, hy), RadiusX = w * 0.7f, RadiusY = _h * 0.05f, Opacity = 0.75f + 0.25f * pulse };
        ds.Blend = CanvasBlend.Add;
        ds.FillRectangle(sun.X - w * 0.7f, hy - _h * 0.05f, w * 1.4f, _h * 0.05f + _h * 0.035f, band);
        ds.Blend = CanvasBlend.SourceOver;
    }

    // ---- the land

    private void Land(CanvasDrawingSession ds, long first, float pulse)
    {
        var offset = (float)(_travel - first);
        for (var i = 0; i <= Rows; i++)
        {
            var r = first + i;
            var z = i - offset;
            for (var c = 0; c < Columns; c++)
            {
                var k = Math.Abs(c - Across);
                var side = Math.Sign(c - Across);
                var x = side * Spread[k];
                var zz = z;
                if (k > Valley + 1)
                {
                    x += (VisualNoise.Hash(r, c) - 0.5f) * 0.5f * (Spread[k] - Spread[k - 1]);
                    zz += (VisualNoise.Hash(r, c + 999) - 0.5f) * 0.4f;
                }
                _screen[i][c] = Project(x, Height(r, c), zz);
            }
        }

        for (var i = Rows - 1; i >= 0; i--)
        {
            var z = i - offset + 0.5f;
            if (z < 0.3f) continue;
            var fog = MathF.Pow(VisualPainters.Smooth(Far * 0.6f, Far, z), 1.6f);
            var face = VisualPalette.Mix(Night, Haze, fog);
            var bright = (0.55f + 0.45f * VisualPainters.Smooth(0, Far * 0.5f, z)) * (0.85f + 0.3f * pulse);
            var line = VisualPalette.Mix(VisualPalette.Mix(Night, Gold, Math.Min(1, bright)), HazeGlow, fog * 0.85f);
            var width = Math.Clamp(3.2f / z, 0.7f, 1.5f);
            var r = first + i;
            foreach (var side in new[] { -1, 1 }) Side(ds, r, i, side, face, line, width);
            Floor(ds, i, line, width, pulse);
        }
    }

    /// <summary>One side of a row's mountains, from the outside in, in runs that each rise or fall the whole way.</summary>
    private void Side(CanvasDrawingSession ds, long r, int i, int side, Color face, Color line, float width)
    {
        _runs.Clear();
        var start = Valley;
        var rising = true;
        for (var k = Valley; k < Across; k++)
        {
            var here = Height(r, Across + side * k) + Height(r + 1, Across + side * k);
            var next = Height(r, Across + side * (k + 1)) + Height(r + 1, Across + side * (k + 1));
            var up = next >= here;
            if (k == start) rising = up;
            else if (up != rising)
            {
                _runs.Add((start, k, rising));
                start = k;
                rising = up;
            }
        }
        _runs.Add((start, Across, rising));

        for (var n = _runs.Count - 1; n >= 0; n--)
        {
            var (from, to, up) = _runs[n];
            // Land falling away behind a peak that stands above the eye is hidden by that peak.
            if (!up && Math.Min(Height(r, Across + side * from), Height(r + 1, Across + side * from)) > Eye + 0.3f) continue;
            if (!Seen(i, side, from, to)) continue;
            using var fill = new CanvasPathBuilder(ds);
            using var edges = new CanvasPathBuilder(ds);
            for (var k = to - 1; k >= from; k--)
            {
                var inner = Across + side * k;
                var outer = Across + side * (k + 1);
                Vector2 a = _screen[i][inner], b = _screen[i][outer], c = _screen[i + 1][outer], d = _screen[i + 1][inner];
                fill.BeginFigure(a);
                fill.AddLine(b);
                fill.AddLine(c);
                fill.AddLine(d);
                fill.EndFigure(CanvasFigureLoop.Closed);
                Segment(edges, a, b);
                Segment(edges, d, c);
                Segment(edges, b, c);
                Segment(edges, a, c);
                if (k == from) Segment(edges, a, d);
            }
            using var faces = CanvasGeometry.CreatePath(fill);
            using var lines = CanvasGeometry.CreatePath(edges);
            ds.FillGeometry(faces, face);
            ds.DrawGeometry(lines, line, width);
        }
    }

    /// <summary>Whether any of a run of faces could land on screen.</summary>
    private bool Seen(int i, int side, int from, int to)
    {
        var lo = _screen[i][Across + side * from];
        var hi = _screen[i][Across + side * to];
        var top = Math.Min(Math.Min(lo.Y, hi.Y), Math.Min(_screen[i + 1][Across + side * from].Y, _screen[i + 1][Across + side * to].Y));
        for (var k = from; k <= to; k++) top = Math.Min(top, Math.Min(_screen[i][Across + side * k].Y, _screen[i + 1][Across + side * k].Y));
        var left = Math.Min(lo.X, hi.X);
        var right = Math.Max(lo.X, hi.X);
        return top < _h && right > -_w * 0.1f && left < _w * 1.1f;
    }

    /// <summary>The valley floor of one row: the grid's cross line and the lines running towards the horizon.</summary>
    private void Floor(CanvasDrawingSession ds, int i, Color line, float width, float pulse)
    {
        using var grid = new CanvasPathBuilder(ds);
        Segment(grid, _screen[i][Across - Valley], _screen[i][Across + Valley]);
        for (var c = Across - Valley; c <= Across + Valley; c++) Segment(grid, _screen[i][c], _screen[i + 1][c]);
        using var lines = CanvasGeometry.CreatePath(grid);
        ds.Blend = CanvasBlend.Add;
        ds.DrawGeometry(lines, Color.FromArgb((byte)(40 + 50 * pulse), line.R, line.G, line.B), width * 4);
        ds.Blend = CanvasBlend.SourceOver;
        ds.DrawGeometry(lines, line, width);
    }

    private static void Segment(CanvasPathBuilder path, Vector2 a, Vector2 b)
    {
        path.BeginFigure(a);
        path.AddLine(b);
        path.EndFigure(CanvasFigureLoop.Open);
    }
}
