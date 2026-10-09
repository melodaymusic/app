using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Controls;

/// <summary>
/// The backdrop the Plex server keeps for an item (a playlist's, an album's, a collection's), laid out as the artist page lays
/// out the artist's: the 16:9 picture flush with the right edge (the whole page width when it is narrow), dissolving to the
/// left and downward into the page's UltraBlur wash, which stays behind it. Collapsed when the item has none.
/// </summary>
public sealed class BackdropLayer : Grid
{
    private readonly FadedArtwork _picture = new() { VerticalAlignment = VerticalAlignment.Top, FadeTop = 0, FadeBottom = 0.5 };
    private readonly TranslateTransform _scroll = new();
    private ScrollViewer? _scroller;

    public BackdropLayer()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        _picture.RenderTransform = _scroll;
        Children.Add(_picture);
        SizeChanged += (_, _) => Layout();
        Loaded += (_, _) => AttachScroller();
        Unloaded += (_, _) => DetachScroller();
    }

    public static readonly DependencyProperty ScrollSourceProperty =
        DependencyProperty.Register(nameof(ScrollSource), typeof(FrameworkElement), typeof(BackdropLayer), new PropertyMetadata(null, (d, _) => ((BackdropLayer)d).AttachScroller()));

    /// <summary>The page's scrolling content: the backdrop moves up with it, as the artist page's hero does.</summary>
    public FrameworkElement? ScrollSource
    {
        get => (FrameworkElement?)GetValue(ScrollSourceProperty);
        set => SetValue(ScrollSourceProperty, value);
    }

    private void AttachScroller()
    {
        DetachScroller();
        if (ScrollSource is not { } source || !IsLoaded) return;
        if (!source.IsLoaded)
        {
            void OnLoaded(object sender, RoutedEventArgs e)
            {
                source.Loaded -= OnLoaded;
                AttachScroller();
            }
            source.Loaded += OnLoaded;
            return;
        }
        _scroller = Helpers.Scrollers.Find(source);
        if (_scroller is null) return;
        _scroller.ViewChanged += OnViewChanged;
        _scroll.Y = -_scroller.VerticalOffset;
    }

    private void DetachScroller()
    {
        if (_scroller is not null) _scroller.ViewChanged -= OnViewChanged;
        _scroller = null;
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => _scroll.Y = -(_scroller?.VerticalOffset ?? 0);

    public static readonly DependencyProperty SourcePathProperty =
        DependencyProperty.Register(nameof(SourcePath), typeof(string), typeof(BackdropLayer), new PropertyMetadata(null, (d, e) => ((BackdropLayer)d).Apply((string?)e.NewValue)));

    /// <summary>The Plex path of the backdrop, or null for none.</summary>
    public string? SourcePath
    {
        get => (string?)GetValue(SourcePathProperty);
        set => SetValue(SourcePathProperty, value);
    }

    private void Apply(string? path)
    {
        _picture.SourcePath = path;
        Visibility = string.IsNullOrEmpty(path) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>The artist page's own proportions (ArtistPage.LayoutHero).</summary>
    private void Layout()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0) return;
        var wide = width >= Services.UiScale.Size(900);
        var imageWidth = wide ? Math.Round(width * 0.8) : width;
        var imageHeight = Math.Round(Math.Clamp(imageWidth * 9 / 16, Services.UiScale.Size(320, 280), Math.Max(height * 0.85, Services.UiScale.Size(320, 280))));
        _picture.Height = imageHeight;
        _picture.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        _picture.Width = wide ? imageWidth : double.NaN;
        _picture.FadeLeft = wide ? 0.55 : 0;
    }
}
