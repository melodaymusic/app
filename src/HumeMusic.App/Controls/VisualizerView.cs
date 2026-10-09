using System.Diagnostics;
using HumeMusic.Core.Audio;
using HumeMusic.Core.Models;
using HumeMusic.Core.Playback;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.Graphics.DirectX;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// The picture Now Playing draws from the music, on a transparent ground (over the page, or the full-screen stage). Light
/// by design: it listens to the last stretch of what the speakers play (see <see cref="MixEngine.TryReadHeard"/>),
/// analyses it and paints on a thread of its own, never the interface's; and it draws nothing at all while it can't
/// be seen or heard (no style chosen, music stopped and settled, window in the background, motion off). It looks after
/// what its pictures need: the artwork's colours, the song's lyrics and the pictures of the artists. And it keeps itself
/// within what the computer can do: the picture is never drawn at more than about a 1080p screen's pixels (the compositor
/// scales it up), it waits out each frame on a timer that costs no system-wide power, and when frames cost too much it
/// steps down (to 30 frames a second, then a smaller picture), coming back up after a calm stretch; with Windows' energy
/// saver on it stays at 30.
/// </summary>
public sealed partial class VisualizerView : Grid
{
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    // The picture is a composition surface shown by a sprite on top of this element: unlike a swap chain (which Win2D's
    // animated control uses, and which showed as an opaque black square) it keeps its alpha, so the page shows through.
    private readonly object _gate = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private CanvasDevice? _device;
    private CompositionGraphicsDevice? _graphics;
    private CompositionDrawingSurface? _surface;
    private SpriteVisual? _sprite;
    private Windows.Foundation.Size _size;
    private float _scale = 1;
    private volatile float _raster = 1;
    private CancellationTokenSource? _loop;
    private double _lastTotal;
    // Quality steps: 0 is 60 frames a second at full size, 1 is 30, 2 is 30 at three quarters, 3 is 30 at half.
    private const double MostPixels = 2_100_000;
    private static readonly double[] Sizes = [1, 1, 0.75, 0.5];
    private volatile int _quality;
    private volatile bool _saver;
    private volatile bool _resetPainter;
    private long _durationTicks;
    private readonly TextBlock _note = new()
    {
        Text = "Visualizers listen to this computer's own player, so they don't show while casting.",
        TextWrapping = TextWrapping.WrapWholeWords,
        TextAlignment = TextAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 320,
        Visibility = Visibility.Collapsed,
    };
    private readonly float[] _heard = new float[SpectrumAnalyzer.FftSize];
    private readonly BeatTracker _beat = new();
    private DispatcherQueueTimer? _watch;
    private SpectrumAnalyzer? _analyzer;
    private VisualInput? _input;
    private int _rate;
    private IVisualPainter? _painter;
    private VisualizerStyle _painted = VisualizerStyle.Off;
    private volatile VisualPalette _palette = VisualPalette.Default;
    private volatile bool _settled = true;
    private volatile int _style;
    private volatile Core.Models.TrackLyrics? _lyrics;
    private volatile string _title = string.Empty;
    private volatile string _artist = string.Empty;
    private volatile IReadOnlyList<BackdropImage> _backdrops = [];
    private int _trackVersion;
    private TimeSpan _lastPosition;
    private long _lastStamp;

    /// <summary>Raised on the interface's thread when the artwork's colours have been worked out (or lost).</summary>
    public event EventHandler? PaletteChanged;

    /// <summary>Raised when the picture is double-tapped (full screen on, or off).</summary>
    public event EventHandler? DoubleActivated;

    public VisualizerView()
    {
        // Taps are caught here (the picture itself passes none on).
        _note.Foreground = (Brush)Application.Current.Resources["HumeTextSecondaryBrush"];
        Children.Add(_note);
        Children.Add(new Border { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) });
        DoubleTapped += (_, e) =>
        {
            e.Handled = true;
            DoubleActivated?.Invoke(this, EventArgs.Empty);
        };
        SizeChanged += (_, e) => Resize(e.NewSize);
        Loaded += (_, _) =>
        {
            CreateSurface();
            Motion.Changed += OnConditionsChanged;
            _player.PropertyChanged += OnPlayerChanged;
            App.Services.GetRequiredService<LyricsService>().Changed += OnLyricsChanged;
            _watch = DispatcherQueue.CreateTimer();
            _watch.Interval = TimeSpan.FromMilliseconds(250);
            _watch.Tick += (_, _) => Refresh();
            _watch.Start();
            _ = LoadTrackAsync();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            Motion.Changed -= OnConditionsChanged;
            _player.PropertyChanged -= OnPlayerChanged;
            App.Services.GetRequiredService<LyricsService>().Changed -= OnLyricsChanged;
            _watch?.Stop();
            StopLoop();
            DestroySurface();
        };
    }

    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(VisualizerStyle), typeof(VisualizerView), new PropertyMetadata(VisualizerStyle.Off, (d, _) => ((VisualizerView)d).OnStyleChanged()));

    /// <summary>The picture drawn (Off draws nothing and costs nothing).</summary>
    public VisualizerStyle Picture
    {
        get => (VisualizerStyle)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>A very dark shade of the artwork's colour: what the full-screen stage sits on.</summary>
    public Color BackColour => _palette.Back;

    private void OnStyleChanged()
    {
        _style = (int)Picture;
        if (IsLoaded) _ = LoadTrackAsync();
        Refresh();
    }

    private void OnConditionsChanged(object? sender, EventArgs e) => Refresh();

    private void OnPlayerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PlayerService.IsPlaying) or nameof(PlayerService.LiveStation)) DispatcherQueue.TryEnqueue(Refresh);
        else if (e.PropertyName == nameof(PlayerService.CurrentTrack)) DispatcherQueue.TryEnqueue(() => _ = LoadTrackAsync());
    }

    // The listener timed this song's lyrics (or moved them): the words are read again.
    private void OnLyricsChanged(object? sender, string ratingKey) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_player.CurrentTrack?.RatingKey == ratingKey) _ = LoadTrackAsync();
    });

    private static bool UsesWords(VisualizerStyle style) => style is VisualizerStyle.LyricVideo or VisualizerStyle.LyricGlow;

    /// <summary>Gathers what the pictures draw from for the song now playing: its colours, and (for the lyric pictures) its words and the artist's backdrop.</summary>
    private async Task LoadTrackAsync()
    {
        var version = ++_trackVersion;
        var track = _player.CurrentTrack;
        _title = track?.Title ?? string.Empty;
        _artist = track?.DisplayArtist ?? string.Empty;
        _durationTicks = track?.Duration.Ticks ?? 0;
        _lyrics = null;
        _backdrops = [];
        if (track is null || Picture == VisualizerStyle.Off) return;
        var style = Picture;
        var artwork = App.Services.GetRequiredService<ArtworkService>();

        // The artwork's four colours (the same ones the window's own backdrop is made of); one dominant colour when there aren't four.
        var corners = await artwork.GetUltraBlurPaletteAsync(track.DisplayThumb);
        var colour = corners is null ? await artwork.GetDominantColorAsync(track.DisplayThumb) : null;
        if (version != _trackVersion) return;
        _palette = corners is { } four
            ? VisualPalette.FromTones(Core.Artwork.ArtworkTones.From(four))
            : colour is { } rgb
                ? VisualPalette.From(Color.FromArgb(255, (byte)(rgb >> 16 & 0xFF), (byte)(rgb >> 8 & 0xFF), (byte)(rgb & 0xFF)))
                : VisualPalette.Default;
        PaletteChanged?.Invoke(this, EventArgs.Empty);
        if (!UsesWords(style)) return;

        var lyricsTask = App.Services.GetRequiredService<LyricsService>().GetAsync(track);
        // The pictures to cycle behind the words: the artist's backdrop (wide, kept sharp), then the album's cover and the artist's portrait (square, made into a haze).
        var wide = style == VisualizerStyle.LyricVideo && track.Art is { Length: > 0 } art ? LoadPicture(artwork, art, 1280, soft: false) : Task.FromResult<BackdropImage?>(null);
        var cover = style == VisualizerStyle.LyricVideo ? LoadPicture(artwork, track.AlbumThumb ?? track.Thumb, 720, soft: true) : Task.FromResult<BackdropImage?>(null);
        var portrait = style == VisualizerStyle.LyricVideo ? LoadPicture(artwork, track.ArtistThumb, 720, soft: true) : Task.FromResult<BackdropImage?>(null);
        // And the artists the track credits besides its own (found in the library): each one's backdrop and portrait too.
        var featured = style == VisualizerStyle.LyricVideo ? App.Services.GetRequiredService<FeaturedArtistService>().GetAsync(track) : Task.FromResult<IReadOnlyList<Artist>>([]);
        var extra = new List<Task<BackdropImage?>>();
        try
        {
            await Task.WhenAll(lyricsTask, wide, cover, portrait, featured);
            foreach (var artist in featured.Result)
            {
                extra.Add(LoadPicture(artwork, artist.Art, 1280, soft: false));
                extra.Add(LoadPicture(artwork, artist.Thumb, 720, soft: true));
            }
            await Task.WhenAll(extra);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.GetRequiredService<ILogger<VisualizerView>>().LogDebug("A lyric picture's words or backdrop weren't available: {Message}", ex.Message);
        }
        if (version != _trackVersion) return;
        _lyrics = lyricsTask.IsCompletedSuccessfully ? lyricsTask.Result : null;
        var found = new List<BackdropImage>();
        foreach (var task in new[] { wide, cover, portrait }.Concat(extra))
            if (task.IsCompletedSuccessfully && task.Result is { } image && found.All(f => f.Path != image.Path)) found.Add(image);
        _backdrops = found;
        App.Services.GetRequiredService<ILogger<VisualizerView>>().LogInformation("Lyric pictures for {Title}: {Count} ({Featured} featured artists found), lyrics {Lyrics}",
            track.Title, found.Count, featured.IsCompletedSuccessfully ? featured.Result.Count : 0, _lyrics is null ? "none" : _lyrics.IsSynced ? "synced" : "unsynced");
    }

    private static async Task<BackdropImage?> LoadPicture(ArtworkService artwork, string? plexPath, int size, bool soft)
    {
        if (string.IsNullOrEmpty(plexPath)) return null;
        return await artwork.GetLocalPathAsync(plexPath, size) is { } path ? new BackdropImage(path, soft) : null;
    }

    /// <summary>Runs the drawing only while there is something to see.</summary>
    private void Refresh()
    {
        var on = IsLoaded && Picture != VisualizerStyle.Off && Visibility == Visibility.Visible;
        var available = _player.CanVisualize;
        _note.Visibility = on && !available && _player.CurrentTrack is not null ? Visibility.Visible : Visibility.Collapsed;
        var run = on && available && Motion.IsActive && (_player.IsPlaying || !_settled);
        if (run) StartLoop();
        else StopLoop();
    }

    // ------------------------------------------------------------------ the surface

    private void CreateSurface()
    {
        if (_surface is not null) return;
        try
        {
            try
            {
                _device = new CanvasDevice();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // No usable graphics card: Windows' own software renderer does it (slowly, so the steps below bring the cost down).
                App.Services.GetRequiredService<ILogger<VisualizerView>>().LogWarning(ex, "No graphics card for the visualizer; using software drawing");
                _device = new CanvasDevice(forceSoftwareRenderer: true);
            }
            _resetPainter = true;
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            _graphics = CanvasComposition.CreateCompositionGraphicsDevice(compositor, _device);
            _graphics.RenderingDeviceReplaced += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                // The graphics card was reset: begin again with a fresh surface.
                StopLoop();
                DestroySurface();
                CreateSurface();
                Refresh();
            });
            _sprite = compositor.CreateSpriteVisual();
            ElementCompositionPreview.SetElementChildVisual(this, _sprite);
            Resize(new Windows.Foundation.Size(ActualWidth, ActualHeight));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            App.Services.GetRequiredService<ILogger<VisualizerView>>().LogWarning(ex, "The visualizer's surface couldn't be made");
            DestroySurface();
        }
    }

    private void Resize(Windows.Foundation.Size size)
    {
        if (_graphics is null || _sprite is null || size.Width < 1 || size.Height < 1) return;
        _raster = (float)(XamlRoot?.RasterizationScale ?? 1.0);
        lock (_gate)
        {
            _size = size;
            Fit();
            _sprite.Size = new Vector2((float)size.Width, (float)size.Height);
        }
    }

    /// <summary>
    /// Sizes the surface in pixels: the screen's own, but never more than about a 1080p screen's worth (a 4K window is
    /// drawn at half size and scaled up by the compositor, a quarter of the work for a picture that is soft by design),
    /// then smaller again for each quality step down. Called with the lock held.
    /// </summary>
    private void Fit()
    {
        if (_graphics is null || _sprite is null) return;
        var scale = Math.Min(_raster, Math.Sqrt(MostPixels / Math.Max(1, _size.Width * _size.Height))) * Sizes[_quality];
        scale = Math.Max(0.25, scale);
        _scale = (float)scale;
        var pixels = new Windows.Graphics.SizeInt32 { Width = Math.Max(1, (int)Math.Ceiling(_size.Width * scale)), Height = Math.Max(1, (int)Math.Ceiling(_size.Height * scale)) };
        if (_surface is null)
        {
            _surface = _graphics.CreateDrawingSurface(new Windows.Foundation.Size(pixels.Width, pixels.Height),
                DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
            var brush = _graphics.Compositor.CreateSurfaceBrush(_surface);
            brush.Stretch = CompositionStretch.Fill;
            _sprite.Brush = brush;
        }
        else
        {
            _surface.Resize(pixels);
        }
    }

    private void DestroySurface()
    {
        lock (_gate)
        {
            ElementCompositionPreview.SetElementChildVisual(this, null);
            _sprite?.Dispose();
            _surface?.Dispose();
            _graphics?.Dispose();
            _device?.Dispose();
            (_sprite, _surface, _graphics, _device) = (null, null, null, null);
        }
    }

    // ------------------------------------------------------------------ the drawing thread

    // A waitable timer of its own (Windows 10 1803 and later): precise to under a millisecond without raising the timer
    // resolution of the whole system (which would cost every program on the computer power).
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr argument, bool resume);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

    private void StartLoop()
    {
        if (_loop is not null || _surface is null) return;
        var cancel = _loop = new CancellationTokenSource();
        _ = Task.Factory.StartNew(() =>
        {
            try
            {
                RunLoop(cancel.Token);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The drawing stops (the log says why) and stays stopped until the picture is next chosen or the view reloaded.
                App.Services.GetRequiredService<ILogger<VisualizerView>>().LogError(ex, "The visualizer's drawing loop stopped");
            }
        }, cancel.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private void StopLoop()
    {
        _loop?.Cancel();
        _loop = null;
    }

    /// <summary>
    /// Each frame analyses the sound and draws, then waits out what is left of the frame. Every second it looks at what the
    /// frames have been costing and steps the quality down or (after a calm stretch) back up.
    /// </summary>
    private void RunLoop(CancellationToken token)
    {
        var timer = CreateWaitableTimerExW(IntPtr.Zero, null, 0x2, 0x1F0003);
        var log = App.Services.GetRequiredService<ILogger<VisualizerView>>();
        try
        {
            var next = _clock.Elapsed.TotalSeconds;
            _lastTotal = next;
            double cost = 0, judgedAt = next, calm = 0, updateCost = 0, reportedAt = next;
            var frames = 0;
            while (!token.IsCancellationRequested)
            {
                var begin = _clock.Elapsed.TotalSeconds;
                var seconds = Math.Clamp(begin - _lastTotal, 0.001, 0.25);
                _lastTotal = begin;
                OnUpdate(seconds, begin);
                var updated = _clock.Elapsed.TotalSeconds;
                OnDraw(begin);
                var done = _clock.Elapsed.TotalSeconds;
                cost += (done - begin - cost) * 0.1;
                updateCost += (updated - begin - updateCost) * 0.1;
                frames++;
                if (done - reportedAt >= 10)
                {
                    log.LogDebug("Visualizer frames: {Fps:0} per second, {Cost:0.00} ms each ({Analysis:0.00} ms of it analysing), quality {Quality}", frames / (done - reportedAt), cost * 1000, updateCost * 1000, _quality);
                    (reportedAt, frames) = (done, 0);
                }

                if (done - judgedAt >= 1)
                {
                    judgedAt = done;
                    try
                    {
                        _saver = Windows.System.Power.PowerManager.EnergySaverStatus == Windows.System.Power.EnergySaverStatus.On;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                    {
                        _saver = false;
                    }
                    var level = _quality;
                    var budget = 1.0 / (level == 0 ? 60 : 30);
                    if (cost > budget * 0.7 && level < 3)
                    {
                        level++;
                        calm = 0;
                    }
                    else if (level > (_saver ? 1 : 0))
                    {
                        // Back up only after twenty calm seconds, and only if the cost would still be well within the faster frame.
                        calm = cost < 0.3 * (1.0 / (level == 1 ? 60 : 30)) ? calm + 1 : 0;
                        if (calm >= 20)
                        {
                            level--;
                            calm = 0;
                        }
                    }
                    if (_saver && level < 1) level = 1;
                    if (level != _quality)
                    {
                        log.LogInformation("Visualizer quality {From} -> {To} (frames cost {Cost:0.0} ms{Saver})", _quality, level, cost * 1000, _saver ? ", energy saver on" : string.Empty);
                        _quality = level;
                        lock (_gate) Fit();
                        cost = 0;
                    }
                }

                next += 1.0 / (_quality == 0 ? 60 : 30);
                var wait = next - _clock.Elapsed.TotalSeconds;
                if (wait > 0.0005)
                {
                    if (timer != IntPtr.Zero)
                    {
                        var due = -(long)(wait * 1e7);
                        if (SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) WaitForSingleObject(timer, 100);
                        else Thread.Sleep(Math.Max(1, (int)(wait * 1000)));
                    }
                    else Thread.Sleep(Math.Max(1, (int)(wait * 1000)));
                }
                else if (wait < -0.1) next = _clock.Elapsed.TotalSeconds;
            }
        }
        finally
        {
            if (timer != IntPtr.Zero) CloseHandle(timer);
        }
    }

    // ------------------------------------------------------------------ on the drawing thread

    private void OnUpdate(double seconds, double total)
    {
        var heard = _player.TryReadHeard(_heard, out var rate);
        if (_analyzer is null || _rate != rate)
        {
            _rate = rate;
            _analyzer = new SpectrumAnalyzer(rate);
        }
        // Nothing playing: the picture is fed silence, so it falls away rather than freezing.
        _analyzer.Update(heard ? _heard : ReadOnlySpan<float>.Empty, seconds);
        _beat.Update(_analyzer.Bass, total);
        _settled = !heard && _analyzer.Level < 0.003f && _analyzer.Bass < 0.01f && Highest(_analyzer.Bands) < 0.02f;
    }

    private static float Highest(float[] values)
    {
        var top = 0f;
        foreach (var v in values) top = Math.Max(top, v);
        return top;
    }

    private void OnDraw(double total)
    {
        var style = (VisualizerStyle)_style;
        Windows.Foundation.Size size;
        float scale;
        lock (_gate) (size, scale) = (_size, _scale);
        if (_resetPainter)
        {
            // A new graphics device: what the painter made on the old one is no good.
            _resetPainter = false;
            (_painter as IDisposable)?.Dispose();
            _painter = null;
            _painted = VisualizerStyle.Off;
        }
        if (style != _painted)
        {
            _painted = style;
            (_painter as IDisposable)?.Dispose();
            _painter = VisualPainters.For(style);
            App.Services.GetRequiredService<ILogger<VisualizerView>>().LogInformation("Visualizer: {Style} at {Width:0}x{Height:0}", style, size.Width, size.Height);
        }
        if (_painter is null || _analyzer is null) return;

        // Where the music is: the player reports it a few times a second, so it is carried on between reports.
        var now = Stopwatch.GetTimestamp();
        var position = _player.Position;
        if (position != _lastPosition)
        {
            _lastPosition = position;
            _lastStamp = now;
        }
        var carried = _player.IsPlaying ? Math.Min(0.6, (now - _lastStamp) / (double)Stopwatch.Frequency) : 0;

        _input ??= new VisualInput { Sound = _analyzer, Beat = _beat };
        _input.Sound = _analyzer;
        _input.Time = total;
        _input.Palette = _palette;
        _input.Position = position + TimeSpan.FromSeconds(carried);
        _input.Lyrics = _lyrics;
        _input.Duration = TimeSpan.FromTicks(Interlocked.Read(ref _durationTicks));
        _input.Title = _title;
        _input.Artist = _artist;
        _input.Backdrops = _backdrops;
        try
        {
            lock (_gate)
            {
                if (_surface is null) return;
                using var ds = CanvasComposition.CreateDrawingSession(_surface, new Windows.Foundation.Rect(0, 0, Math.Ceiling(size.Width * scale), Math.Ceiling(size.Height * scale)), 96 * scale);
                ds.Clear(Color.FromArgb(0, 0, 0, 0));
                _painter.Draw(ds, _input, (float)size.Width, (float)size.Height);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A picture that can't be drawn is dropped (and the log says why), never allowed to take the app with it.
            (_painter as IDisposable)?.Dispose();
            _painter = null;
            App.Services.GetRequiredService<ILogger<VisualizerView>>().LogWarning(ex, "The {Style} visualizer couldn't be drawn", style);
        }
    }
}
