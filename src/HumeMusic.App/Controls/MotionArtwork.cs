using HumeMusic.Core.Artwork;
using HumeMusic.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Microsoft.UI.Dispatching;

namespace HumeMusic.Controls;

/// <summary>
/// A station's own artwork: one of Meloday Music's four colour fields, moving (a silent looping video)
/// or, with motion off, its still. The still also sits under the video, so there is never a blank
/// frame. A video only plays while it can be seen: scrolled out of view or minimised, it pauses.
/// </summary>
public sealed partial class MotionArtwork : Grid
{
    /// <summary>The fields, and the hue each stands for (degrees).</summary>
    private static readonly (string Name, double Hue)[] Fields =
    [
        ("sapphire", 222),
        ("lagoon", 170),
        ("green", 135),
        ("crimson", 346),
    ];

    private readonly Image _still = new() { Stretch = Stretch.UniformToFill };
    private MediaPlayerElement? _video;
    private string? _field;
    private bool _onScreen;
    private DispatcherQueueTimer? _release;

    // Video players are costly to make and to put away (each takes the UI thread for a while), so they
    // are made one at a time, only for boards that can be seen, and put away one at a time.
    private static readonly Queue<MotionArtwork> ToBuild = new();
    private static readonly Queue<MotionArtwork> ToRelease = new();
    private static bool _working;
    /// <summary>How long a page left behind keeps its (paused) videos, so coming back is instant.</summary>
    private static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(30);

    public MotionArtwork()
    {
        IsHitTestVisible = false;
        Children.Add(_still);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>The field closest in hue to a palette family ("sapphire", "lagoon", "green" or "crimson").</summary>
    public static string FieldFor(PaletteFamily family)
    {
        var rgb = family.Original;
        var hue = Hue((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        static double Gap(double a, double b) => Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b));
        return Fields.MinBy(f => Gap(f.Hue, hue)).Name;
    }

    public string? Field
    {
        get => _field;
        set
        {
            if (_field == value) return;
            _field = value;
            _still.Source = value is null ? null : new BitmapImage(new Uri($"ms-appx:///Assets/Radio/{value}_still.png"))
            {
                DecodePixelWidth = 480,
                DecodePixelType = DecodePixelType.Logical,
            };
            StopVideo();
            Want();
        }
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
        _video?.MediaPlayer?.Pause();
        if (_video is null) return;
        // Kept a little while, paused, in case the page is come back to.
        _release ??= CreateReleaseTimer();
        _release?.Start();
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

    // Scrolled out of the page or its row: the video rests until it comes back into view.
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
        if (!Motion.IsEnabled) StopVideo();
        else
        {
            Want();
            UpdatePlayback();
        }
    }

    // Nothing plays while the window is minimised or hidden.
    private void OnWindowVisibilityChanged(object sender, WindowVisibilityChangedEventArgs args) => UpdatePlayback();

    private void UpdatePlayback()
    {
        if (_video?.MediaPlayer is not { } player) return;
        // Only while it can be seen and the window is in use; paused, a video costs nothing.
        if (_onScreen && Motion.IsWindowActive && App.MainWindow is not { Visible: false }) player.Play();
        else player.Pause();
    }

    /// <summary>Asks for this board's video, when it can be seen and has none.</summary>
    private void Want()
    {
        if (_video is not null || !_onScreen || !IsLoaded || !Motion.IsEnabled || _field is null || ToBuild.Contains(this)) return;
        ToBuild.Enqueue(this);
        Work();
    }

    /// <summary>Makes or puts away one video per pass of the UI thread, after whatever is drawing.</summary>
    private void Work()
    {
        if (_working || DispatcherQueue is not { } queue) return;
        _working = true;
        if (!queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Step)) _working = false;

        void Step()
        {
            if (ToRelease.Count > 0)
            {
                var gone = ToRelease.Dequeue();
                if (!gone.IsLoaded) gone.StopVideo();
            }
            else if (ToBuild.Count > 0)
            {
                ToBuild.Dequeue().BuildVideo();
            }
            if (ToRelease.Count > 0 || ToBuild.Count > 0) queue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Step);
            else _working = false;
        }
    }

    private void BuildVideo()
    {
        if (_field is not { } field || _video is not null || !_onScreen || !IsLoaded || !Motion.IsEnabled) return;
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Radio", field + ".mp4");
        if (!File.Exists(path)) return;
        var player = new MediaPlayer
        {
            IsLoopingEnabled = true,
            IsMuted = true,
            AutoPlay = false,
            AudioCategory = MediaPlayerAudioCategory.Other,
            Source = MediaSource.CreateFromUri(new Uri(path)),
        };
        // Artwork, not media: it never appears in Windows' media controls or takes their keys.
        player.CommandManager.IsEnabled = false;
        _video = new MediaPlayerElement
        {
            Stretch = Stretch.UniformToFill,
            AreTransportControlsEnabled = false,
            IsHitTestVisible = false,
        };
        _video.SetMediaPlayer(player);
        Children.Add(_video);
        UpdatePlayback();
    }

    private void StopVideo()
    {
        _release?.Stop();
        if (_video is not { } video) return;
        _video = null;
        var player = video.MediaPlayer;
        video.SetMediaPlayer(null);
        Children.Remove(video);
        if (player is null) return;
        (player.Source as IDisposable)?.Dispose();
        player.Dispose();
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
