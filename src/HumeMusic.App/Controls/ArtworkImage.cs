using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HumeMusic.Controls;

/// <summary>
/// Displays Plex artwork. Downloads a server-resized copy in the background via the artwork cache,
/// decodes at display size, shows a neutral placeholder with an icon while loading or when the
/// item has no artwork, and copes with list container recycling.
/// </summary>
public sealed partial class ArtworkImage : UserControl
{
    private readonly Border _placeholder;
    private readonly LucideIcon _placeholderIcon;
    private readonly Border _image;
    private readonly ImageBrush _brush;
    private readonly Border _outline;
    private CancellationTokenSource? _cts;
    private string? _loadedKey;

    public ArtworkImage()
    {
        IsTabStop = false;
        _placeholderIcon = new LucideIcon { Icon = "Disc3", Width = 32, Height = 32, StrokeWidth = 1.5 };
        _placeholderIcon.SetValue(ForegroundProperty, Application.Current.Resources["HumePlaceholderIconBrush"]);
        _placeholder = new Border
        {
            Background = (Brush)Application.Current.Resources["HumePlaceholderBrush"],
            Child = _placeholderIcon,
        };
        _brush = new ImageBrush { Stretch = Stretch.UniformToFill };
        _image = new Border { Background = _brush, Opacity = 0 };
        // A hairline keeps pale covers from dissolving into the background (as album art is framed in Apple Music).
        _outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };

        var root = new Grid();
        root.Children.Add(_placeholder);
        root.Children.Add(_image);
        root.Children.Add(_outline);
        Content = root;

        Loaded += (_, _) =>
        {
            UpdateOutline();
            Reload();
        };
        Unloaded += (_, _) => _cts?.Cancel();
        ActualThemeChanged += (_, _) =>
        {
            UpdateOutline();
            _placeholder.Background = (Brush)Application.Current.Resources["HumePlaceholderBrush"];
            _placeholderIcon.Foreground = (Brush)Application.Current.Resources["HumePlaceholderIconBrush"];
        };
    }

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(ArtworkImage), new PropertyMetadata(null, (d, _) => ((ArtworkImage)d).Reload()));

    /// <summary>Plex artwork path such as /library/metadata/123/thumb/456.</summary>
    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public static readonly DependencyProperty DecodeSizeProperty = DependencyProperty.Register(
        nameof(DecodeSize), typeof(int), typeof(ArtworkImage), new PropertyMetadata(200, (d, _) => ((ArtworkImage)d).Reload()));

    /// <summary>Display size in effective pixels; used to request and decode an appropriately sized image.</summary>
    public int DecodeSize
    {
        get => (int)GetValue(DecodeSizeProperty);
        set => SetValue(DecodeSizeProperty, value);
    }

    public static readonly DependencyProperty PlaceholderIconProperty = DependencyProperty.Register(
        nameof(PlaceholderIcon), typeof(string), typeof(ArtworkImage), new PropertyMetadata("Disc3", (d, e) => ((ArtworkImage)d)._placeholderIcon.Icon = (string)e.NewValue));

    public string PlaceholderIcon
    {
        get => (string)GetValue(PlaceholderIconProperty);
        set => SetValue(PlaceholderIconProperty, value);
    }

    public static readonly DependencyProperty ArtworkCornerRadiusProperty = DependencyProperty.Register(
        nameof(ArtworkCornerRadius), typeof(CornerRadius), typeof(ArtworkImage), new PropertyMetadata(new CornerRadius(8), (d, e) =>
        {
            var self = (ArtworkImage)d;
            self._placeholder.CornerRadius = (CornerRadius)e.NewValue;
            self._image.CornerRadius = (CornerRadius)e.NewValue;
            self._outline.CornerRadius = (CornerRadius)e.NewValue;
        }));

    public CornerRadius ArtworkCornerRadius
    {
        get => (CornerRadius)GetValue(ArtworkCornerRadiusProperty);
        set => SetValue(ArtworkCornerRadiusProperty, value);
    }

    public static readonly DependencyProperty ShowOutlineProperty = DependencyProperty.Register(
        nameof(ShowOutline), typeof(bool), typeof(ArtworkImage), new PropertyMetadata(true, (d, _) => ((ArtworkImage)d).UpdateOutline()));

    /// <summary>Hairline frame around the artwork. Off for full-bleed backdrops.</summary>
    public bool ShowOutline
    {
        get => (bool)GetValue(ShowOutlineProperty);
        set => SetValue(ShowOutlineProperty, value);
    }

    private void UpdateOutline()
    {
        _outline.BorderBrush = ShowOutline
            ? new SolidColorBrush(Helpers.ThemeColors.Get("HumeArtworkOutlineBrush", ActualTheme, Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)))
            : null;
    }

    private async void Reload()
    {
        var path = SourcePath;
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var pixels = (int)Math.Ceiling(DecodeSize * scale);
        var key = path is null ? null : $"{path}|{ArtworkService.Bucket(pixels)}";
        if (key == _loadedKey && _image.Opacity > 0) return;

        _cts?.Cancel();
        _loadedKey = null;
        _brush.ImageSource = null;
        _image.Opacity = 0;
        // While a picture is on its way the placeholder is plain; its icon is for artwork that has none.
        ShowPlaceholder(true, icon: string.IsNullOrEmpty(path));
        if (string.IsNullOrEmpty(path) || !IsLoaded) return;

        var cts = new CancellationTokenSource();
        _cts = cts;
        string? file;
        try
        {
            var artwork = App.Services.GetRequiredService<ArtworkService>();
            file = await Task.Run(() => artwork.GetLocalPathAsync(path, pixels, cts.Token), cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (cts.IsCancellationRequested || SourcePath != path) return;
        if (file is null)
        {
            ShowPlaceholder(true, icon: true);
            return;
        }

        var bitmap = new BitmapImage
        {
            DecodePixelWidth = pixels,
            DecodePixelType = DecodePixelType.Physical,
        };
        bitmap.ImageOpened += (_, _) =>
        {
            if (cts.IsCancellationRequested) return;
            FadeIn();
        };
        bitmap.ImageFailed += (_, _) =>
        {
            if (!cts.IsCancellationRequested) ShowPlaceholder(true, icon: true);
        };
        bitmap.UriSource = new Uri(file);
        _brush.ImageSource = bitmap;
        _loadedKey = key;
    }

    /// <summary>
    /// The placeholder (and its icon) only while there's no picture: once the picture is in, the icon
    /// leaves the tree, so a page full of artwork carries a few hundred fewer elements.
    /// </summary>
    private void ShowPlaceholder(bool show, bool icon = true)
    {
        _placeholder.Opacity = show ? 1 : 0;
        var child = show && icon ? _placeholderIcon : null;
        if (!ReferenceEquals(_placeholder.Child, child)) _placeholder.Child = child;
    }

    private void FadeIn()
    {
        // Once the picture is in, the placeholder behind it goes: its colour would otherwise show as a
        // thin dark rim along the picture's anti-aliased rounded edge.
        if (!Helpers.Motion.IsEnabled)
        {
            _image.Opacity = 1;
            ShowPlaceholder(false);
            return;
        }
        var animation = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(Helpers.Motion.Standard)) };
        Storyboard.SetTarget(animation, _image);
        Storyboard.SetTargetProperty(animation, nameof(Opacity));
        var sb = new Storyboard();
        sb.Children.Add(animation);
        sb.Completed += (_, _) => ShowPlaceholder(_image.Opacity <= 0.99, icon: false);
        sb.Begin();
    }
}
