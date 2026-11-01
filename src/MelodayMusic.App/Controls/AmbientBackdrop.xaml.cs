using System.ComponentModel;
using MelodayMusic.Core.Artwork;
using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace MelodayMusic.Controls;

public sealed partial class AmbientBackdrop : UserControl
{
    private const int RenderWidth = 960;
    private const int RenderHeight = 540;

    private readonly AmbientService _ambient = App.Services.GetRequiredService<AmbientService>();
    private readonly PlayerService _player = App.Services.GetRequiredService<PlayerService>();
    private readonly ArtworkService _artwork = App.Services.GetRequiredService<ArtworkService>();
    private readonly Crossfade _playing;
    private readonly Crossfade _page;
    private int _playingVersion;
    private int _pageVersion;

    public AmbientBackdrop()
    {
        InitializeComponent();
        _playing = new Crossfade(PlayingA, PlayingB);
        _page = new Crossfade(PageA, PageB);
        ActualThemeChanged += (_, _) => ApplyThemeColours();
        Loaded += (_, _) =>
        {
            ApplyMotion();
            Helpers.Motion.Changed += OnMotionChanged;
            ApplyThemeColours();
            _ambient.PropertyChanged += OnAmbientChanged;
            _player.PropertyChanged += OnPlayerChanged;
            WashLayer.Background = _ambient.Wash;
            ShowFill();
            _ = ShowPageAsync(_ambient.PagePalette);
            _ = ShowHeaderAsync(_ambient.HeaderPalette);
            _ = ShowPlayingAsync(_player.CurrentTrack);
        };
        Unloaded += (_, _) =>
        {
            Helpers.Motion.Changed -= OnMotionChanged;
            _ambient.PropertyChanged -= OnAmbientChanged;
            _player.PropertyChanged -= OnPlayerChanged;
        };
    }

    private void OnMotionChanged(object? sender, EventArgs e) => ApplyMotion();

    private void ApplyMotion()
    {
        PlayingA.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Ambient);
        PlayingB.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Ambient);
        foreach (var layer in new UIElement[] { HeaderShade, PageA, PageB, PageShade, FillLayer })
            layer.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Emphasized);
    }

    private void ApplyThemeColours()
    {
        BaseLayer.Background = new SolidColorBrush(Helpers.ThemeColors.Get("MelodayWindowBrush", ActualTheme, Color.FromArgb(255, 0x12, 0x11, 0x14)));
        DimLayer.Background = new SolidColorBrush(Helpers.ThemeColors.Get("MelodayAmbientDimBrush", ActualTheme, Color.FromArgb(0xA8, 0x10, 0x10, 0x13)));
        ShowFill();
    }

    private void ShowFill()
    {
        PageText.Apply(_ambient.PageFill);
        Accent.SetGround(_ambient.PageFill);
        if (_ambient.PageFill is not { } rgb)
        {
            FillLayer.Opacity = 0;
            return;
        }
        var fill = DominantColor.ForPageFill(rgb, darkText: false);
        FillLayer.Background = new SolidColorBrush(Color.FromArgb(255, (byte)(fill >> 16), (byte)(fill >> 8), (byte)fill));
        FillLayer.Opacity = 1;
    }

    private void OnAmbientChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AmbientService.Wash):
                WashLayer.Background = _ambient.Wash;
                break;
            case nameof(AmbientService.PagePalette):
                _ = ShowPageAsync(_ambient.PagePalette);
                break;
            case nameof(AmbientService.PageFill):
                ShowFill();
                break;
            case nameof(AmbientService.HeaderPalette):
                _ = ShowHeaderAsync(_ambient.HeaderPalette);
                break;
        }
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerService.CurrentTrack)) _ = ShowPlayingAsync(_player.CurrentTrack);
    }

    private async Task ShowPlayingAsync(Track? track)
    {
        var version = ++_playingVersion;
        var art = track?.AlbumThumb ?? track?.Thumb;
        var palette = track?.Palette ?? await _artwork.GetUltraBlurPaletteAsync(art);
        var file = palette is null ? null : await _artwork.GetUltraBlurImageAsync(palette, RenderWidth, RenderHeight);
        if (version == _playingVersion) _playing.Show(file);
    }

    private int _headerVersion;

    private async Task ShowHeaderAsync(ArtworkPalette? palette)
    {
        var version = ++_headerVersion;
        var file = palette is null ? null : await _artwork.GetUltraBlurImageAsync(palette, RenderWidth, RenderHeight);
        if (version != _headerVersion) return;
        HeaderLayer.SourceFile = file;
        HeaderShade.Opacity = file is not null ? 1 : 0;
    }

    private async Task ShowPageAsync(ArtworkPalette? palette)
    {
        var version = ++_pageVersion;
        var file = palette is null ? null : await _artwork.GetUltraBlurImageAsync(palette, RenderWidth, RenderHeight);
        if (version != _pageVersion) return;
        _page.Show(file);
        PageShade.Opacity = file is null || palette is null ? 0 : ShadeFor(ArtworkService.Readable(palette));
    }

    internal static double ShadeFor(ArtworkPalette p)
    {
        var mean = new[] { p.TopLeft, p.TopRight, p.BottomRight, p.BottomLeft }.Average(c => DominantColor.Luminance(c));
        return Math.Clamp((mean - 0.08) * 1.3, 0.08, 0.45);
    }

    private sealed class Crossfade(Image a, Image b)
    {
        private static readonly Dictionary<string, BitmapImage> Decoded = [];
        private Image _front = a;
        private Image _back = b;
        private string? _file;

        public void Show(string? file)
        {
            if (file == _file) return;
            _file = file;
            if (file is null)
            {
                _front.Opacity = 0;
                _back.Opacity = 0;
                return;
            }
            if (Decoded.TryGetValue(file, out var known))
            {
                _back.Source = known;
                Swap();
                return;
            }
            var bitmap = new BitmapImage();
            bitmap.ImageOpened += (_, _) =>
            {
                if (Decoded.Count >= 24) Decoded.Clear();
                Decoded[file] = bitmap;
                if (_file == file && ReferenceEquals(_back.Source, bitmap)) Swap();
            };
            bitmap.ImageFailed += (_, _) =>
            {
                if (_file == file && ReferenceEquals(_back.Source, bitmap)) Swap();
            };
            bitmap.UriSource = new Uri(file);
            _back.Source = bitmap;
        }

        private void Swap()
        {
            (_front, _back) = (_back, _front);
            Canvas.SetZIndex(_front, 1);
            Canvas.SetZIndex(_back, 0);
            _front.Opacity = 1;
            _back.Opacity = 0;
        }
    }
}
