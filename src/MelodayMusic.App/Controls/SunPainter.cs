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

internal sealed class SunPainter : IVisualPainter
{
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
            var colour = i == Rings.Length - 1 ? VisualPalette.Mix(p.Tone(3), Microsoft.UI.Colors.White, 0.18) : p.Tone(i);
            ds.FillCircle(c, radius, VisualPalette.With(colour, alpha));
        }
    }
}
