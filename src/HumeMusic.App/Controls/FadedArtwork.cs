using System.Numerics;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// Artwork whose edges fade to transparent (not to a colour), so it dissolves into whatever is
/// behind it, such as the UltraBlur backdrop. Drawn with a composition mask: the image is filled
/// uniform-to-fill and multiplied by a horizontal and a vertical alpha gradient.
/// </summary>
public sealed partial class FadedArtwork : Grid
{
    private Compositor? _compositor;
    private SpriteVisual? _sprite;
    private CompositionSurfaceBrush? _surface;
    private CompositionLinearGradientBrush? _horizontal;
    private CompositionLinearGradientBrush? _vertical;
    private SpriteVisual? _inner;
    private CompositionVisualSurface? _innerSurface;
    private LoadedImageSurface? _image;
    private int _version;

    public FadedArtwork()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) =>
        {
            if (EnsureVisual()) _ = LoadAsync();
        };
        // Composition sizes only: nothing here changes layout.
        SizeChanged += (_, _) => UpdateSize();
        Unloaded += (_, _) =>
        {
            _image?.Dispose();
            _image = null;
        };
    }

    /// <summary>Builds the masked image visual on first load (composition needs the element's visual).</summary>
    private bool EnsureVisual()
    {
        if (_sprite is not null) return true;
        try
        {
            var compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;
            _surface = compositor.CreateSurfaceBrush();
            _surface.Stretch = CompositionStretch.UniformToFill;
            _surface.HorizontalAlignmentRatio = 0.5f;
            _surface.VerticalAlignmentRatio = 0.3f; // portraits: keep faces in frame

            _horizontal = compositor.CreateLinearGradientBrush();
            _horizontal.StartPoint = new Vector2(0, 0.5f);
            _horizontal.EndPoint = new Vector2(1, 0.5f);
            _vertical = compositor.CreateLinearGradientBrush();
            _vertical.StartPoint = new Vector2(0.5f, 0);
            _vertical.EndPoint = new Vector2(0.5f, 1);

            // Mask brushes can't be nested, so the horizontally faded image is rendered to a visual
            // surface, and that surface is faded vertically.
            var horizontallyFaded = compositor.CreateMaskBrush();
            horizontallyFaded.Source = _surface;
            horizontallyFaded.Mask = _horizontal;
            _inner = compositor.CreateSpriteVisual();
            _inner.Brush = horizontallyFaded;
            _innerSurface = compositor.CreateVisualSurface();
            _innerSurface.SourceVisual = _inner;

            var outer = compositor.CreateMaskBrush();
            outer.Source = compositor.CreateSurfaceBrush(_innerSurface);
            outer.Mask = _vertical;

            var sprite = compositor.CreateSpriteVisual();
            sprite.Brush = outer;
            sprite.RelativeSizeAdjustment = Vector2.One; // always the element's size, without layout involvement
            sprite.Opacity = 0;
            ElementCompositionPreview.SetElementChildVisual(this, sprite);
            _compositor = compositor;
            _sprite = sprite;
            UpdateMasks();
            UpdateSize();
            return true;
        }
        catch (Exception ex)
        {
            App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<FadedArtwork>>()
                .LogWarning(ex, "Faded artwork unavailable");
            return false;
        }
    }

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(FadedArtwork), new PropertyMetadata(null, (d, e) => _ = ((FadedArtwork)d).LoadAsync()));

    public static readonly DependencyProperty SourceFileProperty = DependencyProperty.Register(
        nameof(SourceFile), typeof(string), typeof(FadedArtwork), new PropertyMetadata(null, (d, e) => _ = ((FadedArtwork)d).LoadAsync()));

    /// <summary>A local image file, used instead of <see cref="SourcePath"/> when set.</summary>
    public string? SourceFile
    {
        get => (string?)GetValue(SourceFileProperty);
        set => SetValue(SourceFileProperty, value);
    }

    /// <summary>Plex artwork path.</summary>
    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public static readonly DependencyProperty FadeLeftProperty = DependencyProperty.Register(
        nameof(FadeLeft), typeof(double), typeof(FadedArtwork), new PropertyMetadata(0.45, (d, _) => ((FadedArtwork)d).UpdateMasks()));

    /// <summary>Fraction of the width, from the left edge, over which the image fades in (0 = none).</summary>
    public double FadeLeft
    {
        get => (double)GetValue(FadeLeftProperty);
        set => SetValue(FadeLeftProperty, value);
    }

    public static readonly DependencyProperty FadeBottomProperty = DependencyProperty.Register(
        nameof(FadeBottom), typeof(double), typeof(FadedArtwork), new PropertyMetadata(0.4, (d, _) => ((FadedArtwork)d).UpdateMasks()));

    /// <summary>Fraction of the height, from the bottom edge, over which the image fades out.</summary>
    public double FadeBottom
    {
        get => (double)GetValue(FadeBottomProperty);
        set => SetValue(FadeBottomProperty, value);
    }

    public static readonly DependencyProperty FadeTopProperty = DependencyProperty.Register(
        nameof(FadeTop), typeof(double), typeof(FadedArtwork), new PropertyMetadata(0.12, (d, _) => ((FadedArtwork)d).UpdateMasks()));

    /// <summary>Fraction of the height, from the top edge, over which the image fades in.</summary>
    public double FadeTop
    {
        get => (double)GetValue(FadeTopProperty);
        set => SetValue(FadeTopProperty, value);
    }

    private void UpdateSize()
    {
        if (_inner is null || _innerSurface is null) return;
        var size = new Vector2((float)ActualWidth, (float)ActualHeight);
        _inner.Size = size;
        _innerSurface.SourceSize = size;
    }

    private void UpdateMasks()
    {
        if (_compositor is null || _horizontal is null || _vertical is null) return;
        static Color Alpha(double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), 255, 255, 255);

        _horizontal.ColorStops.Clear();
        var left = (float)Math.Clamp(FadeLeft, 0, 0.95);
        // Eased stops read as a soft dissolve rather than a visible linear ramp.
        _horizontal.ColorStops.Add(_compositor.CreateColorGradientStop(0, Alpha(left > 0 ? 0 : 1)));
        _horizontal.ColorStops.Add(_compositor.CreateColorGradientStop(left * 0.5f, Alpha(left > 0 ? 0.35 : 1)));
        _horizontal.ColorStops.Add(_compositor.CreateColorGradientStop(Math.Max(left, 0.001f), Alpha(1)));
        _horizontal.ColorStops.Add(_compositor.CreateColorGradientStop(1, Alpha(1)));

        _vertical.ColorStops.Clear();
        var top = (float)Math.Clamp(FadeTop, 0, 0.45);
        var bottom = (float)Math.Clamp(FadeBottom, 0, 0.9);
        _vertical.ColorStops.Add(_compositor.CreateColorGradientStop(0, Alpha(top > 0 ? 0 : 1)));
        _vertical.ColorStops.Add(_compositor.CreateColorGradientStop(Math.Max(top, 0.001f), Alpha(1)));
        _vertical.ColorStops.Add(_compositor.CreateColorGradientStop(1 - bottom, Alpha(1)));
        _vertical.ColorStops.Add(_compositor.CreateColorGradientStop(1 - bottom * 0.45f, Alpha(0.35)));
        _vertical.ColorStops.Add(_compositor.CreateColorGradientStop(1, Alpha(0)));
    }

    private async Task LoadAsync()
    {
        if (_sprite is null || _surface is null) return;
        var version = ++_version;
        var file = SourceFile ?? await App.Services.GetRequiredService<ArtworkService>().GetLocalPathAsync(SourcePath, 1080);
        if (version != _version) return;
        if (file is null)
        {
            FadeTo(0);
            return;
        }
        try
        {
            var storage = await StorageFile.GetFileFromPathAsync(file);
            var stream = await storage.OpenReadAsync();
            if (version != _version) return;
            var image = LoadedImageSurface.StartLoadFromStream(stream);
            image.LoadCompleted += (_, e) =>
            {
                if (version != _version || e.Status != LoadedImageSourceLoadStatus.Success) return;
                FadeIn();
            };
            _surface.Surface = image;
            _image?.Dispose();
            _image = image;
        }
        catch (Exception)
        {
            // Decoration only: a broken image simply isn't shown.
            _sprite.Opacity = 0;
        }
    }

    private void FadeIn()
    {
        if (_compositor is null || _sprite is null) return;
        if (!Helpers.Motion.IsEnabled)
        {
            _sprite.StopAnimation(nameof(Visual.Opacity));
            _sprite.Opacity = 1;
            return;
        }
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0, 0);
        fade.InsertKeyFrame(1, 1);
        fade.Duration = TimeSpan.FromMilliseconds(450);
        _sprite.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private void FadeTo(float opacity)
    {
        if (_compositor is null || _sprite is null) return;
        if (!Helpers.Motion.IsEnabled)
        {
            _sprite.StopAnimation(nameof(Visual.Opacity));
            _sprite.Opacity = opacity;
            return;
        }
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, opacity);
        fade.Duration = TimeSpan.FromMilliseconds(350);
        _sprite.StartAnimation(nameof(Visual.Opacity), fade);
    }
}
