using System.Numerics;
using MelodayMusic.Core.Intro;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class IntroView : UserControl
{
    private readonly CanvasAnimatedControl _canvas;
    private readonly string _fontFamily;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly ILogger<IntroView> _log;
    private readonly float[] _heights = new float[MelodayIntro.Points];
    private CanvasStrokeStyle? _stroke;
    private double? _firstFrame;
    private CanvasLinearGradientBrush? _spectrum;
    private CanvasTextLayout? _wordLayout;
    private double _wordWidth, _wordSize;
    private volatile bool _finished;
    private int _completionQueued;
    private volatile bool _unloaded;
    private long _lastDrawAt;
    private int _frames;
    private double _largestGap;
    private bool _drawingFailed;

    public event EventHandler? Finished;

    public IntroView()
    {
        IsTabStop = true;
        _dispatcher = DispatcherQueue;
        _log = App.Services.GetRequiredService<ILogger<IntroView>>();
        AutomationProperties.SetName(this, "Meloday Music");
        AutomationProperties.SetHelpText(this, "Press any key or click to skip the opening animation.");
        _fontFamily = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "Nunito.ttf")).AbsoluteUri + "#Nunito";
        _canvas = new CanvasAnimatedControl { ClearColor = Colors.Black, IsHitTestVisible = false, IsFixedTimeStep = false };
        AutomationProperties.SetAccessibilityView(_canvas, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) => ResetResources();
        Content = new Grid { Background = new SolidColorBrush(Colors.Black), Children = { _canvas } };
        Loaded += (_, _) =>
        {
            Helpers.Motion.Changed += OnMotionChanged;
            Focus(FocusState.Programmatic);
            OnMotionChanged(null, EventArgs.Empty);
        };
        Unloaded += (_, _) =>
        {
            _unloaded = true;
            Helpers.Motion.Changed -= OnMotionChanged;
            _canvas.RemoveFromVisualTree();
        };
        PointerPressed += (_, _) => Finish();
        KeyDown += (_, e) =>
        {
            e.Handled = true;
            Finish();
        };
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (!Helpers.Motion.IsEnabled) Finish();
        else if (!_finished) _canvas.Paused = !Helpers.Motion.IsWindowActive;
    }

    private void ResetResources()
    {
        _stroke?.Dispose(); _stroke = null;
        _spectrum?.Dispose(); _spectrum = null;
        _wordLayout?.Dispose(); _wordLayout = null;
    }

    private void Finish()
    {
        if (_finished || _unloaded) return;
        _finished = true;
        _canvas.Paused = true;
        _canvas.Invalidate();
        _log.LogInformation("Startup intro rendered {Frames} frames; largest frame interval {Gap:F1} ms", _frames, _largestGap);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void OnDraw(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        if (_drawingFailed) return;
        try { DrawFrame(sender, args); }
        catch (Exception ex)
        {
            _drawingFailed = true;
            _log.LogError(ex, "Startup intro drawing failed");
            if (Interlocked.Exchange(ref _completionQueued, 1) == 0)
                _dispatcher.TryEnqueue(Finish);
        }
    }

    private void DrawFrame(ICanvasAnimatedControl sender, CanvasAnimatedDrawEventArgs args)
    {
        var ds = args.DrawingSession;
        var size = sender.Size;
        if (_firstFrame is null)
        {
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            DrawArtwork(sender, ds, 0.5, size.Width, size.Height);
            DrawArtwork(sender, ds, MelodayIntro.End, size.Width, size.Height);
            ds.Clear(Colors.Black);
            _firstFrame = args.Timing.TotalTime.TotalSeconds + System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalSeconds;
            return;
        }
        var t = _finished ? MelodayIntro.End + 1 : args.Timing.TotalTime.TotalSeconds - _firstFrame.Value - MelodayIntro.Lead;
        if (!_finished && _completionQueued == 0)
        {
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (_lastDrawAt != 0)
                _largestGap = Math.Max(_largestGap, System.Diagnostics.Stopwatch.GetElapsedTime(_lastDrawAt, now).TotalMilliseconds);
            _lastDrawAt = now;
            ++_frames;
        }
        if (t >= MelodayIntro.End && Interlocked.Exchange(ref _completionQueued, 1) == 0)
            _dispatcher.TryEnqueue(Finish);
        if (t < 0) return;
        DrawArtwork(sender, ds, t, size.Width, size.Height);
    }

    private void DrawArtwork(ICanvasResourceCreator sender, CanvasDrawingSession ds, double t, double width, double height)
    {
        var f = MelodayIntro.Frame(t, width, height);
        _stroke ??= new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };

        var half = (float)(MelodayIntro.Span / 2 * f.Scale);
        _spectrum ??= new CanvasLinearGradientBrush(sender, MelodayIntro.Spectrum
            .Select((c, k) => new CanvasGradientStop { Position = k / (float)(MelodayIntro.Spectrum.Length - 1), Color = Rgb(c) })
            .ToArray());
        var spectrum = _spectrum;
        spectrum.StartPoint = new Vector2((float)f.OriginX - half, 0);
        spectrum.EndPoint = new Vector2((float)f.OriginX + half, 0);

        if (f.EchoFade > 0.001)
        {
            for (var g = MelodayIntro.Echoes.Length - 1; g >= 0; g--)
            {
                MelodayIntro.Shape(Math.Max(0, t - MelodayIntro.Echoes[g]), _heights);
                using var echo = Line(sender, f);
                var alpha = 0.22 * f.EchoFade * (1 - g / (MelodayIntro.Echoes.Length + 1.0));
                ds.Blend = CanvasBlend.Add;
                Composite(ds, sender, echo, spectrum, f.StrokeWidth * 0.7, alpha, default, 0);
                ds.Blend = CanvasBlend.SourceOver;
            }
        }

        MelodayIntro.Shape(t, _heights);
        using var line = Line(sender, f);
        if (f.White < 1)
            Composite(ds, sender, line, spectrum, f.StrokeWidth, 1 - f.White, Color.FromArgb(140, 120, 110, 255), 18);
        if (f.White > 0)
        {
            using var white = new CanvasSolidColorBrush(sender, Colors.White);
            Composite(ds, sender, line, white, f.StrokeWidth, f.White, Color.FromArgb((byte)Math.Round(f.Bloom * 255), 255, 255, 255), 4 + 34 * f.Bloom);
        }
        if (f.WordOpacity > 0)
        {
            if (_wordLayout is null || _wordWidth != width || _wordSize != f.WordSize)
            {
            _wordLayout?.Dispose();
            using var format = new CanvasTextFormat
            {
                FontFamily = _fontFamily, FontSize = (float)f.WordSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = CanvasHorizontalAlignment.Center,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            _wordLayout = new CanvasTextLayout(sender, "Meloday Music", format, (float)width, (float)f.WordSize * 2);
            _wordWidth = width; _wordSize = f.WordSize;
            }
            var word = _wordLayout;
            word.SetCharacterSpacing(0, 13, 0, (float)(f.WordSpacingEm * f.WordSize), 0);
            ds.DrawTextLayout(word, 0, (float)(f.WordTop + f.WordRise), Color.FromArgb((byte)Math.Round(f.WordOpacity * 255), 255, 255, 255));
        }
    }

    private CanvasGeometry Line(ICanvasResourceCreator creator, IntroFrame f)
    {
        using var path = new CanvasPathBuilder(creator);
        for (var i = 0; i <= f.LastPoint; i++)
        {
            var point = new Vector2(
                (float)(f.OriginX + (MelodayIntro.U(i) - 0.5) * MelodayIntro.Span * f.Scale),
                (float)(f.OriginY + _heights[i] * f.Scale));
            if (i == 0) path.BeginFigure(point);
            else path.AddLine(point);
        }
        path.EndFigure(CanvasFigureLoop.Open);
        return CanvasGeometry.CreatePath(path);
    }

    private void Composite(CanvasDrawingSession ds, ICanvasResourceCreator creator, CanvasGeometry line, ICanvasBrush brush,
        double width, double alpha, Color glow, double blur)
    {
        using var layer = new CanvasCommandList(creator);
        using (var session = layer.CreateDrawingSession())
            session.DrawGeometry(line, brush, (float)width, _stroke);
        if (glow.A > 0 && blur > 0)
        {
            using var shadow = new ShadowEffect { Source = layer, BlurAmount = (float)(blur / 2), ShadowColor = glow };
            using var faded = new OpacityEffect { Source = shadow, Opacity = (float)alpha };
            ds.DrawImage(faded);
        }
        using var image = new OpacityEffect { Source = layer, Opacity = (float)alpha };
        ds.DrawImage(image);
    }

    private static Color Rgb(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
