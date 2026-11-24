using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Windows.Foundation;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>
/// Light Wall: columns of warm, out-of-focus lights, packed so close that each lit stretch reads as a glowing bar. Each
/// column belongs to one part of the sound, low on the left to high on the right. Bright patches drift up through the
/// wall, and the louder a column's part of the sound, the more of it lights and the hotter it burns. The beat lifts the
/// whole wall.
/// </summary>
internal sealed class LightWallPainter : IVisualPainter, IDisposable
{
    private const int Sprite = 96, Most = 48;
    private static readonly Vector4 Amber = new(1f, 0.62f, 0.2f, 1), Hot = new(1f, 0.9f, 0.72f, 1);

    private readonly BandFollower _bands = new(Most);
    private readonly float[] _level = new float[Most];
    private CanvasDevice? _device;
    private CanvasRenderTarget? _bead, _soft;
    private double _last = -1;
    private float _rise, _energy;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var t = v.Time;
        var dt = _last < 0 ? 1 / 60.0 : Math.Clamp(t - _last, 0.001, 0.1);
        _last = t;
        Prepare(ds);

        var columns = Math.Clamp((int)MathF.Round(w / 44), 10, Most);
        _bands.Update(v.Sound.Bands, columns, dt);
        for (var i = 0; i < columns; i++) _level[i] += (_bands[i] - _level[i]) * (1 - MathF.Exp(-(float)dt / 0.12f));
        _energy += (Math.Clamp(v.Sound.Level * 3f, 0, 1) - _energy) * (1 - MathF.Exp(-(float)dt / 0.5f));
        _rise += (float)dt * (0.22f + 0.35f * _energy);
        var pulse = (float)v.Beat.Pulse(t);

        // A dark amber room behind the lights, warmest where the wall is busiest.
        ds.FillRectangle(0, 0, w, h, Color.FromArgb(255, 14, 8, 3));
        using (var warmth = new CanvasRadialGradientBrush(ds, Color.FromArgb(255, 70, 40, 10), Color.FromArgb(0, 30, 16, 4)) { Center = new(w / 2, h / 2), RadiusX = w * 0.7f, RadiusY = h * 0.75f, Opacity = 0.5f + 0.4f * _energy })
            ds.FillRectangle(0, 0, w, h, warmth);

        var pitch = w / columns;
        var size = pitch * 0.94f;
        var step = size * 0.3f;
        var rows = (int)(h / step) + 3;
        var lift = 0.12f * pulse;

        ds.Blend = CanvasBlend.Add;
        if (CanvasSpriteBatch.IsSupported(ds.Device))
        {
            using (var glow = ds.CreateSpriteBatch(CanvasSpriteSortMode.None, CanvasImageInterpolation.Linear, CanvasSpriteOptions.None))
                Lay(columns, rows, pitch, size, step, h, lift, (rect, tint) => glow.Draw(_soft!, Grow(rect, 2.6f), tint * 0.2f));
            using var beads = ds.CreateSpriteBatch(CanvasSpriteSortMode.None, CanvasImageInterpolation.Linear, CanvasSpriteOptions.None);
            Lay(columns, rows, pitch, size, step, h, lift, (rect, tint) => beads.Draw(_bead!, rect, tint));
        }
        else
        {
            Lay(columns, rows, pitch, size, step, h, lift, (rect, tint) => ds.DrawImage(_bead!, rect, new Rect(0, 0, Sprite, Sprite), tint.X * 0.8f + 0.2f * tint.W));
        }
        ds.Blend = CanvasBlend.SourceOver;
    }

    /// <summary>Works out every light's place and colour and hands each to <paramref name="put"/>.</summary>
    private void Lay(int columns, int rows, float pitch, float size, float step, float h, float lift, Action<Rect, Vector4> put)
    {
        for (var i = 0; i < columns; i++)
        {
            var level = _level[i];
            var x = (i + 0.5f) * pitch;
            // Patches of light drift upwards through each column, a little out of step with its neighbours.
            var drift = _rise * (0.85f + 0.3f * VisualNoise.Hash(i, 7));
            var threshold = 0.52f - 0.45f * level - lift;
            for (var j = 0; j < rows; j++)
            {
                var y = h + step - j * step;
                var field = VisualNoise.Layered(i * 0.42f + 3, y / h * 2.4f + drift * 2.2f, 11);
                var lit = VisualPainters.Smooth(threshold, threshold + 0.22f, field);
                var bright = 0.13f + lit * (0.55f + 0.45f * level + lift);
                if (bright < 0.03f) continue;
                var heat = Math.Clamp(bright * bright, 0, 1);
                var colour = Vector4.Lerp(Amber, Hot, heat) * Math.Min(1, bright);
                colour.W = Math.Min(1, bright);
                put(new Rect(x - size / 2, y - size / 2, size, size), colour);
            }
        }
    }

    private static Rect Grow(Rect r, float by)
    {
        var w = r.Width * by;
        var h = r.Height * by;
        return new Rect(r.X + r.Width / 2 - w / 2, r.Y + r.Height / 2 - h / 2, w, h);
    }

    /// <summary>Makes the two lights everything is drawn from: a lens-blurred bead with a brighter rim, and a wide soft glow.</summary>
    private void Prepare(CanvasDrawingSession ds)
    {
        if (_device == ds.Device && _bead is not null) return;
        Dispose();
        _device = ds.Device;
        _bead = new CanvasRenderTarget(ds.Device, Sprite, Sprite, 96);
        using (var draw = _bead.CreateDrawingSession())
        {
            draw.Clear(Color.FromArgb(0, 0, 0, 0));
            using var disc = new CanvasRadialGradientBrush(draw,
            [
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 0, Color = Color.FromArgb(120, 255, 255, 255) },
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 0.5f, Color = Color.FromArgb(110, 255, 255, 255) },
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 0.74f, Color = Color.FromArgb(90, 255, 255, 255) },
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
            ], CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied) { Center = new(Sprite / 2f), RadiusX = Sprite / 2f, RadiusY = Sprite / 2f };
            draw.FillCircle(Sprite / 2f, Sprite / 2f, Sprite / 2f, disc);
        }
        _soft = new CanvasRenderTarget(ds.Device, Sprite, Sprite, 96);
        using (var draw = _soft.CreateDrawingSession())
        {
            draw.Clear(Color.FromArgb(0, 0, 0, 0));
            using var glow = new CanvasRadialGradientBrush(draw,
            [
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 0, Color = Color.FromArgb(255, 255, 255, 255) },
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 0.35f, Color = Color.FromArgb(120, 255, 255, 255) },
                new Microsoft.Graphics.Canvas.Brushes.CanvasGradientStop { Position = 1, Color = Color.FromArgb(0, 255, 255, 255) },
            ], CanvasEdgeBehavior.Clamp, CanvasAlphaMode.Premultiplied) { Center = new(Sprite / 2f), RadiusX = Sprite / 2f, RadiusY = Sprite / 2f };
            draw.FillCircle(Sprite / 2f, Sprite / 2f, Sprite / 2f, glow);
        }
    }

    public void Dispose()
    {
        _bead?.Dispose();
        _soft?.Dispose();
        (_bead, _soft, _device) = (null, null, null);
    }
}
