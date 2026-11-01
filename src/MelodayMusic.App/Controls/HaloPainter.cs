using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;

namespace MelodayMusic.Controls;

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
