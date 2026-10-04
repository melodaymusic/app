using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace HumeMusic.Controls;

/// <summary>
/// The artwork of an artist mix: up to three round artist photos, each a size smaller than the last
/// (large top left, medium right, small bottom left), gently drifting around their places.
/// Positions are fractions of the square, so it scales with the card and never feeds back into layout.
/// </summary>
public sealed partial class MixCircles : Panel
{
    // Centre x, centre y and diameter of each circle, as fractions of the square.
    private static readonly (double X, double Y, double D)[] Slots =
    [
        (0.33, 0.30, 0.44),
        (0.715, 0.57, 0.35),
        (0.325, 0.775, 0.27),
    ];

    // Every circle on every card gets its own random drift, so nothing moves in step.
    private static readonly Random Random = new();

    private double _size;
    private bool _floating;
    private bool _onScreen = true;
    private bool _queued;

    public MixCircles()
    {
        Loaded += (_, _) =>
        {
            Helpers.Motion.Changed += OnMotionChanged;
            EffectiveViewportChanged += OnViewportChanged;
            QueueUpdate();
        };
        Unloaded += (_, _) =>
        {
            Helpers.Motion.Changed -= OnMotionChanged;
            EffectiveViewportChanged -= OnViewportChanged;
            Rest();
        };
    }

    private void OnMotionChanged(object? sender, EventArgs e) => QueueUpdate();

    // Drifting keeps the compositor drawing every frame: only while the circles can be seen.
    private void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var view = args.EffectiveViewport;
        var onScreen = !view.IsEmpty && view.Width > 0 && view.Height > 0
            && view.X < ActualWidth && view.X + view.Width > 0 && view.Y < ActualHeight && view.Y + view.Height > 0;
        if (onScreen == _onScreen) return;
        _onScreen = onScreen;
        QueueUpdate();
    }

    /// <summary>Starts or stops the drift after layout, never from inside it.</summary>
    private void QueueUpdate()
    {
        if (_queued) return;
        _queued = DispatcherQueue?.TryEnqueue(() =>
        {
            _queued = false;
            if (Helpers.Motion.IsActive && _onScreen && IsLoaded) StartFloating();
            else Rest();
        }) ?? false;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        for (var i = 0; i < Children.Count; i++)
        {
            var d = i < Slots.Length ? Math.Round(size * Slots[i].D) : 0;
            Children[i].Measure(new Size(d, d));
        }
        return new Size(size, size);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = finalSize.Width;
        for (var i = 0; i < Children.Count; i++)
        {
            if (i >= Slots.Length)
            {
                Children[i].Arrange(new Rect(0, 0, 0, 0));
                continue;
            }
            var (x, y, dFraction) = Slots[i];
            var d = Math.Round(size * dFraction);
            Children[i].Arrange(new Rect(Math.Round(size * x - d / 2), Math.Round(size * y - d / 2), d, d));
        }
        if (Math.Abs(size - _size) > 0.5)
        {
            _size = size;
            _floating = false;
            QueueUpdate();
        }
        return finalSize;
    }

    /// <summary>Still: every circle back in its place (only circles that were drifting have an animation to stop).</summary>
    private void Rest()
    {
        if (!_floating) return;
        _floating = false;
        for (var i = 0; i < Children.Count && i < Slots.Length; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(Children[i]);
            try
            {
                visual.StopAnimation("Translation");
                visual.Properties.InsertVector3("Translation", Vector3.Zero);
            }
            catch (ArgumentException)
            {
                // Translation was never enabled on this circle: it is already at rest.
            }
        }
    }

    /// <summary>
    /// A very slow, subtle drift on the compositor (no layout work): each circle wanders through a few
    /// random points within about 1.5% of the card, over 20-34 seconds per loop, starting at a random
    /// point of its loop, so no two circles or cards move together.
    /// </summary>
    private void StartFloating()
    {
        if (_size <= 0 || _floating) return;
        _floating = true;
        var amplitude = (float)(_size * 0.015);
        for (var i = 0; i < Children.Count && i < Slots.Length; i++)
        {
            var element = Children[i];
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var drift = compositor.CreateVector3KeyFrameAnimation();
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));

            // Four random points around the circle's place; the loop returns to the first.
            var points = Enumerable.Range(0, 4).Select(_ =>
            {
                var angle = Random.NextDouble() * Math.PI * 2;
                var reach = 0.4 + Random.NextDouble() * 0.6;
                return new Vector3((float)(Math.Cos(angle) * reach), (float)(Math.Sin(angle) * reach), 0) * amplitude;
            }).ToList();
            for (var k = 0; k < points.Count; k++) drift.InsertKeyFrame(k / (float)points.Count, points[k], ease);
            drift.InsertKeyFrame(1f, points[0], ease);

            drift.Duration = TimeSpan.FromSeconds(20 + Random.NextDouble() * 14);
            drift.IterationBehavior = AnimationIterationBehavior.Forever;
            visual.StartAnimation("Translation", drift);

            // Start somewhere in the loop rather than at its beginning.
            try
            {
                if (visual.TryGetAnimationController("Translation") is { } controller) controller.Progress = (float)Random.NextDouble();
            }
            catch (Exception)
            {
                // A random start is a nicety; the drift runs either way.
            }
        }
    }
}
