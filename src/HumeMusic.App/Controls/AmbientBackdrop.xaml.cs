using System.ComponentModel;
using HumeMusic.Core.Artwork;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>The ambient colour layers behind the whole window (see the XAML for the stack).</summary>
public sealed partial class AmbientBackdrop : UserControl
{
    // UltraBlur is a soft gradient, so a small render scaled up is indistinguishable from a large one.
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

    /// <summary>Colours crossfade as pages and tracks change; with motion off they switch at once.</summary>
    private void ApplyMotion()
    {
        PlayingA.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Ambient);
        PlayingB.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Ambient);
        foreach (var layer in new UIElement[] { HeaderShade, PageA, PageB, PageShade })
            layer.OpacityTransition = Helpers.Motion.Scalar(Helpers.Motion.Emphasized);
    }

    // Set from the element's actual theme: this control lives in the window's own markup, whose theme
    // resources are not re-resolved when the listener switches between light and dark.
    private void ApplyThemeColours()
    {
        BaseLayer.Background = new SolidColorBrush(Helpers.ThemeColors.Get("HumeWindowBrush", ActualTheme, Color.FromArgb(255, 0x12, 0x11, 0x14)));
        DimLayer.Background = new SolidColorBrush(Helpers.ThemeColors.Get("HumeAmbientDimBrush", ActualTheme, Color.FromArgb(0xA8, 0x10, 0x10, 0x13)));
        // In light mode the header colour is softened so dark text stays readable on it.
        HeaderLayer.Opacity = ActualTheme == ElementTheme.Light ? 0.5 : 1;
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
        HeaderShade.Opacity = file is not null && ActualTheme != ElementTheme.Light ? 1 : 0;
    }

    private async Task ShowPageAsync(ArtworkPalette? palette)
    {
        var version = ++_pageVersion;
        var file = palette is null ? null : await _artwork.GetUltraBlurImageAsync(palette, RenderWidth, RenderHeight);
        if (version != _pageVersion) return;
        _page.Show(file);
        PageShade.Opacity = file is null || palette is null ? 0 : ShadeFor(ArtworkService.Readable(palette));
    }

    /// <summary>
    /// Just enough black over the UltraBlur for white text to read: a dark palette needs almost
    /// none, a bright one gets more, so the colours stay as rich as legibility allows.
    /// </summary>
    internal static double ShadeFor(ArtworkPalette p)
    {
        var mean = new[] { p.TopLeft, p.TopRight, p.BottomRight, p.BottomLeft }.Average(c => DominantColor.Luminance(c));
        return Math.Clamp((mean - 0.08) * 1.3, 0.08, 0.45);
    }

    /// <summary>Two stacked images: the new picture fades in over the old one, then the old one hides.</summary>
    private sealed class Crossfade(Image a, Image b)
    {
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
            (_front, _back) = (_back, _front);
            _front.Source = new BitmapImage(new Uri(file));
            Canvas.SetZIndex(_front, 1);
            Canvas.SetZIndex(_back, 0);
            _front.Opacity = 1;
            _back.Opacity = 0;
        }
    }
}
