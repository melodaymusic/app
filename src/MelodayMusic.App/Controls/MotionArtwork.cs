using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Radio;
using MelodayMusic.Helpers;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

/// <summary>A live station's artwork: its colour field with the M-wave moving across it.</summary>
public sealed partial class MotionArtwork : Grid
{
    private static readonly (string Name, double Hue, uint Ground)[] Fields =
    [
        ("sapphire", 222, 0x1442AC),
        ("lagoon", 170, 0x18A088),
        ("green", 135, 0x2E984B),
        ("crimson", 346, 0xC8163E),
    ];

    private const uint NeutralGround = 0x777777;
    private const double Resting = 0.8;
    private const double MostSide = 640;

    private string? _field;
    private uint? _tintColour;
    private bool _onScreen;
    private DispatcherQueueTimer? _release;
    private SpriteVisual? _sprite;
    private CompositionDrawingSurface? _surface;
    private float _scale = 1;
    private double _offset;
    private double _seconds = Resting;

    private static readonly Queue<MotionArtwork> ToBuild = new();
    private static readonly Queue<MotionArtwork> ToRelease = new();
    private static bool _working;
    private static readonly TimeSpan KeepFor = TimeSpan.FromMinutes(2);

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly HashSet<MotionArtwork> Built = [];
    private static readonly HashSet<MotionArtwork> Moving = [];
    private static CanvasDevice? _device;
    private static CompositionGraphicsDevice? _graphics;
    private static DispatcherQueue? _dispatcher;
    private static DispatcherQueueTimer? _frames;
    private static bool _unavailable;

    public MotionArtwork()
    {
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public static string FieldFor(PaletteFamily family)
    {
        var rgb = family.Original;
        var hue = Hue((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        static double Gap(double a, double b) => Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));
        return Fields.MinBy(f => Gap(f.Hue, hue)).Name;
    }

    /// <summary>The colour a field is laid on; a station's own colour, when it has one, takes its place.</summary>
    public static uint GroundFor(string? field, uint? tint = null)
    {
        if (tint is { } own) return own;
        foreach (var f in Fields)
            if (f.Name == field) return f.Ground;
        return NeutralGround;
    }

    public uint? Tint
    {
        get => _tintColour;
        set
        {
            if (_tintColour == value) return;
            _tintColour = value;
            Recolour();
        }
    }

    public string? Field
    {
        get => _field;
        set
        {
            if (_field == value) return;
            _field = value;
            if (value is null) ReleaseSurface();
            Recolour();
            Want();
        }
    }

    private uint Ground => GroundFor(_field, _tintColour);

    private void Recolour()
    {
        Background = _field is null ? null : new SolidColorBrush(WaveField.Rgb(Ground));
        _offset = (Ground * 2654435761UL >> 16) % 6000 / 1000.0;
        _seconds = Resting + _offset;
        Redraw();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _release?.Stop();
        Motion.Changed += OnMotionChanged;
        EffectiveViewportChanged += OnViewportChanged;
        if (App.MainWindow is { } window) window.VisibilityChanged += OnWindowVisibilityChanged;
        UpdatePlayback();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        Motion.Changed -= OnMotionChanged;
        EffectiveViewportChanged -= OnViewportChanged;
        if (App.MainWindow is { } window) window.VisibilityChanged -= OnWindowVisibilityChanged;
        _onScreen = false;
        UpdatePlayback();
        if (_surface is null) return;
        _release ??= CreateReleaseTimer();
        _release?.Start();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_surface is null) Want();
        else
        {
            Fit();
            Redraw();
        }
    }

    private DispatcherQueueTimer? CreateReleaseTimer()
    {
        if (DispatcherQueue is not { } queue) return null;
        var timer = queue.CreateTimer();
        timer.Interval = KeepFor;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            if (IsLoaded) return;
            ToRelease.Enqueue(this);
            Work();
        };
        return timer;
    }

    private void OnViewportChanged(FrameworkElement sender, EffectiveViewportChangedEventArgs args)
    {
        var view = args.EffectiveViewport;
        var onScreen = !view.IsEmpty && view.Width > 0 && view.Height > 0
            && view.X < ActualWidth && view.X + view.Width > 0 && view.Y < ActualHeight && view.Y + view.Height > 0;
        if (onScreen == _onScreen) return;
        _onScreen = onScreen;
        if (onScreen) Want();
        UpdatePlayback();
    }

    private void OnMotionChanged(object? sender, EventArgs e)
    {
        if (!Motion.IsEnabled)
        {
            _seconds = Resting + _offset;
            Redraw();
        }
        UpdatePlayback();
    }

    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => UpdatePlayback();

    private void UpdatePlayback()
    {
        if (_surface is not null && _onScreen && IsLoaded && Motion.IsActive && App.MainWindow is not { Visible: false }) Moving.Add(this);
        else Moving.Remove(this);
        Pulse();
    }

    private static void Pulse()
    {
        if (Moving.Count == 0)
        {
            _frames?.Stop();
            return;
        }
        if (_frames is null)
        {
            if (_dispatcher is null) return;
            _frames = _dispatcher.CreateTimer();
            _frames.Interval = TimeSpan.FromMilliseconds(33);
            _frames.Tick += (_, _) =>
            {
                var now = Clock.Elapsed.TotalSeconds;
                foreach (var artwork in Moving) artwork.Draw(now + artwork._offset);
            };
        }
        if (!_frames.IsRunning) _frames.Start();
    }

    private void Want()
    {
        if (_surface is not null || !_onScreen || !IsLoaded || _field is null || _unavailable || ToBuild.Contains(this)) return;
        ToBuild.Enqueue(this);
        Work();
    }

    private void Work()
    {
        if (_working || DispatcherQueue is not { } queue) return;
        _working = true;
        if (!queue.TryEnqueue(DispatcherQueuePriority.Low, Step)) _working = false;

        void Step()
        {
            if (ToRelease.Count > 0)
            {
                var gone = ToRelease.Dequeue();
                if (!gone.IsLoaded) gone.ReleaseSurface();
            }
            else if (ToBuild.Count > 0)
            {
                ToBuild.Dequeue().BuildSurface();
            }
            if (ToRelease.Count > 0 || ToBuild.Count > 0) queue.TryEnqueue(DispatcherQueuePriority.Low, Step);
            else _working = false;
        }
    }

    private void BuildSurface()
    {
        if (_field is null || _surface is not null || !_onScreen || !IsLoaded || ActualWidth < 1 || ActualHeight < 1) return;
        var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
        if (!EnsureGraphics(compositor, DispatcherQueue) || _graphics is null) return;
        try
        {
            _sprite = compositor.CreateSpriteVisual();
            ElementCompositionPreview.SetElementChildVisual(this, _sprite);
            _surface = _graphics.CreateDrawingSurface(Pixels(), DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            var brush = compositor.CreateSurfaceBrush(_surface);
            brush.Stretch = CompositionStretch.Fill;
            _sprite.Brush = brush;
            _sprite.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
            Built.Add(this);
            Redraw();
            UpdatePlayback();
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException)
        {
            ReleaseSurface();
        }
    }

    private Windows.Foundation.Size Pixels()
    {
        var raster = XamlRoot?.RasterizationScale ?? 1.0;
        _scale = (float)Math.Max(0.25, Math.Min(raster, MostSide / Math.Max(ActualWidth, ActualHeight)));
        return new Windows.Foundation.Size(Math.Max(1, Math.Ceiling(ActualWidth * _scale)), Math.Max(1, Math.Ceiling(ActualHeight * _scale)));
    }

    private void Fit()
    {
        if (_surface is null || _sprite is null || ActualWidth < 1 || ActualHeight < 1) return;
        try
        {
            var size = Pixels();
            _surface.Resize(new Windows.Graphics.SizeInt32 { Width = (int)size.Width, Height = (int)size.Height });
            _sprite.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException) { }
    }

    private void Redraw() => Draw(_seconds);

    private void Draw(double seconds)
    {
        if (_surface is null || _field is null || ActualWidth < 1 || ActualHeight < 1) return;
        _seconds = seconds;
        try
        {
            using var ds = CanvasComposition.CreateDrawingSession(_surface,
                new Windows.Foundation.Rect(0, 0, Math.Ceiling(ActualWidth * _scale), Math.Ceiling(ActualHeight * _scale)), 96 * _scale);
            WaveField.Draw(ds, (float)ActualWidth, (float)ActualHeight, StationWave.Phase(seconds), Ground);
        }
        catch (Exception ex) when (ex is COMException or ObjectDisposedException) { }
    }

    private void ReleaseSurface()
    {
        _release?.Stop();
        Moving.Remove(this);
        Built.Remove(this);
        if (_sprite is not null)
        {
            ElementCompositionPreview.SetElementChildVisual(this, null);
            (_sprite.Brush as IDisposable)?.Dispose();
            _sprite.Dispose();
        }
        _surface?.Dispose();
        (_sprite, _surface) = (null, null);
        Pulse();
    }

    private static bool EnsureGraphics(Compositor compositor, DispatcherQueue queue)
    {
        if (_graphics is not null) return true;
        if (_unavailable) return false;
        try
        {
            try
            {
                _device = new CanvasDevice();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _device = new CanvasDevice(forceSoftwareRenderer: true);
            }
            _dispatcher = queue;
            _graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _device);
            _graphics.RenderingDeviceReplaced += (_, _) => _dispatcher?.TryEnqueue(Rebuild);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _graphics?.Dispose();
            _device?.Dispose();
            (_graphics, _device) = (null, null);
            _unavailable = true;
            return false;
        }
    }

    private static void Rebuild()
    {
        var artworks = Built.ToList();
        foreach (var artwork in artworks) artwork.ReleaseSurface();
        _graphics?.Dispose();
        _device?.Dispose();
        (_graphics, _device) = (null, null);
        foreach (var artwork in artworks) artwork.Want();
    }

    private static double Hue(uint r, uint g, uint b)
    {
        double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var d = max - min;
        if (d < 1e-6) return 0;
        var h = max == rf ? (gf - bf) / d % 6 : max == gf ? (bf - rf) / d + 2 : (rf - gf) / d + 4;
        return (h * 60 + 360) % 360;
    }
}
