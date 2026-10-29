using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.Foundation;

namespace MelodayMusic.Controls;

public enum MixCirclesLayout
{
    Cascade,
    Orbit,
    Float,
}

public sealed partial class MixCircles : Panel
{
    private static readonly (double X, double Y, double D)[] CascadeSlots =
    [
        (0.33, 0.30, 0.44),
        (0.715, 0.57, 0.35),
        (0.325, 0.775, 0.27),
    ];

    public static readonly (double X, double Y, double D)[] FloatSlots =
    [
        (0.33, 0.30, 0.44),
        (0.715, 0.57, 0.35),
        (0.325, 0.775, 0.27),
        (0.725, 0.225, 0.2025),
        (0.155, 0.585, 0.2025),
        (0.64, 0.845, 0.2025),
    ];

    public static readonly (double X, double Y, double D)[] OrbitSlots =
    [
        (0.5, 0.42, 0.54),
        (0.2, 0.29, 0.25),
        (0.82, 0.61, 0.27),
    ];

    public static readonly (double X, double Y, double D)[] StillSlots =
    [
        (0.52, 0.43, 0.56),
        (0.24, 0.30, 0.28),
        (0.78, 0.61, 0.30),
    ];

    public const double RingShare = 0.016;

    public MixCirclesLayout Layout { get; set; } = MixCirclesLayout.Cascade;

    private (double X, double Y, double D)[] Slots => Layout switch
    {
        MixCirclesLayout.Orbit => OrbitSlots,
        MixCirclesLayout.Float => FloatSlots,
        _ => CascadeSlots,
    };

    private static readonly Random Random = new();

    private double _size;
    private bool _floating;
    private bool _onScreen = true;
    private bool _queued;

    public MixCircles()
    {
        PointerMoved += (_, e) =>
        {
            if (_size <= 0) return;
            var at = e.GetCurrentPoint(this).Position;
            _pointer = new Vector2((float)(at.X / _size), (float)(at.Y / _size));
        };
        PointerExited += (_, _) => _pointer = null;
        PointerCanceled += (_, _) => _pointer = null;
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

    private void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var view = args.EffectiveViewport;
        var onScreen = !view.IsEmpty && view.Width > 0 && view.Height > 0
            && view.X < ActualWidth && view.X + view.Width > 0 && view.Y < ActualHeight && view.Y + view.Height > 0;
        if (onScreen == _onScreen) return;
        _onScreen = onScreen;
        QueueUpdate();
    }

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

    private void Rest()
    {
        if (!_floating) return;
        _floating = false;
        Bouncing.Remove(this);
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
            }
        }
    }

    private void StartFloating()
    {
        if (_size <= 0 || _floating) return;
        _floating = true;
        if (Layout == MixCirclesLayout.Float)
        {
            StartBouncing();
            return;
        }
        var amplitude = (float)(_size * 0.015);
        for (var i = 0; i < Children.Count && i < Slots.Length; i++)
        {
            var element = Children[i];
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var drift = compositor.CreateVector3KeyFrameAnimation();
            var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.42f, 0), new Vector2(0.58f, 1));

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

            try
            {
                if (visual.TryGetAnimationController("Translation") is { } controller) controller.Progress = (float)Random.NextDouble();
            }
            catch (Exception)
            {
            }
        }
    }

    private static readonly List<MixCircles> Bouncing = [];
    private static TimeSpan _lastTick;
    private Vector2[] _at = [], _velocity = [], _phase = [], _home = [];
    private float[] _radius = [];
    private double _clock;
    private Vector2? _pointer;

    private void StartBouncing()
    {
        var count = Math.Min(Children.Count, Slots.Length);
        _home = Slots.Take(count).Select(s => new Vector2((float)s.X, (float)s.Y)).ToArray();
        _radius = Slots.Take(count).Select(s => (float)(s.D / 2)).ToArray();
        _at = _home.ToArray();
        _velocity = Enumerable.Range(0, count).Select(_ => new Vector2((float)(Random.NextDouble() - 0.5), (float)(Random.NextDouble() - 0.5)) * 0.01f).ToArray();
        _phase = Enumerable.Range(0, count).Select(_ => new Vector2((float)(Random.NextDouble() * Math.PI * 2), (float)(Random.NextDouble() * Math.PI * 2))).ToArray();
        _clock = Random.NextDouble() * 100;
        for (var i = 0; i < count; i++) ElementCompositionPreview.SetIsTranslationEnabled(Children[i], true);
        if (Bouncing.Count == 0)
        {
            _lastTick = TimeSpan.Zero;
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += OnTick;
        }
        if (!Bouncing.Contains(this)) Bouncing.Add(this);
    }

    private static void OnTick(object? sender, object e)
    {
        if (Bouncing.Count == 0)
        {
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= OnTick;
            return;
        }
        var now = e is Microsoft.UI.Xaml.Media.RenderingEventArgs args ? args.RenderingTime : TimeSpan.Zero;
        var dt = _lastTick == TimeSpan.Zero ? 1 / 60.0 : Math.Clamp((now - _lastTick).TotalSeconds, 0.001, 1 / 20.0);
        _lastTick = now;
        foreach (var board in Bouncing.ToArray()) board.Step((float)dt);
    }

    private void Step(float dt)
    {
        _clock += dt;
        var count = Math.Min(_at.Length, Children.Count);
        const float pull = 3.2f, drag = 2.4f, wander = 0.03f, bounce = 0.35f, gap = 0.01f;
        var t = (float)_clock;
        for (var i = 0; i < count; i++)
        {
            var target = _home[i] + wander * new Vector2(MathF.Sin(t * 0.21f + _phase[i].X), MathF.Cos(t * 0.17f + _phase[i].Y));
            _velocity[i] += (target - _at[i]) * pull * dt;
            if (_pointer is { } pointer && Children[i].Visibility == Visibility.Visible)
            {
                var away = _at[i] - pointer;
                var distance = away.Length();
                var reach = _radius[i] + 0.12f;
                if (distance < reach && distance > 1e-4f)
                    _velocity[i] += away / distance * (1 - distance / reach) * 1.6f * dt * 60 * 0.016f;
            }
            _velocity[i] *= MathF.Max(0, 1 - drag * dt);
            _at[i] += _velocity[i] * dt;
        }
        for (var i = 0; i < count; i++)
        {
            if (Children[i].Visibility != Visibility.Visible) continue;
            for (var j = i + 1; j < count; j++)
            {
                if (Children[j].Visibility != Visibility.Visible) continue;
                var apart = _at[j] - _at[i];
                var distance = apart.Length();
                var nearest = _radius[i] + _radius[j] + gap;
                if (distance >= nearest || distance < 1e-5f) continue;
                var normal = apart / distance;
                float massI = _radius[i] * _radius[i], massJ = _radius[j] * _radius[j];
                var share = massJ / (massI + massJ);
                var overlap = nearest - distance;
                _at[i] -= normal * overlap * share;
                _at[j] += normal * overlap * (1 - share);
                var closing = Vector2.Dot(_velocity[j] - _velocity[i], normal);
                if (closing >= 0) continue;
                var impulse = -(1 + bounce) * closing / (1 / massI + 1 / massJ);
                _velocity[i] -= normal * impulse / massI;
                _velocity[j] += normal * impulse / massJ;
            }
        }
        for (var i = 0; i < count; i++)
        {
            var r = _radius[i];
            if (_at[i].X < r) { _at[i].X = r; _velocity[i].X = MathF.Abs(_velocity[i].X) * bounce; }
            if (_at[i].X > 1 - r) { _at[i].X = 1 - r; _velocity[i].X = -MathF.Abs(_velocity[i].X) * bounce; }
            if (_at[i].Y < r) { _at[i].Y = r; _velocity[i].Y = MathF.Abs(_velocity[i].Y) * bounce; }
            if (_at[i].Y > 1 - r) { _at[i].Y = 1 - r; _velocity[i].Y = -MathF.Abs(_velocity[i].Y) * bounce; }
            var offset = (_at[i] - _home[i]) * (float)_size;
            ElementCompositionPreview.GetElementVisual(Children[i]).Properties.InsertVector3("Translation", new Vector3(offset.X, offset.Y, 0));
        }
    }
}
