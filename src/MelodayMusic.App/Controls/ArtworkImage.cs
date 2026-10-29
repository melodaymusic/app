using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MelodayMusic.Controls;

public sealed partial class ArtworkImage : UserControl
{
    private readonly Border _placeholder;
    private readonly LucideIcon _placeholderIcon;
    private readonly Border _image;
    private readonly ImageBrush _brush;
    private readonly Border _outline;
    private CancellationTokenSource? _cts;
    private string? _loadedKey;
    private Storyboard? _fade;
    private XamlRoot? _artRoot;
    private double _rasterScale;

    public ArtworkImage()
    {
        IsTabStop = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _placeholderIcon = new LucideIcon { Icon = "Disc3", Width = 32, Height = 32, StrokeWidth = 1.5 };
        _placeholderIcon.SetValue(ForegroundProperty, Application.Current.Resources["MelodayPlaceholderIconBrush"]);
        _placeholder = new Border
        {
            Background = (Brush)Application.Current.Resources["MelodayPlaceholderBrush"],
            Child = _placeholderIcon,
        };
        _brush = new ImageBrush { Stretch = Stretch.UniformToFill };
        _image = new Border { Background = _brush, Opacity = 0 };
        _outline = new Border { BorderThickness = new Thickness(1), IsHitTestVisible = false };
        var radius = (CornerRadius)Application.Current.Resources["MelodayRadiusArtwork"];
        _placeholder.CornerRadius = _image.CornerRadius = _outline.CornerRadius = radius;
        ArtworkCornerRadius = radius;

        var root = new Grid();
        root.Children.Add(_placeholder);
        root.Children.Add(_image);
        root.Children.Add(_outline);
        Content = root;

        Loaded += (_, _) =>
        {
            _artRoot = XamlRoot;
            if (_artRoot is not null)
            {
                _rasterScale = _artRoot.RasterizationScale;
                _artRoot.Changed += OnRootChanged;
            }
            UpdateOutline();
            Reload();
        };
        Unloaded += (_, _) =>
        {
            _cts?.Cancel();
            if (_artRoot is not null) _artRoot.Changed -= OnRootChanged;
            _artRoot = null;
            _fade?.Stop();
            _fade = null;
        };
        ActualThemeChanged += (_, _) =>
        {
            UpdateOutline();
            _placeholder.Background = (Brush)Application.Current.Resources["MelodayPlaceholderBrush"];
            _placeholderIcon.Foreground = (Brush)Application.Current.Resources["MelodayPlaceholderIconBrush"];
        };
    }

    private void OnRootChanged(XamlRoot root, XamlRootChangedEventArgs e)
    {
        if (Math.Abs(root.RasterizationScale - _rasterScale) < 0.001) return;
        _rasterScale = root.RasterizationScale;
        Reload();
    }

    public static readonly DependencyProperty SourcePathProperty = DependencyProperty.Register(
        nameof(SourcePath), typeof(string), typeof(ArtworkImage), new PropertyMetadata(null, (d, _) => ((ArtworkImage)d).Reload()));

    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    public static readonly DependencyProperty DecodeSizeProperty = DependencyProperty.Register(
        nameof(DecodeSize), typeof(int), typeof(ArtworkImage), new PropertyMetadata(200, (d, _) => ((ArtworkImage)d).Reload()));

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

    public bool ShowOutline
    {
        get => (bool)GetValue(ShowOutlineProperty);
        set => SetValue(ShowOutlineProperty, value);
    }

    private void UpdateOutline()
    {
        _outline.BorderBrush = ShowOutline
            ? new SolidColorBrush(Helpers.ThemeColors.Get("MelodayArtworkOutlineBrush", ActualTheme, Windows.UI.Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)))
            : null;
    }

    private async void Reload()
    {
        var path = SourcePath;
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var pixels = ArtworkService.Bucket((int)Math.Ceiling(DecodeSize * scale));
        var key = path is null ? null : $"{path}|{pixels}";
        if (key == _loadedKey && _image.Opacity > 0) return;

        _cts?.Cancel();
        _fade?.Stop();
        _fade = null;
        _loadedKey = null;

        if (path is not null && ArtworkMemory.Find(path, pixels) is { } known)
        {
            _brush.ImageSource = known;
            _image.Opacity = 1;
            ShowPlaceholder(false);
            _loadedKey = key;
            return;
        }

        _brush.ImageSource = null;
        _image.Opacity = 0;
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
            ArtworkMemory.Keep(path, pixels, bitmap);
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

    private void ShowPlaceholder(bool show, bool icon = true)
    {
        _placeholder.Opacity = show ? 1 : 0;
        var child = show && icon ? _placeholderIcon : null;
        if (!ReferenceEquals(_placeholder.Child, child)) _placeholder.Child = child;
    }

    private void FadeIn()
    {
        if (!Helpers.Motion.IsActive)
        {
            _image.Opacity = 1;
            ShowPlaceholder(false);
            return;
        }
        var animation = new DoubleAnimation { To = 1, Duration = new Duration(TimeSpan.FromMilliseconds(Helpers.Motion.Standard)) };
        Storyboard.SetTarget(animation, _image);
        Storyboard.SetTargetProperty(animation, nameof(Opacity));
        var sb = new Storyboard();
        _fade = sb;
        sb.Children.Add(animation);
        sb.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_fade, sb)) return;
            _fade = null;
            ShowPlaceholder(false);
        };
        sb.Begin();
    }
}
