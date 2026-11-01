using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace MelodayMusic.Controls;

/// <summary>
/// Columns of rounded pills, one per frequency band (bass at the left), that stretch, light up and jump on the beat.
/// </summary>
internal sealed class PillarsPainter : IVisualPainter
{
    private const int Most = 32, Pills = 7;

    private readonly float[][] _lengths = Enumerable.Range(0, Most)
        .Select(i => Enumerable.Range(0, Pills).Select(k => 0.18f + 0.45f * Unit(i * 12.9898f + k * 78.233f)).ToArray())
        .ToArray();
    private readonly float[] _weights = Enumerable.Range(0, Most).Select(i => 0.65f + 0.7f * Unit(i * 4.1414f + 1.7f)).ToArray();
    private readonly float[] _slide = new float[Most];
    private readonly BandFollower _sound = new(Most);
    private double _last = -1;

    private static float Unit(float seed)
    {
        var s = MathF.Sin(seed) * 43758.5453f;
        return s - MathF.Floor(s);
    }

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p, t) = (v.Sound, v.Palette, v.Time);
        var dt = _last < 0 ? 1 / 60f : (float)Math.Clamp(t - _last, 0.001, 0.1);
        _last = t;

        var bands = sound.Bands;
        var span = w * 0.94f;
        var height = h * 0.92f;
        var count = Math.Clamp((int)MathF.Round(span / (height * 0.1f)), 8, Most);
        var left = (w - span) / 2;
        var top = (h - height) / 2;
        var gap = span * 0.008f;
        var room = span - gap * (count - 1);
        var total = 0f;
        for (var i = 0; i < count; i++) total += _weights[i];
        var pulse = (float)v.Beat.Pulse(t);
        _sound.Update(bands, count, dt);

        using var layer = ds.CreateLayer(1f, new Rect(left, top, span, height));
        var x = left;
        for (var i = 0; i < count; i++)
        {
            var level = _sound[i];

            var colWidth = room * _weights[i] / total;
            var gapV = colWidth * 0.22f;
            var period = 0f;
            foreach (var length in _lengths[i]) period += length * height + gapV;
            _slide[i] += (i % 2 == 0 ? 1 : -1) * dt * height * (0.02f + 0.9f * level + 1.6f * pulse);
            var shift = (_slide[i] % period + period) % period;

            var colour = VisualPalette.Mix(p.Tone(1), p.Tone(3), 0.25 + 0.75 * level);
            var y = top - shift;
            for (var k = 0; y < top + height; k++)
            {
                var basic = _lengths[i][k % Pills] * height;
                var drawn = Math.Max(colWidth, basic * (0.3f + 0.7f * level));
                var shade = k % 2 == 0 ? colour : VisualPalette.Mix(colour, p.Tone(2), 0.35);
                ds.FillRoundedRectangle(x, y + (basic - drawn) / 2, colWidth, drawn, colWidth / 2, colWidth / 2, VisualPalette.With(shade, 0.5 + 0.5 * level));
                y += basic + gapV;
            }
            x += colWidth + gap;
        }
    }
}
