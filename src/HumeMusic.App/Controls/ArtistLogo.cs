using System.Numerics;
using HumeMusic.Services;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI;

namespace HumeMusic.Controls;

/// <summary>
/// An artist's logo, shown as made or painted in a single colour by Colour Drop (<see cref="Tinted"/>):
/// the logo is then a mask over a colour on the compositor, so the colour can change (and ease)
/// without touching the image. The logo keeps its shape: it is as wide as allowed, unless that would
/// make it taller than <see cref="MaxLogoHeight"/>.
/// </summary>
public sealed partial class ArtistLogo : Panel
{
    private readonly Compositor _compositor;
    private readonly SpriteVisual _sprite;
    private readonly CompositionColorBrush _colour;
    private readonly CompositionSurfaceBrush _mask;
    private readonly CompositionSurfaceBrush _plain;
    private readonly CompositionMaskBrush _painted;
    private LoadedImageSurface? _surface;
    private int _version;

    public ArtistLogo()
    {
        var visual = ElementCompositionPreview.GetElementVisual(this);
        _compositor = visual.Compositor;
        _colour = _compositor.CreateColorBrush(Microsoft.UI.Colors.White);
        _mask = _compositor.CreateSurfaceBrush();
        _mask.Stretch = CompositionStretch.Uniform;
        _mask.HorizontalAlignmentRatio = 0;
        _mask.VerticalAlignmentRatio = 0.5f;
        _plain = _compositor.CreateSurfaceBrush();
        _plain.Stretch = CompositionStretch.Uniform;
        _plain.HorizontalAlignmentRatio = 0;
        _plain.VerticalAlignmentRatio = 0.5f;
        _painted = _compositor.CreateMaskBrush();
        _painted.Source = _colour;
        _painted.Mask = _mask;
        _sprite = _compositor.CreateSpriteVisual();
        _sprite.Brush = _painted;
        _sprite.Opacity = 0;
        ElementCompositionPreview.SetElementChildVisual(this, _sprite);
        Unloaded += (_, _) =>
        {
            _surface?.Dispose();
            _surface = null;
        };
    }

    public static readonly DependencyProperty LogoProperty = DependencyProperty.Register(
        nameof(Logo), typeof(PreparedLogo), typeof(ArtistLogo), new PropertyMetadata(null, (d, _) => ((ArtistLogo)d).OnLogoChanged()));

    public PreparedLogo? Logo
    {
        get => (PreparedLogo?)GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    public static readonly DependencyProperty TintedProperty = DependencyProperty.Register(
        nameof(Tinted), typeof(bool), typeof(ArtistLogo), new PropertyMetadata(true, (d, e) => ((ArtistLogo)d)._sprite.Brush = (bool)e.NewValue ? ((ArtistLogo)d)._painted : ((ArtistLogo)d)._plain));

    /// <summary>Painted in one colour (Colour Drop), or shown in the logo's own colours.</summary>
    public bool Tinted
    {
        get => (bool)GetValue(TintedProperty);
        set => SetValue(TintedProperty, value);
    }

    public static readonly DependencyProperty MaxLogoHeightProperty = DependencyProperty.Register(
        nameof(MaxLogoHeight), typeof(double), typeof(ArtistLogo), new PropertyMetadata(120.0, (d, _) => ((ArtistLogo)d).InvalidateMeasure()));

    public double MaxLogoHeight
    {
        get => (double)GetValue(MaxLogoHeightProperty);
        set => SetValue(MaxLogoHeightProperty, value);
    }

    public static readonly DependencyProperty MaxLogoWidthProperty = DependencyProperty.Register(
        nameof(MaxLogoWidth), typeof(double), typeof(ArtistLogo), new PropertyMetadata(480.0, (d, _) => ((ArtistLogo)d).InvalidateMeasure()));

    public double MaxLogoWidth
    {
        get => (double)GetValue(MaxLogoWidthProperty);
        set => SetValue(MaxLogoWidthProperty, value);
    }

    /// <summary>Eases the logo to a new colour.</summary>
    public void SetColour(Color colour, bool animate = true)
    {
        if (!animate || !Helpers.Motion.IsEnabled)
        {
            _colour.Color = colour;
            return;
        }
        var tween = _compositor.CreateColorKeyFrameAnimation();
        tween.InsertKeyFrame(1, colour);
        tween.Duration = TimeSpan.FromMilliseconds(450);
        _colour.StartAnimation("Color", tween);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Logo is not { Aspect: > 0 } logo) return new Size(0, 0);
        var width = Math.Min(MaxLogoWidth, double.IsInfinity(availableSize.Width) ? MaxLogoWidth : availableSize.Width);
        var height = width / logo.Aspect;
        if (height > MaxLogoHeight)
        {
            height = MaxLogoHeight;
            width = height * logo.Aspect;
        }
        return new Size(Math.Floor(width), Math.Floor(height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Visual size only: nothing here feeds back into layout.
        _sprite.Size = new Vector2((float)finalSize.Width, (float)finalSize.Height);
        return finalSize;
    }

    private async void OnLogoChanged()
    {
        var version = ++_version;
        InvalidateMeasure();
        _sprite.Opacity = 0;
        _surface?.Dispose();
        _surface = null;
        _mask.Surface = null;
        _plain.Surface = null;
        if (Logo is not { } logo) return;
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(logo.Path);
            var stream = await file.OpenReadAsync();
            if (version != _version) return;
            var surface = LoadedImageSurface.StartLoadFromStream(stream);
            surface.LoadCompleted += (_, e) =>
            {
                if (version != _version || e.Status != LoadedImageSourceLoadStatus.Success) return;
                if (!Helpers.Motion.IsEnabled)
                {
                    _sprite.Opacity = 1;
                    return;
                }
                var fade = _compositor.CreateScalarKeyFrameAnimation();
                fade.InsertKeyFrame(1, 1);
                fade.Duration = TimeSpan.FromMilliseconds(300);
                _sprite.StartAnimation("Opacity", fade);
            };
            _surface = surface;
            _mask.Surface = surface;
            _plain.Surface = surface;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A missing cache file just means the name is shown instead.
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ArtistLogoPeer(this);

    private sealed partial class ArtistLogoPeer(ArtistLogo owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
        protected override bool IsControlElementCore() => true;
    }
}
