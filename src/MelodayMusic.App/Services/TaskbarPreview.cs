using System.ComponentModel;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Dispatching;
using Windows.UI;

namespace MelodayMusic.Services;

/// <summary>
/// The taskbar thumbnail: the current song over its artwork with a progress line. Buttons are in <see cref="TaskbarButtons"/>.
/// </summary>
public sealed class TaskbarPreview
{
    private const int DWMWA_FORCE_ICONIC_REPRESENTATION = 7;
    private const int DWMWA_HAS_ICONIC_BITMAP = 10;
    private const uint WM_DWMSENDICONICTHUMBNAIL = 0x0323;
    private const uint WM_DWMSENDICONICLIVEPREVIEWBITMAP = 0x0326;

    private readonly PlayerService _player;
    private readonly ArtworkService _artwork;
    private readonly ILogger<TaskbarPreview> _log;
    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _tick;
    private IntPtr _hwnd;
    private bool _iconic;
    private int _version;
    private CanvasBitmap? _picture;
    private string? _pictureKey;

    public TaskbarPreview(PlayerService player, ArtworkService artwork, ILogger<TaskbarPreview> log)
    {
        _player = player;
        _artwork = artwork;
        _log = log;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
    }

    public void Attach(IntPtr hwnd)
    {
        if (_hwnd != IntPtr.Zero) return;
        _hwnd = hwnd;
        _player.PropertyChanged += OnPlayerChanged;
        _tick = _dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(5);
        _tick.Tick += (_, _) => Invalidate();
        _ = LoadPictureAsync();
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PlayerService.CurrentTrack):
                _dispatcher.TryEnqueue(() => _ = LoadPictureAsync());
                break;
            case nameof(PlayerService.IsPlaying):
                _dispatcher.TryEnqueue(() =>
                {
                    if (_player.IsPlaying) _tick?.Start();
                    else _tick?.Stop();
                    Invalidate();
                });
                break;
        }
    }

    private async Task LoadPictureAsync()
    {
        var version = ++_version;
        var track = _player.CurrentTrack;
        SetIconic(track is not null);
        if (track is null)
        {
            _tick?.Stop();
            return;
        }
        if (_player.IsPlaying) _tick?.Start();

        var source = track.Art is { Length: > 0 } art ? art : track.DisplayThumb;
        if (source != _pictureKey)
        {
            CanvasBitmap? picture = null;
            try
            {
                if (await _artwork.GetLocalPathAsync(source, 720) is { } path)
                    picture = await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), path);
            }
            catch (Exception ex) when (ex is IOException or COMException or UnauthorizedAccessException or ArgumentException)
            {
                _log.LogDebug("The taskbar picture couldn't be loaded: {Message}", ex.Message);
            }
            if (version != _version)
            {
                picture?.Dispose();
                return;
            }
            _picture?.Dispose();
            _picture = picture;
            _pictureKey = source;
        }
        Invalidate();
    }

    private void SetIconic(bool on)
    {
        if (_hwnd == IntPtr.Zero || on == _iconic) return;
        _iconic = on;
        var value = on ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, DWMWA_FORCE_ICONIC_REPRESENTATION, ref value, sizeof(int));
        DwmSetWindowAttribute(_hwnd, DWMWA_HAS_ICONIC_BITMAP, ref value, sizeof(int));
    }

    private void Invalidate()
    {
        if (_iconic) DwmInvalidateIconicBitmaps(_hwnd);
    }

    /// <summary>Answers Windows asking for the taskbar picture or the peek; true when the message was handled.</summary>
    public bool Handle(IntPtr hwnd, uint msg, IntPtr lParam)
    {
        if (!_iconic || msg is not (WM_DWMSENDICONICTHUMBNAIL or WM_DWMSENDICONICLIVEPREVIEWBITMAP)) return false;
        try
        {
            int width, height;
            if (msg == WM_DWMSENDICONICTHUMBNAIL)
            {
                var most = (long)lParam;
                (width, height) = Fit((int)(most >> 16 & 0xFFFF), (int)(most & 0xFFFF));
            }
            else
            {
                if (!GetClientRect(hwnd, out var client)) return false;
                (width, height) = (client.Right - client.Left, client.Bottom - client.Top);
            }
            if (width < 8 || height < 8) return false;

            var bitmap = Render(width, height);
            try
            {
                if (msg == WM_DWMSENDICONICTHUMBNAIL) DwmSetIconicThumbnail(hwnd, bitmap, 0);
                else DwmSetIconicLivePreviewBitmap(hwnd, bitmap, IntPtr.Zero, 0);
            }
            finally
            {
                DeleteObject(bitmap);
            }
            return true;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException)
        {
            _log.LogDebug("The taskbar picture couldn't be drawn: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>A 16:9 picture as big as Windows allows.</summary>
    internal static (int Width, int Height) Fit(int mostWidth, int mostHeight)
    {
        var width = mostWidth;
        var height = (int)Math.Round(width * 9 / 16.0);
        if (height > mostHeight)
        {
            height = mostHeight;
            width = (int)Math.Round(height * 16 / 9.0);
        }
        return (Math.Max(1, width), Math.Max(1, height));
    }

    private IntPtr Render(int width, int height)
    {
        var duration = _player.Duration.TotalSeconds;
        var done = duration > 0 ? Math.Clamp(_player.Position.TotalSeconds / duration, 0, 1) : 0;
        var track = _player.CurrentTrack;
        using var target = Draw(width, height, _picture, track?.Title ?? string.Empty, track?.DisplayArtist ?? string.Empty, _player.IsPlaying, done);
        var pixels = target.GetPixelBytes();
        var info = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        var bitmap = CreateDIBSection(IntPtr.Zero, ref info, 0, out var bits, IntPtr.Zero, 0);
        if (bitmap == IntPtr.Zero) throw new InvalidOperationException("No bitmap for the taskbar picture.");
        Marshal.Copy(pixels, 0, bits, pixels.Length);
        return bitmap;
    }

    internal static CanvasRenderTarget Draw(int width, int height, CanvasBitmap? backdrop, string song, string artistName, bool playing, double done)
    {
        var target = new CanvasRenderTarget(CanvasDevice.GetSharedDevice(), width, height, 96);
        using (var ds = target.CreateDrawingSession())
        {
            ds.Clear(Color.FromArgb(255, 20, 19, 22));
            if (backdrop is { } picture)
            {
                var size = picture.Size;
                var scale = Math.Max(width / size.Width, height / size.Height);
                var shown = new Windows.Foundation.Size(width / scale, height / scale);
                var from = new Windows.Foundation.Rect((size.Width - shown.Width) / 2, (size.Height - shown.Height) * 0.35, shown.Width, shown.Height);
                ds.DrawImage(picture, new Windows.Foundation.Rect(0, 0, width, height), from);
            }

            var stops = new[]
            {
                new CanvasGradientStop { Position = 0f, Color = Color.FromArgb(0, 0, 0, 0) },
                new CanvasGradientStop { Position = 0.45f, Color = Color.FromArgb(110, 0, 0, 0) },
                new CanvasGradientStop { Position = 1f, Color = Color.FromArgb(240, 0, 0, 0) },
            };
            using (var fade = new CanvasLinearGradientBrush(ds, stops) { StartPoint = new Vector2(0, height * 0.25f), EndPoint = new Vector2(0, height) })
                ds.FillRectangle(0, 0, width, height, fade);

            var pad = height * 0.08f;
            var bar = Math.Max(2f, height * 0.022f);
            var room = width - pad * 2;
            using var titleFormat = Text(height * 0.135f, Microsoft.UI.Text.FontWeights.Bold);
            using var artistFormat = Text(height * 0.095f, Microsoft.UI.Text.FontWeights.SemiBold);
            using var title = new CanvasTextLayout(ds, song, titleFormat, room, height);
            using var artist = new CanvasTextLayout(ds, artistName, artistFormat, room, height);
            var artistTop = height - pad - bar - (float)artist.LayoutBounds.Height;
            var titleTop = artistTop - (float)title.LayoutBounds.Height - height * 0.01f;
            ds.DrawTextLayout(title, pad, titleTop, Color.FromArgb(255, 255, 255, 255));
            ds.DrawTextLayout(artist, pad, artistTop, Color.FromArgb(200, 255, 255, 255));

            if (!playing)
            {
                using var pausedFormat = Text(height * 0.075f, Microsoft.UI.Text.FontWeights.Bold);
                using var paused = new CanvasTextLayout(ds, "PAUSED", pausedFormat, room, height);
                var (w, h) = ((float)paused.LayoutBounds.Width + pad, (float)paused.LayoutBounds.Height + pad * 0.5f);
                ds.FillRoundedRectangle(pad * 0.75f, pad * 0.75f, w, h, h / 2, h / 2, Color.FromArgb(150, 0, 0, 0));
                ds.DrawTextLayout(paused, pad * 0.75f + pad / 2, pad * 0.75f + pad * 0.25f, Color.FromArgb(230, 255, 255, 255));
            }

            ds.FillRectangle(0, height - bar, width, bar, Color.FromArgb(70, 255, 255, 255));
            ds.FillRectangle(0, height - bar, width * (float)done, bar, Color.FromArgb(240, 255, 255, 255));
        }
        return target;
    }

    private static CanvasTextFormat Text(float size, Windows.UI.Text.FontWeight weight) => new()
    {
        FontFamily = MelodayCover.Font,
        FontSize = size,
        FontWeight = weight,
        WordWrapping = CanvasWordWrapping.NoWrap,
        TrimmingGranularity = CanvasTextTrimmingGranularity.Character,
        TrimmingSign = CanvasTrimmingSign.Ellipsis,
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmInvalidateIconicBitmaps(IntPtr hwnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicThumbnail(IntPtr hwnd, IntPtr bitmap, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetIconicLivePreviewBitmap(IntPtr hwnd, IntPtr bitmap, IntPtr client, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER info, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out RECT rect);
}
