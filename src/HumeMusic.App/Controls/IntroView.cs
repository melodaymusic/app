using System.Diagnostics;
using System.Numerics;
using HumeMusic.Core.Intro;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// The startup intro, drawn with Win2D: a line draws out, a tape of notes runs through it in the
/// spectrum colours with fading echoes, it brakes into the Meloday wave, turns white with a soft
/// bloom, and "Meloday Music" rises in beneath. The maths is in <see cref="MelodayIntro"/>.
/// A click or key press skips it.
/// </summary>
public sealed partial class IntroView : UserControl
{
    private readonly CanvasControl _canvas;
    private readonly TextBlock _word;
    private readonly Stopwatch _clock = new();
    private readonly float[] _heights = new float[MelodayIntro.Points];
    private CanvasStrokeStyle? _stroke;
    private double _time = -1;
    private bool _running;
    private bool _finished;

    /// <summary>The intro is over (played through or skipped).</summary>
    public event EventHandler? Finished;

    public IntroView()
    {
        IsTabStop = true;
        AutomationProperties.SetName(this, "Meloday Music");
        _canvas = new CanvasControl { ClearColor = Colors.Black, IsHitTestVisible = false };
        _canvas.Draw += OnDraw;
        _word = new TextBlock
        {
            Text = "Meloday Music",
            FontFamily = (FontFamily)Application.Current.Resources["HumeFont"],
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            TextLineBounds = TextLineBounds.Full,
            IsHitTestVisible = false,
            Opacity = 0,
        };
        Content = new Grid { Background = new SolidColorBrush(Colors.Black), Children = { _canvas, _word } };
        Loaded += (_, _) => Start();
        Unloaded += (_, _) => StopClock();
        PointerPressed += (_, _) => Finish();
        KeyDown += (_, e) =>
        {
            e.Handled = true;
            Finish();
        };
    }

    private void Start()
    {
        if (_running || _finished) return;
        _running = true;
        _clock.Restart();
        CompositionTarget.Rendering += OnRendering;
        Focus(FocusState.Programmatic);
    }

    private void StopClock()
    {
        if (!_running) return;
        _running = false;
        CompositionTarget.Rendering -= OnRendering;
    }

    /// <summary>Stops where the animation ends (its final frame stays up) and reports that it's over.</summary>
    private void Finish()
    {
        if (_finished) return;
        _finished = true;
        StopClock();
        Show(MelodayIntro.End + 1);
        Finished?.Invoke(this, EventArgs.Empty);
    }

    private void OnRendering(object? sender, object e)
    {
        var t = _clock.Elapsed.TotalSeconds - MelodayIntro.Lead;
        if (t >= MelodayIntro.End)
        {
            Finish();
            return;
        }
        Show(t);
    }

    private void Show(double t)
    {
        _time = t;
        var width = ActualWidth;
        var height = ActualHeight;
        if (width > 0 && height > 0)
        {
            var f = MelodayIntro.Frame(Math.Max(0, t), width, height);
            _word.FontSize = f.WordSize;
            _word.LineHeight = f.WordSize;
            _word.Margin = new Thickness(0, f.WordTop, 0, 0);
            _word.Opacity = t < 0 ? 0 : f.WordOpacity;
            _word.Translation = new Vector3(0, (float)f.WordRise, 0);
            _word.CharacterSpacing = (int)Math.Round(f.WordSpacingEm * 1000);
        }
        _canvas.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        var t = _time;
        if (t < 0) return;
        var ds = args.DrawingSession;
        var f = MelodayIntro.Frame(t, sender.ActualWidth, sender.ActualHeight);
        _stroke ??= new CanvasStrokeStyle { StartCap = CanvasCapStyle.Round, EndCap = CanvasCapStyle.Round, LineJoin = CanvasLineJoin.Round };

        var half = (float)(MelodayIntro.Span / 2 * f.Scale);
        var stops = MelodayIntro.Spectrum
            .Select((c, k) => new CanvasGradientStop { Position = k / (float)(MelodayIntro.Spectrum.Length - 1), Color = Rgb(c) })
            .ToArray();
        using var spectrum = new CanvasLinearGradientBrush(sender, stops)
        {
            StartPoint = new Vector2((float)f.OriginX - half, 0),
            EndPoint = new Vector2((float)f.OriginX + half, 0),
        };

        // Echoes trail the moving line, adding light where they overlap.
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
        // In motion: the spectrum with a violet glow; at rest: white with a brief bloom.
        if (f.White < 1)
            Composite(ds, sender, line, spectrum, f.StrokeWidth, 1 - f.White, Color.FromArgb(140, 120, 110, 255), 18);
        if (f.White > 0)
        {
            using var white = new CanvasSolidColorBrush(sender, Colors.White);
            Composite(ds, sender, line, white, f.StrokeWidth, f.White, Color.FromArgb((byte)Math.Round(f.Bloom * 255), 255, 255, 255), 4 + 34 * f.Bloom);
        }
    }

    /// <summary>The line up to how far it has drawn out, as a path.</summary>
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

    /// <summary>
    /// Draws a stroke as one opaque layer composited at <paramref name="alpha"/> (so overlapping parts
    /// of the path never double up), with an optional glow behind it.
    /// </summary>
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
