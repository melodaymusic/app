using System.Numerics;
using MelodayMusic.Core.Lyrics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace MelodayMusic.Controls;

internal sealed class LyricPainter(bool backdrop) : IVisualPainter, IDisposable
{
    private sealed class SetLine : IDisposable
    {
        public required CanvasTextLayout[] Words { get; init; }
        public required Vector2[] Positions { get; init; }
        public required double[] Starts { get; init; }
        public required double[] Holds { get; init; }
        public required double Seconds { get; init; }
        public required float Height { get; init; }

        public void Dispose()
        {
            foreach (var w in Words) w.Dispose();
        }
    }

    private static readonly object TypeGate = new();
    private static double s_energy = 0.4, s_energyAt = -1;
    private readonly LyricFont _font = LyricFont.Nunito;
    private double _energy = 0.4;
    private float _shownAlpha = 1;
    private SetLine?[] _lines = [];
    private SetLine?[] _leaving = [];
    private string _lineKey = string.Empty;
    private double _changedAt;
    private double _lineStartSeconds;
    private float _setFor;
    private sealed class Backdrop(BackdropImage image) : IDisposable
    {
        private readonly object _gate = new();
        private bool _dead;
        public BackdropImage Image { get; } = image;
        public volatile CanvasBitmap? Bitmap;

        public void Give(CanvasBitmap bitmap)
        {
            lock (_gate)
            {
                if (_dead) bitmap.Dispose();
                else Bitmap = bitmap;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _dead = true;
                Bitmap?.Dispose();
                Bitmap = null;
            }
        }
    }

    private const double Hold = 15, Crossfade = 2.5;
    private List<Backdrop> _backdrops = [];
    private IReadOnlyList<BackdropImage>? _wanted;
    private double _cycleAt = -1, _firstAt = -1;

    public void Dispose()
    {
        DisposeAll(_lines);
        DisposeAll(_leaving);
        foreach (var b in _backdrops) b.Dispose();
        _backdrops = [];
        _scroll?.Dispose();
        _fade?.Dispose();
        DisposeClouds();
    }

    private static void DisposeAll(SetLine?[] lines)
    {
        foreach (var line in lines) line?.Dispose();
    }

    public void Draw(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        DrawBackground(ds, v, w, h);
        ChooseType(v, t);

        var beat = v.Beat;
        var period = beat.Period;
        var pop = Math.Clamp(period * 0.45, 0.16, 0.32);
        var away = Math.Clamp(period * 0.5, 0.18, 0.3);
        var position = v.Position.TotalSeconds;

        string key;
        string text;
        double lineStart;
        double available;
        IReadOnlyList<TimedWord> timed;
        var lyrics = v.Lyrics;
        if (lyrics is { IsSynced: false, IsInstrumental: false, Lines.Count: > 0 } && lyrics.Lines.Any(l => l.Text.Length > 0))
        {
            DrawScroll(ds, v, w, h);
            return;
        }
        var index = lyrics is { IsSynced: true } ? lyrics.IndexAt(v.Position) : -1;
        var sung = lyrics is { IsSynced: true } && index >= 0 && !string.IsNullOrWhiteSpace(lyrics.Lines[index].Text);
        if (sung)
        {
            key = "L" + index + ":" + lyrics!.Lines[index].Text;
            text = lyrics.Lines[index].Text.Trim();
            lineStart = lyrics.Lines[index].Start!.Value.TotalSeconds;
            available = LyricTiming.Available(lyrics, index);
            var words = lyrics.Lines[index].Words;
            timed = words is null
                ? LyricTiming.Words(text, available)
                : words.Select(x => new TimedWord(x.Text, Math.Max(0, (x.Start - lyrics.Lines[index].Start!.Value).TotalSeconds),
                    x.End is { } end && (end - x.Start).TotalSeconds >= 0.45 ? (end - x.Start).TotalSeconds : 0)).ToList();
        }
        else if (lyrics is { IsSynced: true, IsInstrumental: false } && lyrics.Lines.Any(l => l.Text.Length > 0))
        {
            if (_lineKey != "B")
            {
                DisposeAll(_leaving);
                if (_shownAlpha > 0.05f) _leaving = _lines;
                else DisposeAll(_lines);
                _lines = [];
                _lineKey = "B";
                _changedAt = t;
            }
            DrawLeaving(ds, away, t, Math.Min(h * 0.112f, Math.Min(w, h * 1.5f) * 0.086f), p);
            return;
        }
        else
        {
            key = "T" + v.Title + "|" + v.Artist;
            text = v.Title;
            lineStart = 0;
            available = 2.2;
            timed = LyricTiming.Words(text, available);
        }
        if (string.IsNullOrWhiteSpace(text)) return;

        var size = Math.Min(h * 0.112f, Math.Min(w, h * 1.5f) * 0.086f);
        if (key != _lineKey || Math.Abs(size - _setFor) > 0.5f)
        {
            var sameLine = key == _lineKey;
            if (sameLine) DisposeAll(_lines);
            else
            {
                DisposeAll(_leaving);
                _leaving = _lines;
                _changedAt = t;
            }
            if (sung)
            {
                _lines = [Set(ds, timed, available, w, h, size, h / 2, 0, _font, LyricFonts.Weight(_font, _energy))];
            }
            else
            {
                var weight = LyricFonts.Weight(_font, Math.Max(_energy, 0.7));
                var title = Set(ds, timed, available, w, h, size, h * 0.46f, 0, _font, weight);
                var artist = string.IsNullOrWhiteSpace(v.Artist) || title is null
                    ? null
                    : Set(ds, LyricTiming.Words(v.Artist, 1.6), 1.6, w, h, size * 0.46f, h * 0.46f + title.Height / 2 + size * 0.62f, 0.45, _font, LyricFonts.Weight(_font, 0.3));
                _lines = [title, artist];
                if (sameLine) _changedAt = t;
            }
            _lineKey = key;
            _setFor = size;
            _lineStartSeconds = lineStart;
        }
        if (_lines.Length == 0 || _lines[0] is null) return;

        var into = sung ? position - _lineStartSeconds : t - _changedAt;
        var alpha = sung ? 1 - VisualPainters.Smooth(0, 0.7f, (float)(into - (_lines[0]!.Seconds + 1.8))) : 1f;

        var arrive = _leaving.Length > 0 ? VisualPainters.Smooth((float)(away * 0.75), (float)away, (float)(t - _changedAt)) : 1f;
        alpha *= arrive;
        _shownAlpha = alpha;
        DrawLeaving(ds, away, t, size, p);
        foreach (var line in _lines)
            if (line is not null) DrawLine(ds, line, into, pop, alpha, 0, 1, p);
    }

    private void DrawLeaving(CanvasDrawingSession ds, double away, double t, float size, VisualPalette p)
    {
        if (_leaving.Length == 0) return;
        var s = (float)((t - _changedAt) / away);
        if (s >= 1)
        {
            DisposeAll(_leaving);
            _leaving = [];
            return;
        }
        var e = s * s;
        foreach (var gone in _leaving)
            if (gone is not null) DrawLine(ds, gone, double.MaxValue, 0.2, 1 - s, -size * 0.5f * e, 0.98f, p);
    }

    private void ChooseType(VisualInput v, double t)
    {
        lock (TypeGate)
        {
            var level = LyricFonts.Energy(v.Sound.Level);
            var dt = s_energyAt < 0 ? 0 : Math.Clamp(t - s_energyAt, 0, 0.5);
            s_energyAt = t;
            s_energy += (level - s_energy) * (1 - Math.Exp(-dt / 3.0));
            _energy = s_energy;
        }
    }

    private static SetLine? Set(CanvasDrawingSession ds, IReadOnlyList<TimedWord> timed, double available, float w, float h, float size, float centreY, double delay, LyricFont font, int weight)
    {
        if (timed.Count == 0) return null;
        var whole = string.Join(' ', timed.Select(x => x.Text));
        var wrap = w * 0.82f;
        CanvasTextLayout layout;
        var fit = size;
        for (var attempt = 0; ; attempt++)
        {
            layout = VisualFont.Layout(ds, whole, fit, weight, font, wrap, h, CanvasHorizontalAlignment.Center);
            if (layout.LayoutBounds.Height <= h * 0.58 || attempt >= 6) break;
            layout.Dispose();
            fit *= 0.88f;
        }
        using (layout)
        {
            var bounds = layout.LayoutBounds;
            var originX = (w - wrap) / 2;
            var originY = centreY - (float)bounds.Height / 2 - (float)bounds.Y;
            var words = new CanvasTextLayout[timed.Count];
            var positions = new Vector2[timed.Count];
            var starts = new double[timed.Count];
            var holds = new double[timed.Count];
            var at = 0;
            for (var i = 0; i < timed.Count; i++)
            {
                var region = layout.GetCharacterRegions(at, timed[i].Text.Length)[0].LayoutBounds;
                words[i] = VisualFont.Layout(ds, timed[i].Text, fit, weight, font, w, h, CanvasHorizontalAlignment.Left);
                positions[i] = new Vector2(originX + (float)region.X, originY + (float)region.Y);
                starts[i] = timed[i].Start + delay;
                holds[i] = timed[i].Hold;
                at += timed[i].Text.Length + 1;
            }
            var seconds = Math.Max(LyricTiming.SingingSeconds(timed.Count, available) + delay, starts[^1] + holds[^1]);
            return new SetLine { Words = words, Positions = positions, Starts = starts, Holds = holds, Seconds = seconds, Height = (float)bounds.Height };
        }
    }

    private CanvasTextLayout? _scroll;
    private string _scrollKey = string.Empty;
    private CanvasLinearGradientBrush? _fade;
    private float _fadeFor;

    private void DrawScroll(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var lyrics = v.Lyrics!;
        var key = v.Title + "|" + lyrics.Lines.Count + "|" + (int)w + "x" + (int)h + "|" + _font;
        if (key != _scrollKey)
        {
            _scroll?.Dispose();
            var size = Math.Clamp(Math.Min(w * 0.045f, h * 0.06f), 16f, 60f);
            _scroll = VisualFont.Layout(ds, string.Join('\n', lyrics.Lines.Select(l => l.Text)), size, LyricFonts.Weight(_font, 0.35), _font, w * 0.7f, 60000f, CanvasHorizontalAlignment.Center);
            _scrollKey = key;
        }
        if (_fade is null || Math.Abs(_fadeFor - h) > 0.5f)
        {
            _fade?.Dispose();
            _fadeFor = h;
            _fade = new CanvasLinearGradientBrush(ds, new[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.2f, Color = Color.FromArgb(255, 255, 255, 255) },
                new CanvasGradientStop { Position = 0.8f, Color = Color.FromArgb(255, 255, 255, 255) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(0, 255, 255, 255) },
            }) { StartPoint = new Vector2(0, 0), EndPoint = new Vector2(0, h) };
        }
        var bounds = _scroll!.LayoutBounds;
        var travel = (float)bounds.Height + h * 0.9f;
        var seconds = v.Duration.TotalSeconds > 5 ? v.Duration.TotalSeconds : 210;
        var progress = (float)Math.Clamp((v.Position.TotalSeconds - seconds * 0.04) / (seconds * 0.9), 0, 1);
        var top = h * 0.95f - progress * travel - (float)bounds.Y;
        using (ds.CreateLayer(_fade))
            ds.DrawTextLayout(_scroll, w * 0.15f, top, VisualPalette.With(Microsoft.UI.Colors.White, 0.92));
    }

    private static void DrawLine(CanvasDrawingSession ds, SetLine line, double into, double pop, float alpha, float lift, float scale, VisualPalette p)
    {
        if (alpha <= 0.002f) return;
        var saved = ds.Transform;
        for (var i = 0; i < line.Words.Length; i++)
        {
            var held = line.Holds[i];
            var progress = (float)Math.Clamp((into - line.Starts[i]) / Math.Max(0.05, pop), 0, 1);
            var settle = (float)Math.Clamp((into - line.Starts[i] - held) / 0.7, 0, 1);
            var eased = 1 - MathF.Pow(1 - progress, 3);
            var spring = 1 + 0.12f * MathF.Sin(progress * MathF.PI) * (1 - progress);
            var wordScale = scale * (0.84f + 0.16f * eased) * spring;
            if (held > 0)
            {
                var swell = (float)(Math.Clamp((into - line.Starts[i]) / 0.3, 0, 1) * (1 - Math.Clamp((into - line.Starts[i] - held) / 0.35, 0, 1)));
                wordScale *= 1 + 0.06f * swell;
            }
            var rise = (1 - eased) * line.Height * 0.12f + lift;
            var opacity = eased * alpha;
            var colour = VisualPalette.Mix(p.Light, Microsoft.UI.Colors.White, VisualPainters.Smooth(0, 1, settle));
            var bounds = line.Words[i].LayoutBounds;
            var centre = line.Positions[i] + new Vector2((float)bounds.Width / 2, (float)bounds.Height / 2);
            ds.Transform = Matrix3x2.CreateScale(wordScale, centre) * Matrix3x2.CreateTranslation(0, rise) * saved;
            ds.DrawTextLayout(line.Words[i], line.Positions[i], VisualPalette.With(colour, opacity));
        }
        ds.Transform = saved;
    }

    private void DrawBackground(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        DrawClouds(ds, v, w, h);
        if (!backdrop) return;
        if (!ReferenceEquals(v.Backdrops, _wanted)) Want(ds.Device, v.Backdrops);
        var ready = _backdrops.Where(b => b.Bitmap is not null).ToList();
        if (ready.Count == 0) return;
        if (_firstAt < 0)
        {
            _firstAt = t;
            _cycleAt = t;
        }
        var fade = VisualPainters.Smooth(0, 1.4f, (float)(t - _firstAt));
        var elapsed = t - _cycleAt;
        var slot = (int)(elapsed / Hold);
        var at = slot % ready.Count;
        var into = elapsed - slot * Hold;
        DrawPicture(ds, ready[at], t, w, h, fade, at);
        if (ready.Count > 1 && into > Hold - Crossfade)
        {
            var next = (at + 1) % ready.Count;
            DrawPicture(ds, ready[next], t, w, h, fade * VisualPainters.Smooth(0, 1, (float)((into - (Hold - Crossfade)) / Crossfade)), next);
        }
        ds.FillRectangle(0, 0, w, h, VisualPalette.With(p.Back, 0.60 * fade));
        ds.FillRectangle(0, 0, w, h, VisualPalette.With(p.Main, 0.10 * fade));
    }

    private void Want(CanvasDevice device, IReadOnlyList<BackdropImage> images)
    {
        _wanted = images;
        var old = _backdrops;
        var mine = _backdrops = [];
        foreach (var b in old) b.Dispose();
        _cycleAt = _firstAt = -1;
        foreach (var image in images)
        {
            var slot = new Backdrop(image);
            mine.Add(slot);
            _ = Task.Run(async () =>
            {
                try
                {
                    var loaded = await CanvasBitmap.LoadAsync(device, image.Path);
                    if (image.Soft)
                    {
                        try
                        {
                            slot.Give(Haze(device, loaded));
                        }
                        finally
                        {
                            loaded.Dispose();
                        }
                    }
                    else slot.Give(loaded);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.COMException)
                {
                }
            });
        }
    }

    private static CanvasBitmap Haze(CanvasDevice device, CanvasBitmap source)
    {
        var size = source.Size;
        var scale = Math.Min(1.0, 480 / Math.Max(size.Width, size.Height));
        var (tw, th) = ((float)(size.Width * scale), (float)(size.Height * scale));
        using var target = new CanvasRenderTarget(device, tw, th, 96);
        using (var ds = target.CreateDrawingSession())
        {
            using var blur = new GaussianBlurEffect { Source = source, BlurAmount = (float)(6 * scale), BorderMode = EffectBorderMode.Hard };
            ds.DrawImage(blur, new Windows.Foundation.Rect(0, 0, tw, th), new Windows.Foundation.Rect(0, 0, size.Width, size.Height));
        }
        return CanvasBitmap.CreateFromBytes(device, target.GetPixelBytes(), (int)target.SizeInPixels.Width, (int)target.SizeInPixels.Height, target.Format);
    }

    private static void DrawPicture(CanvasDrawingSession ds, Backdrop picture, double t, float w, float h, float opacity, int seed)
    {
        if (picture.Bitmap is not { } image || opacity <= 0.002f) return;
        var size = image.Size;
        var cover = Math.Max(w / size.Width, h / size.Height);
        var shift = seed * 17.0;
        var zoom = (float)(cover * (1.16 + 0.07 * Math.Sin((t + shift) * Math.PI * 2 / 52)));
        var dw = (float)size.Width * zoom;
        var dh = (float)size.Height * zoom;
        var cx = w / 2 + w * 0.035f * (float)Math.Sin((t + shift) * Math.PI * 2 / 37);
        var cy = h / 2 + h * 0.03f * (float)Math.Cos((t + shift) * Math.PI * 2 / 43);
        ds.DrawImage(image, new Windows.Foundation.Rect(cx - dw / 2, cy - dh / 2, dw, dh), new Windows.Foundation.Rect(0, 0, size.Width, size.Height), opacity, CanvasImageInterpolation.Linear);
    }

    private CanvasRadialGradientBrush[]? _clouds;
    private VisualPalette? _cloudsOf;

    private void DisposeClouds()
    {
        if (_clouds is null) return;
        foreach (var b in _clouds) b.Dispose();
        _clouds = null;
    }

    private void DrawClouds(CanvasDrawingSession ds, VisualInput v, float w, float h)
    {
        var (p, t) = (v.Palette, v.Time);
        ds.FillRectangle(0, 0, w, h, p.Back);
        var m = Math.Max(w, h);
        var bass = v.Sound.Bass;
        (Color Colour, float Fx, float Fy, float Px, float Py, float Radius, float Alpha)[] clouds =
        [
            (p.Tone(1), 0.13f, 0.17f, 0.0f, 1.0f, 0.66f, 0.62f),
            (p.Tone(2), 0.19f, 0.11f, 2.1f, 3.2f, 0.60f, 0.55f),
            (p.Tone(3), 0.11f, 0.21f, 4.0f, 0.4f, 0.58f, 0.50f),
        ];
        if (_clouds is null || !ReferenceEquals(_cloudsOf, p))
        {
            DisposeClouds();
            _cloudsOf = p;
            _clouds = clouds.Select(c => new CanvasRadialGradientBrush(ds, VisualPalette.With(c.Colour, c.Alpha), VisualPalette.With(c.Colour, 0))).ToArray();
        }
        for (var i = 0; i < clouds.Length; i++)
        {
            var (_, fx, fy, px, py, radius, _) = clouds[i];
            var c = new Vector2(w / 2 + w * 0.34f * MathF.Sin((float)t * fx * MathF.Tau + px), h / 2 + h * 0.30f * MathF.Sin((float)t * fy * MathF.Tau + py));
            var r = m * radius * (1 + 0.10f * bass);
            var brush = _clouds[i];
            (brush.Center, brush.RadiusX, brush.RadiusY) = (c, r, r);
            var (x0, y0) = (Math.Max(0, c.X - r), Math.Max(0, c.Y - r));
            var (x1, y1) = (Math.Min(w, c.X + r), Math.Min(h, c.Y + r));
            if (x1 > x0 && y1 > y0) ds.FillRectangle(x0, y0, x1 - x0, y1 - y0, brush);
        }
    }
}
