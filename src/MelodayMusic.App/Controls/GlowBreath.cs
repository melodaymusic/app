using System.Numerics;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class GlowBreath : Canvas
{
    private const float Smallest = 0.82f;
    private const float Largest = 1.18f;
    private static readonly double[] Seconds = [9.2, 7.8, 10.4, 8.6, 9.8, 7.4, 10.9, 8.2];

    private readonly List<Ellipse> _washes = [];
    private bool _onScreen = true;
    private bool _breathing;

    public GlowBreath()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            Motion.Changed += OnMotionChanged;
            UpdateBreathing();
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
            DispatcherQueue.TryEnqueue(UpdateBreathing);
        };
    }

    public void Show(IReadOnlyList<uint>? colours, double width, double height, double cornerRadius)
    {
        Rest();
        Children.Clear();
        _washes.Clear();
        if (colours is not { Count: > 0 } || width <= 0 || height <= 0) return;
        Width = width;
        Height = height;
        ClipTo(width, height, cornerRadius);

        var size = height * 1.25;
        for (var i = 0; i < Math.Min(colours.Count, GlowPalette.Points.Length); i++)
        {
            var c = colours[i];
            Color Tint(byte alpha) => Color.FromArgb(alpha, (byte)(c >> 16), (byte)(c >> 8), (byte)c);
            var wash = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop { Color = Tint(200), Offset = 0 },
                        new GradientStop { Color = Tint(90), Offset = 0.5 },
                        new GradientStop { Color = Tint(0), Offset = 1 },
                    },
                },
            };
            SetLeft(wash, GlowPalette.Points[i].X * width - size / 2);
            SetTop(wash, GlowPalette.Points[i].Y * height - size / 2);
            Children.Add(wash);
            _washes.Add(wash);
        }
        UpdateBreathing();
    }

    private void ClipTo(double width, double height, double radius)
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        var shape = visual.Compositor.CreateRoundedRectangleGeometry();
        shape.Size = new Vector2((float)width, (float)height);
        shape.CornerRadius = new Vector2((float)radius);
        visual.Clip = visual.Compositor.CreateGeometricClip(shape);
    }

    private void OnMotionChanged(object? sender, EventArgs e) => UpdateBreathing();

    private void UpdateBreathing()
    {
        var breathe = Motion.IsActive && IsLoaded && _onScreen && _washes.Count > 0;
        if (breathe == _breathing) return;
        if (breathe) Breathe();
        else Rest();
    }

    private void Breathe()
    {
        _breathing = true;
        var (x1, y1, x2, y2) = MelodaySunStyle.Ease;
        for (var i = 0; i < _washes.Count; i++)
        {
            var wash = _washes[i];
            var visual = ElementCompositionPreview.GetElementVisual(wash);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2));
            var half = (float)(wash.Width / 2);
            visual.CenterPoint = new Vector3(half, half, 0);
            var (from, to) = i % 2 == 0 ? (Smallest, Largest) : (Largest, Smallest);
            var breath = compositor.CreateVector3KeyFrameAnimation();
            breath.InsertKeyFrame(0, new Vector3(from, from, 1));
            breath.InsertKeyFrame(0.5f, new Vector3(to, to, 1), ease);
            breath.InsertKeyFrame(1, new Vector3(from, from, 1), ease);
            breath.Duration = TimeSpan.FromSeconds(Seconds[i % Seconds.Length]);
            breath.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", breath);
        }
    }

    private void Rest()
    {
        if (!_breathing) return;
        _breathing = false;
        foreach (var wash in _washes)
        {
            var visual = ElementCompositionPreview.GetElementVisual(wash);
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
        }
    }
}
