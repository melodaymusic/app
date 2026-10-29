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

internal sealed class WavePainter : IVisualPainter
{
    private const int Points = 48, Echoes = 3, History = 20, EchoGap = 5;
    private const int Memory = 160, Layers = 4;
    private const double Interval = 1.0 / 50, Crossing = 1.5;
    private static readonly float[] Cycles = [0.8f, 1.5f, 2.3f, 3.4f];
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
        var pulse = (float)v.Beat.Pulse(t);
        if (v.Beat.IsLocked) _tempo += (Math.Clamp(v.Beat.Bpm, 60, 180) - _tempo) * (1 - Math.Exp(-dt / 2));
        Span<float> target = stackalloc float[Layers];
        target[0] = Math.Clamp(sound.Bass * 1.1f, 0, 1);
        target[1] = Math.Clamp(VisualPainters.Average(bands, n / 4, n * 3 / 8) * 1.4f, 0, 1);
        target[2] = Math.Clamp(VisualPainters.Average(bands, n * 3 / 8, n * 3 / 4) * 1.8f, 0, 1);
        target[3] = Math.Clamp(VisualPainters.Average(bands, n * 3 / 4, n) * 2.4f, 0, 1);
        var ease = 1 - (float)Math.Exp(-dt / 0.16);
        for (var k = 0; k < Layers; k++) _amplitude[k] += (target[k] - _amplitude[k]) * ease;

        _sinceSample += dt;
        while (_sinceSample >= Interval)
        {
            _sinceSample -= Interval;
            _flowNewest = (_flowNewest + 1) % Memory;
            Array.Copy(_amplitude, _flow[_flowNewest], Layers);
        }
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
