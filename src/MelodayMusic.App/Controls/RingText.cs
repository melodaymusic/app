using System.Numerics;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public sealed partial class RingText : Grid
{
    private const double Turn = 90;
    private const int Repeats = 4;

    private readonly Canvas _ring = new();
    private bool _onScreen = true;
    private bool _turning;

    public RingText()
    {
        IsHitTestVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_ring);
        Loaded += (_, _) =>
        {
            Motion.Changed += OnMotionChanged;
            Build();
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnMotionChanged;
            Rest();
        };
        EffectiveViewportChanged += (_, e) =>
        {
            var onScreen = e.EffectiveViewport.Width > 0 && e.EffectiveViewport.Height > 0
                           && e.EffectiveViewport.Right > 0 && e.EffectiveViewport.Bottom > 0
                           && e.EffectiveViewport.Left < ActualWidth && e.EffectiveViewport.Top < ActualHeight;
            if (onScreen == _onScreen) return;
            _onScreen = onScreen;
            DispatcherQueue.TryEnqueue(UpdateTurning);
        };
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(RingText), new PropertyMetadata(null, (d, _) => ((RingText)d).Build()));

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(
        nameof(Diameter), typeof(double), typeof(RingText), new PropertyMetadata(200.0, (d, _) => ((RingText)d).Build()));

    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    private void Build()
    {
        Rest();
        _ring.Children.Clear();
        var text = Text?.Trim();
        Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrEmpty(text) || Diameter <= 0) return;

        var resources = Application.Current.Resources;
        var fontSize = 13.0;
        TextBlock Glyph(string c) => new()
        {
            Text = c,
            FontFamily = (FontFamily)resources["MelodayFont"],
            FontWeight = FontWeights.Bold,
            FontSize = fontSize,
            Foreground = (Brush)resources["MelodayOnColorPrimaryBrush"],
        };
        Windows.Foundation.Size Measure(string s)
        {
            var probe = Glyph(s);
            probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return probe.DesiredSize;
        }
        double Space() => Measure("a a").Width - Measure("aa").Width;
        List<(char Char, double Width, double Height)> Glyphs()
        {
            var space = Space();
            return text.Select(c =>
            {
                if (c == ' ') return (c, space, 0.0);
                var size = Measure(c.ToString());
                return (c, size.Width, size.Height);
            }).ToList();
        }

        const double BandPerPoint = 1.6;
        var unit = Glyphs().Sum(g => g.Width) + Measure("•").Width + 2 * Space();
        var denominator = Repeats * unit / fontSize - Math.PI * BandPerPoint;
        fontSize = Math.Clamp(denominator > 0 ? Math.PI * Diameter / denominator : 22, UiScale.Text(10), UiScale.Text(22));

        var glyphs = Glyphs();
        var band = Math.Round(fontSize * BandPerPoint);
        var radius = Diameter / 2 + band / 2;
        var size = Diameter + band * 2;
        Width = Height = size;
        _ring.Width = _ring.Height = size;
        var centre = size / 2;
        var circumference = 2 * Math.PI * radius;
        var textWidth = glyphs.Sum(g => g.Width);
        var gap = Math.Max(0, (circumference - textWidth * Repeats) / Repeats);

        void Place(string c, double width, double height, double middle)
        {
            var glyph = Glyph(c);
            Canvas.SetLeft(glyph, centre - width / 2);
            Canvas.SetTop(glyph, centre - radius - height / 2);
            glyph.RenderTransform = new RotateTransform { Angle = middle / circumference * 360, CenterX = width / 2, CenterY = radius + height / 2 };
            _ring.Children.Add(glyph);
        }

        var bullet = Measure("•");
        var travelled = 0.0;
        for (var r = 0; r < Repeats; r++)
        {
            foreach (var (c, w, h) in glyphs)
            {
                if (c != ' ') Place(c.ToString(), w, h, travelled + w / 2);
                travelled += w;
            }
            Place("•", bullet.Width, bullet.Height, travelled + gap / 2);
            travelled += gap;
        }
        UpdateTurning();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => UpdateTurning();

    private void UpdateTurning()
    {
        var turn = Motion.IsActive && IsLoaded && _onScreen && _ring.Children.Count > 0;
        if (turn == _turning) return;
        if (turn) Spin();
        else Rest();
    }

    private void Spin()
    {
        _turning = true;
        var visual = ElementCompositionPreview.GetElementVisual(_ring);
        var half = (float)(_ring.Width / 2);
        visual.CenterPoint = new Vector3(half, half, 0);
        var spin = visual.Compositor.CreateScalarKeyFrameAnimation();
        var linear = visual.Compositor.CreateLinearEasingFunction();
        spin.InsertKeyFrame(0, 0);
        spin.InsertKeyFrame(1, 360, linear);
        spin.Duration = TimeSpan.FromSeconds(Turn);
        spin.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("RotationAngleInDegrees", spin);
    }

    private void Rest()
    {
        if (!_turning) return;
        _turning = false;
        var visual = ElementCompositionPreview.GetElementVisual(_ring);
        visual.StopAnimation("RotationAngleInDegrees");
        visual.RotationAngleInDegrees = 0;
    }
}
