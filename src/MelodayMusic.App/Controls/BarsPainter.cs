using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;

namespace MelodayMusic.Controls;

internal sealed class BarsPainter : IVisualPainter
{
    private const int Count = 12;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p, t) = (v.Sound, v.Palette, (float)v.Time);
        var bands = sound.Bands;
        var span = Math.Min(w * 0.84f, h * 1.6f);
        var slot = span / Count;
        var bw = slot * 0.88f;
        var left = (w - span) / 2 + (slot - bw) / 2;
        var mid = h * 0.5f;
        var tallest = Math.Min(h * 0.64f, span * 0.62f);
        var swell = 1 + 0.06f * (float)v.Beat.Pulse(v.Time);
        using var fill = new CanvasLinearGradientBrush(ds, p.Tone(3), p.Tone(1));
        for (var i = 0; i < Count; i++)
        {
            var level = Math.Clamp(VisualPainters.Average(bands, i * bands.Length / Count, (i + 1) * bands.Length / Count) * 1.15f, 0, 1);
            var bh = Math.Min(h * 0.9f, bw + level * (tallest - bw) * swell);
            var drift = MathF.Sin(i * 1.9f + t * 0.7f) * h * 0.035f * (0.4f + level);
            var top = Math.Clamp(mid - bh / 2 + drift, h * 0.04f, h * 0.96f - bh);
            var x = left + i * slot;
            fill.StartPoint = new Vector2(x, top);
            fill.EndPoint = new Vector2(x, top + bh);
            ds.FillRoundedRectangle(x, top, bw, bh, bw / 2, bw / 2, fill);
        }
    }
}
