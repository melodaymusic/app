using System.Numerics;
using HumeMusic.Core.Meloday;
using HumeMusic.Helpers;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// Meloday's cover, live: the part of the day's sky and its sun (see MelodaySun), each ring breathing
/// slowly on its own cycle, with the mix's name set right-aligned in the top right and "Meloday" in
/// the bottom left, exactly as the saved cover lays them out. Drawn in a 1000-unit square scaled to
/// fit. The rings only move while motion is on, the window is in use and the art is on screen.
/// </summary>
public sealed partial class MelodayArt : Grid
{
    private const double Unit = CoverType.Unit;

    private readonly Grid _canvas = new() { Width = Unit, Height = Unit };
    private readonly Grid _sun = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    private readonly List<(Ellipse Ring, SunRing Style)> _rings = [];
    private readonly TextBlock _name = new()
    {
        Width = CoverType.NameWidth,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, CoverType.NameTop, CoverType.NameRight, 0),
        TextAlignment = TextAlignment.Right,
        TextWrapping = TextWrapping.WrapWholeWords,
        MaxLines = CoverType.NameLines,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FontSize = CoverType.NameSize,
        LineHeight = CoverType.NameLine,
        LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        FontWeight = Microsoft.UI.Text.FontWeights.ExtraBold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
    };
    private readonly TextBlock _label = new()
    {
        Text = "Meloday",
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(CoverType.LabelLeft, 0, 0, CoverType.LabelBottom),
        FontSize = CoverType.LabelSize,
        FontWeight = Microsoft.UI.Text.FontWeights.Bold,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
    };
    private bool _onScreen = true;
    private bool _breathing;

    public MelodayArt()
    {
        var font = (FontFamily)Application.Current.Resources["HumeFont"];
        _name.FontFamily = font;
        _label.FontFamily = font;
        _canvas.Children.Add(_sun);
        _canvas.Children.Add(_name);
        _canvas.Children.Add(_label);
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
        // Off screen (scrolled away, in a page left behind) the sun keeps still.
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

    /// <summary>The part of the day ("Morning"), as MelodayPeriods names it.</summary>
    public string? Period
    {
        get => (string?)GetValue(PeriodProperty);
        set => SetValue(PeriodProperty, value);
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(MelodayArt), new PropertyMetadata(null, (d, e) => ((MelodayArt)d).SetTitle(e.NewValue as string)));

    /// <summary>The mix's name ("Meloday for Longing Pop Thursday Morning"); the cover leaves out "Meloday for".</summary>
    public string? Title
    {
        get => (string?)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private void SetTitle(string? title)
    {
        var name = title ?? string.Empty;
        if (name.StartsWith(MelodayNaming.Prefix, StringComparison.OrdinalIgnoreCase)) name = name[MelodayNaming.Prefix.Length..];
        _name.Text = name;
    }

    /// <summary>Lays out the period's sky and sun.</summary>
    private void Build()
    {
        Rest();
        var style = MelodaySun.For(Period);
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

    /// <summary>The rings breathe while motion is on, the window is in use and the art can be seen.</summary>
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
            // Each ring starts part way into its cycle, so they never move in step.
            if (visual.TryGetAnimationController("Scale") is { } controller)
                controller.Progress = (float)(style.Delay / style.Seconds % 1);
        }
    }

    /// <summary>Still: every ring at its natural size.</summary>
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
