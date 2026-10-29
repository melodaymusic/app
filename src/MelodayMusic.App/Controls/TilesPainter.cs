using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.UI;

namespace MelodayMusic.Controls;

/// <summary>
/// A patchwork of tiles packed on a grid that fills the whole picture, with a wave of the music sweeping across it:
/// where the wave and the sound are strong, tiles round off, shrink or light up, and their stripes run inwards.
/// Each tile listens to the part of the sound at its place along the wave (bass at the start, treble at the end).
/// The look changes with every song and every so often within one.
/// </summary>
internal sealed class TilesPainter : IVisualPainter
{
    private enum Fill { Solid, Hollow, Zebra }

    private sealed record Look(Fill Fill, bool Radial, Vector2 Axis, int Across, int Largest, bool Rounds, bool Shrinks, float Gap);

    private static readonly Look[] Looks =
    [
        new(Fill.Zebra, false, new(0.5f, 1f), 30, 4, Rounds: true, Shrinks: false, Gap: 0.18f),
        new(Fill.Zebra, true, new(0.5f, 0.5f), 34, 3, Rounds: true, Shrinks: false, Gap: 0.2f),
        new(Fill.Solid, false, new(1f, 0.85f), 26, 1, Rounds: true, Shrinks: true, Gap: 0.22f),
        new(Fill.Solid, true, new(0f, 0.5f), 22, 4, Rounds: true, Shrinks: false, Gap: 0.24f),
        new(Fill.Hollow, false, new(0f, 1f), 30, 3, Rounds: true, Shrinks: true, Gap: 0.2f),
        new(Fill.Hollow, true, new(1f, 1f), 26, 4, Rounds: false, Shrinks: true, Gap: 0.2f),
    ];

    private const double LookSeconds = 28, FadeSeconds = 0.35;

    private readonly struct Tile(int x, int y, int w, int h)
    {
        public readonly int X = x, Y = y, W = w, H = h;
    }

    private readonly List<Tile> _tiles = [];
    private const int Slices = 24;
    private readonly BandFollower _sound = new(Slices);
    private readonly Random _random = new();
    private Look _look = Looks[0];
    private int _cols, _rows;
    private float _cell, _width, _height;
    private string? _song;
    private double _last = -1, _since, _phase, _stripes;
    private float _loud, _fade = 1;
    private bool _leaving;

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (sound, p, t) = (v.Sound, v.Palette, v.Time);
        var dt = _last < 0 ? 1 / 60.0 : Math.Clamp(t - _last, 0.001, 0.1);
        _last = t;
        _since += dt;

        var song = v.Title + "\n" + v.Artist;
        if (song != _song || _tiles.Count == 0)
        {
            _song = song;
            Choose(LookFor(song));
            Lay(w, h);
        }
        else if (Math.Abs(w - _width) > 1 || Math.Abs(h - _height) > 1) Lay(w, h);

        var pulse = (float)v.Beat.Pulse(t);
        if (!_leaving && _since > LookSeconds && pulse > 0.9f) _leaving = true;
        if (_leaving)
        {
            _fade -= (float)(dt / FadeSeconds);
            if (_fade <= 0)
            {
                _leaving = false;
                Choose(_random.Next(Looks.Length));
                Lay(w, h);
            }
        }
        else _fade = Math.Min(1, _fade + (float)(dt / FadeSeconds));

        _sound.Update(sound.Bands, Slices, dt);
        _loud += (Math.Clamp(sound.Level * 3f, 0, 1) - _loud) * (1 - (float)Math.Exp(-dt / 0.3));
        _phase += dt * (0.08 + 0.55 * _loud) + pulse * dt * 1.4;
        _stripes += dt * (0.3 + 1.6 * _loud) + pulse * dt * 4;

        var stripeWidth = Math.Max(2f, _cell * 0.11f);
        var stroke = Math.Max(1.2f, _cell * 0.07f);
        var left = (w - _cols * _cell) / 2;
        var top = (h - _rows * _cell) / 2;
        var gap = _cell * _look.Gap;
        var fade = Math.Clamp(_fade, 0, 1);
        var grid = new Vector2(_cols, _rows);
        var focus = _look.Axis * grid;
        var reach = new[] { Vector2.Zero, new Vector2(_cols, 0), new Vector2(0, _rows), grid }.Max(c => Vector2.Distance(c, focus));
        var axis = Vector2.Normalize(_look.Axis * grid + new Vector2(0.001f));

        foreach (var tile in _tiles)
        {
            var centre = new Vector2(tile.X + tile.W / 2f, tile.Y + tile.H / 2f);
            float along;
            if (_look.Radial) along = Vector2.Distance(centre, focus) / reach;
            else
            {
                var span = MathF.Abs(axis.X) * _cols + MathF.Abs(axis.Y) * _rows;
                var start = new Vector2(axis.X < 0 ? _cols : 0, axis.Y < 0 ? _rows : 0);
                along = Vector2.Dot(centre - start, axis) / span;
            }
            along = Math.Clamp(along, 0, 1);

            var level = _sound.At(along, Slices);
            var wave = 0.5f + 0.5f * MathF.Sin(2 * MathF.PI * (along * 1.3f - (float)_phase));
            var amount = Math.Clamp(0.35f * wave + 1.0f * level + 0.3f * pulse, 0, 1);

            var scale = _look.Shrinks ? 1 - 0.7f * (1 - amount) : 1;
            var tw = Math.Max(1, tile.W * _cell - gap) * scale;
            var th = Math.Max(1, tile.H * _cell - gap) * scale;
            var x = left + tile.X * _cell + (tile.W * _cell - tw) / 2;
            var y = top + tile.Y * _cell + (tile.H * _cell - th) / 2;
            var round = Math.Min(tw, th) / 2 * (_look.Rounds ? amount * amount : 0.08f);

            var shade = (along * 0.7f + (float)_phase * 0.25f + amount * 0.45f) % 1;
            var colour = Gradient(p, shade);
            var alpha = fade * (0.35f + 0.65f * amount);

            switch (_look.Fill)
            {
                case Fill.Solid:
                    ds.FillRoundedRectangle(x, y, tw, th, round, round, VisualPalette.With(colour, alpha));
                    break;
                case Fill.Hollow:
                    ds.DrawRoundedRectangle(x, y, tw, th, round, round, VisualPalette.With(colour, alpha), stroke * (1 + amount));
                    break;
                default:
                    var flow = (float)(_stripes % 1) * stripeWidth * 2;
                    for (var inset = flow - stripeWidth * 2; inset < Math.Min(tw, th) / 2; inset += stripeWidth * 2)
                    {
                        if (inset < 0) continue;
                        var r = Math.Max(0, round - inset);
                        ds.DrawRoundedRectangle(x + inset, y + inset, tw - 2 * inset, th - 2 * inset, r, r, VisualPalette.With(colour, alpha), stripeWidth);
                    }
                    break;
            }
        }
    }

    /// <summary>The look a song starts with: the same every time it plays, different from song to song.</summary>
    internal static int LookFor(string song)
    {
        var hash = 17;
        foreach (var c in song) hash = unchecked(hash * 31 + c);
        return (int)((uint)hash % Looks.Length);
    }

    internal static int LookCount => Looks.Length;

    private void Choose(int index)
    {
        if (_tiles.Count > 0 && ReferenceEquals(Looks[index], _look)) index = (index + 1) % Looks.Length;
        _look = Looks[index];
        _since = 0;
    }

    /// <summary>Packs tiles onto a grid of square cells that covers the whole picture, so full screen gets more of them.</summary>
    private void Lay(float w, float h)
    {
        (_width, _height) = (w, h);
        _cell = Math.Max(w, h) / _look.Across;
        _cols = Math.Max(1, (int)(w * 0.97f / _cell));
        _rows = Math.Max(1, (int)(h * 0.97f / _cell));
        var used = new bool[_cols, _rows];
        _tiles.Clear();
        for (var y = 0; y < _rows; y++)
        for (var x = 0; x < _cols; x++)
        {
            if (used[x, y]) continue;
            var long1 = 1 + _random.Next(_look.Largest);
            var long2 = 1 + _random.Next(Math.Max(1, _look.Largest / 2 + 1));
            var (tw, th) = _random.Next(2) == 0 ? (long1, long2) : (long2, long1);
            tw = Math.Min(tw, _cols - x);
            for (var i = 1; i < tw; i++)
                if (used[x + i, y]) { tw = i; break; }
            th = Math.Min(th, _rows - y);
            for (var j = 1; j < th; j++)
            for (var i = 0; i < tw; i++)
                if (used[x + i, y + j]) { th = Math.Min(th, j); break; }
            for (var j = 0; j < th; j++)
            for (var i = 0; i < tw; i++)
                used[x + i, y + j] = true;
            _tiles.Add(new Tile(x, y, tw, th));
        }
    }

    private static Color Gradient(VisualPalette p, float t)
    {
        var at = t * 3;
        var i = (int)at;
        Color a = p.Tone(1 + i % 3), b = p.Tone(1 + (i + 1) % 3);
        return VisualPalette.Mix(a, b, at - i);
    }
}
