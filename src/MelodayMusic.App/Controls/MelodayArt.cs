using System.Numerics;
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

public sealed partial class MelodayArt : Grid
{
    private const double Unit = CoverType.Unit;

    private readonly Grid _canvas = new() { Width = Unit, Height = Unit };
    private readonly Grid _sun = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly List<(Ellipse Ring, SunRing Style)> _rings = [];
    private readonly CoverTextLayer _text = new() { Label = "Meloday" };
    private readonly CoverSymbolLayer _symbol = new();
    private bool _onScreen = true;
    private bool _breathing;

    public MelodayArt()
    {
        _canvas.Children.Add(_sun);
        _canvas.Children.Add(_text);
        _canvas.Children.Add(_symbol);
        Children.Add(new Viewbox { Stretch = Stretch.UniformToFill, Child = _canvas });
        Build();

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

    public static readonly DependencyProperty PeriodProperty = DependencyProperty.Register(
        nameof(Period), typeof(string), typeof(MelodayArt), new PropertyMetadata(null, (d, _) => ((MelodayArt)d).Build()));

    public string? Period
    {
        get => (string?)GetValue(PeriodProperty);
        set => SetValue(PeriodProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(MelodayArt), new PropertyMetadata(null, (d, e) => ((MelodayArt)d).SetTitle(e.NewValue as string)));

    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void SetTitle(string? title)
    {
        var name = title ?? string.Empty;
        if (name.StartsWith(MelodayNaming.Prefix, StringComparison.OrdinalIgnoreCase)) name = name[MelodayNaming.Prefix.Length..];
        _text.Title = name;
    }

    private void Build()
    {
        Rest();
        var style = MelodaySun.For(Period);
        _symbol.SetGround(style.Background);
        _canvas.Background = new SolidColorBrush(ToColor(style.Background));
        var size = MelodaySunStyle.SunSize * Unit;
        _sun.Width = _sun.Height = size;
        _sun.Margin = new Thickness(style.X * Unit - size / 2, style.Y * Unit - size / 2, 0, 0);
        _sun.Children.Clear();
        _rings.Clear();
        foreach (var ring in style.Rings)
        {
            var ellipse = new Ellipse
            {
                Width = size * ring.Size,
                Height = size * ring.Size,
                Fill = new SolidColorBrush(ToColor(ring.Colour)),
                Opacity = ring.Opacity,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _rings.Add((ellipse, ring));
            _sun.Children.Add(ellipse);
        }
        if (IsLoaded) UpdateBreathing();
    }

    private void OnMotionChanged(object? sender, EventArgs e) => UpdateBreathing();

    private void UpdateBreathing()
    {
        var breathe = Motion.IsActive && IsLoaded && _onScreen;
        if (breathe == _breathing) return;
        if (breathe) Breathe();
        else Rest();
    }

    private void Breathe()
    {
        _breathing = true;
        var (x1, y1, x2, y2) = MelodaySunStyle.Ease;
        foreach (var (ring, style) in _rings)
        {
            var visual = ElementCompositionPreview.GetElementVisual(ring);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2((float)x1, (float)y1), new Vector2((float)x2, (float)y2));
            var half = (float)(ring.Width / 2);
            visual.CenterPoint = new Vector3(half, half, 0);
            var from = new Vector3((float)style.From, (float)style.From, 1);
            var to = new Vector3((float)style.To, (float)style.To, 1);
            var breath = compositor.CreateVector3KeyFrameAnimation();
            breath.InsertKeyFrame(0, from);
            breath.InsertKeyFrame(0.5f, to, ease);
            breath.InsertKeyFrame(1, from, ease);
            breath.Duration = TimeSpan.FromSeconds(style.Seconds);
            breath.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Scale", breath);
            if (visual.TryGetAnimationController("Scale") is { } controller)
                controller.Progress = (float)(style.Delay / style.Seconds % 1);
        }
    }

    private void Rest()
    {
        if (!_breathing) return;
        _breathing = false;
        foreach (var (ring, _) in _rings)
        {
            var visual = ElementCompositionPreview.GetElementVisual(ring);
            visual.StopAnimation("Scale");
            visual.Scale = Vector3.One;
        }
    }

    private static Color ToColor(uint rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
