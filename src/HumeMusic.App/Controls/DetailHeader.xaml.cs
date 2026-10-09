using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace HumeMusic.Controls;

/// <summary>
/// The shared header of detail pages. Pages give it the item and the text lines; it lays them out
/// the same way on every page, handles "More" and the subtitle link, and condenses while the page's
/// content scrolls (see <see cref="ScrollSource"/>).
/// </summary>
public sealed partial class DetailHeader : UserControl
{
    private ScrollViewer? _scroller;
    private bool _compact;
    private double _lastOffset;
    private double _travel;
    private DateTime _settleUntil;
    private Storyboard? _animation;

    public DetailHeader()
    {
        InitializeComponent();
        SizeChanged += (_, e) =>
        {
            if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) >= 1) ApplySizes(animate: false);
        };
        Loaded += (_, _) =>
        {
            Helpers.Elevation.Apply(Cover, ShadowReceiver, 32);
            ApplySizes(animate: false);
            AttachScroller();
            Accent.SourceChanged += OnAccentChanged;
            App.Services.GetRequiredService<ArtistLogoService>().Changed += OnLogoSettingChanged;
            ApplyLogoLook(animate: false);
        };
        Unloaded += (_, _) =>
        {
            DetachScroller();
            Accent.SourceChanged -= OnAccentChanged;
            App.Services.GetRequiredService<ArtistLogoService>().Changed -= OnLogoSettingChanged;
        };
    }

    /// <summary>Raised by the Play and Shuffle buttons; the page decides what plays (e.g. an album's loaded tracks).</summary>
    public event EventHandler? PlayRequested;
    public event EventHandler? ShuffleRequested;

    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(
        nameof(Item), typeof(MediaItem), typeof(DetailHeader), new PropertyMetadata(null, (d, _) => ((DetailHeader)d).UpdateItem()));

    /// <summary>The album, playlist or collection shown.</summary>
    public MediaItem? Item
    {
        get => (MediaItem?)GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    public static readonly DependencyProperty LiveCoverProperty = DependencyProperty.Register(
        nameof(LiveCover), typeof(UIElement), typeof(DetailHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (DetailHeader)d;
            self.LiveCoverHost.Child = e.NewValue as UIElement;
            self.LiveCoverHost.Visibility = e.NewValue is null ? Visibility.Collapsed : Visibility.Visible;
        }));

    /// <summary>A live cover shown over the picture, sized with it (Meloday's animated sun).</summary>
    public UIElement? LiveCover
    {
        get => (UIElement?)GetValue(LiveCoverProperty);
        set => SetValue(LiveCoverProperty, value);
    }

    public static readonly DependencyProperty ExtraActionsProperty = DependencyProperty.Register(
        nameof(ExtraActions), typeof(UIElement), typeof(DetailHeader), new PropertyMetadata(null, (d, e) =>
            ((DetailHeader)d).ExtraActionsHost.Content = e.NewValue));

    /// <summary>One more action, after Pin (Meloday's "Keep on Plex").</summary>
    public UIElement? ExtraActions
    {
        get => (UIElement?)GetValue(ExtraActionsProperty);
        set => SetValue(ExtraActionsProperty, value);
    }

    public static readonly DependencyProperty StatusTextProperty = DependencyProperty.Register(
        nameof(StatusText), typeof(string), typeof(DetailHeader), new PropertyMetadata(string.Empty, (d, e) =>
        {
            var self = (DetailHeader)d;
            var text = e.NewValue as string ?? string.Empty;
            self.StatusLine.Text = text;
            self.StatusLine.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }));

    /// <summary>A line under the actions (whether Meloday is kept on Plex, and when it last was).</summary>
    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public static readonly DependencyProperty ArtworkProperty = DependencyProperty.Register(
        nameof(Artwork), typeof(string), typeof(DetailHeader), new PropertyMetadata(null, (d, e) => ((DetailHeader)d).Cover.SourcePath = e.NewValue as string));

    public string? Artwork
    {
        get => (string?)GetValue(ArtworkProperty);
        set => SetValue(ArtworkProperty, value);
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(
        nameof(Subtitle), typeof(string), typeof(DetailHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (DetailHeader)d;
            self.SubtitleText.Text = e.NewValue as string ?? string.Empty;
            self.SubtitleLink.Visibility = string.IsNullOrEmpty(e.NewValue as string) || self._compact ? Visibility.Collapsed : Visibility.Visible;
        }));

    /// <summary>A link under the title (the album's artist).</summary>
    public string? Subtitle
    {
        get => (string?)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly DependencyProperty MetaProperty = DependencyProperty.Register(
        nameof(Meta), typeof(string), typeof(DetailHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (DetailHeader)d;
            self.MetaLine.Text = e.NewValue as string ?? string.Empty;
            self.MetaLine.Visibility = string.IsNullOrEmpty(e.NewValue as string) || self._compact ? Visibility.Collapsed : Visibility.Visible;
            self.UpdateSmart();
        }));

    /// <summary>The meta line ("Pop · 2017", "31 songs · 1 hr 41 min").</summary>
    public string? Meta
    {
        get => (string?)GetValue(MetaProperty);
        set => SetValue(MetaProperty, value);
    }

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(DetailHeader), new PropertyMetadata(null, (d, e) =>
        {
            var self = (DetailHeader)d;
            self.NotesText.Text = e.NewValue as string ?? string.Empty;
            self.NotesText.MaxLines = 2;
            self.NotesToggle.Content = "More";
            self.Notes.Visibility = string.IsNullOrWhiteSpace(e.NewValue as string) || self._compact ? Visibility.Collapsed : Visibility.Visible;
        }));

    public string? Description
    {
        get => (string?)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly DependencyProperty PlaceholderIconProperty = DependencyProperty.Register(
        nameof(PlaceholderIcon), typeof(string), typeof(DetailHeader), new PropertyMetadata("Disc3", (d, e) => ((DetailHeader)d).Cover.PlaceholderIcon = (string)e.NewValue));

    public string PlaceholderIcon
    {
        get => (string)GetValue(PlaceholderIconProperty);
        set => SetValue(PlaceholderIconProperty, value);
    }

    public static readonly DependencyProperty ScrollSourceProperty = DependencyProperty.Register(
        nameof(ScrollSource), typeof(FrameworkElement), typeof(DetailHeader), new PropertyMetadata(null, (d, _) => ((DetailHeader)d).AttachScroller()));

    /// <summary>The page's scrolling content (a ScrollViewer, or a list that contains one).</summary>
    public FrameworkElement? ScrollSource
    {
        get => (FrameworkElement?)GetValue(ScrollSourceProperty);
        set => SetValue(ScrollSourceProperty, value);
    }

    private void UpdateSmart() =>
        SmartIcon.Visibility = Item is Playlist { IsSmart: true } && MetaLine.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;

    private int _logoVersion;

    /// <summary>The server's logo for the item stands in for its name, when it has one (the name shows otherwise).</summary>
    private async Task ShowLogoAsync(MediaItem? item)
    {
        var version = ++_logoVersion;
        PreparedLogo? logo = null;
        if (item is not null)
        {
            try
            {
                logo = await App.Services.GetRequiredService<PlexLogoService>().GetAsync(item);
            }
            catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException)
            {
                logo = null;
            }
        }
        if (version != _logoVersion) return;
        ApplyLogoLook(animate: false);
        LogoImage.Logo = logo;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LogoImage, item?.Title ?? string.Empty);
        LogoImage.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        TitleText.Visibility = logo is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// The logo is shown as the artist page shows an artist's: the same size (smaller once the header condenses), and in the
    /// page's colour when Colour Drop is chosen in Settings, in its own colours otherwise.
    /// </summary>
    private void ApplyLogoLook(bool animate)
    {
        LogoImage.MaxLogoHeight = _compact ? UiScale.Size(48, 36) : UiScale.Size(168, 110);
        LogoImage.MaxLogoWidth = _compact ? UiScale.Size(240, 160) : UiScale.Size(600, 360);
        var drop = App.Services.GetRequiredService<ArtistLogoService>().UsesColourDrop;
        LogoImage.Tinted = drop;
        if (!drop) return;
        var rgb = Accent.Source is { } source ? Core.Artwork.MelodayPalette.Accent(source, dark: true) : 0xF4F2EE;
        LogoImage.SetColour(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), animate);
    }

    private void OnAccentChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => ApplyLogoLook(animate: true));

    private void OnLogoSettingChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() => ApplyLogoLook(animate: false));

    private void UpdateItem()
    {
        TitleText.Text = Item?.Title ?? string.Empty;
        _ = ShowLogoAsync(Item);
        UpdateSmart();
        Pin.Item = Item;
        HeaderRating.Item = Item;
        HeaderRating.Visibility = Item is Album ? Visibility.Visible : Visibility.Collapsed;
        AddToPlaylistButton.Visibility = Item is Album or MusicCollection ? Visibility.Visible : Visibility.Collapsed;
        SetCompact(false, animate: false);
    }

    // ------------------------------------------------------------------ condensing on scroll

    private void AttachScroller()
    {
        DetachScroller();
        if (ScrollSource is not { } source || !IsLoaded) return;
        if (!source.IsLoaded)
        {
            source.Loaded += OnSourceLoaded;
            return;
        }
        _scroller = source as ScrollViewer ?? FindScroller(source);
        if (_scroller is null) return;
        _lastOffset = _scroller.VerticalOffset;
        _scroller.ViewChanged += OnViewChanged;
    }

    private void OnSourceLoaded(object sender, RoutedEventArgs e)
    {
        ((FrameworkElement)sender).Loaded -= OnSourceLoaded;
        AttachScroller();
    }

    private void DetachScroller()
    {
        if (_scroller is not null) _scroller.ViewChanged -= OnViewChanged;
        _scroller = null;
    }

    /// <summary>
    /// Condenses once the content has moved down a little, and opens again when the listener scrolls
    /// back up (or reaches the top). While the header resizes, the viewport changes size too, which can
    /// nudge the offset; those moments are ignored so the header never flips back and forth.
    /// </summary>
    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroller is null) return;
        var offset = _scroller.VerticalOffset;
        var delta = offset - _lastOffset;
        _lastOffset = offset;
        if (DateTime.UtcNow < _settleUntil) return;

        if (offset <= 2)
        {
            SetCompact(false, animate: true);
            return;
        }
        if (Math.Sign(delta) != Math.Sign(_travel)) _travel = 0;
        _travel += delta;
        if (_travel > UiScale.Size(24)) SetCompact(true, animate: true);
        else if (_travel < -UiScale.Size(56)) SetCompact(false, animate: true);
    }

    private void SetCompact(bool compact, bool animate)
    {
        _travel = 0;
        if (compact == _compact) return;
        _compact = compact;
        _settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(animate ? 380 : 0);
        ApplySizes(animate);
    }

    private double ExpandedCover => ActualWidth > 0 && ActualWidth < UiScale.Size(760) ? UiScale.Size(176, 140) : UiScale.Size(260, 180);
    private static double CompactCover => UiScale.Size(72, 56);

    private void ApplySizes(bool animate)
    {
        var cover = _compact ? CompactCover : ExpandedCover;
        var title = _compact ? UiScale.Text(28) : UiScale.Text(48);
        var lineHeight = _compact ? UiScale.Text(34) : UiScale.Text(56);
        var detail = _compact ? Visibility.Collapsed : Visibility.Visible;
        Actions.Margin = new Thickness(0, _compact ? UiScale.Size(8) : UiScale.Size(16), 0, 0);
        ApplyLogoLook(animate: false);
        Root.Padding = _compact
            ? new Thickness(UiScale.Gutter, UiScale.TopInset + UiScale.Size(4), UiScale.Gutter, UiScale.Size(12))
            : new Thickness(UiScale.Gutter, UiScale.TopInset + UiScale.Size(8), UiScale.Gutter, UiScale.Size(24));

        // Detail lines leave straight away when condensing; they return once the header has opened.
        if (_compact) SetDetailVisibility(Visibility.Collapsed);

        // Carry on from where any running animation has got to: stopping a storyboard snaps the
        // animated values back to their base values, so those are set to the current ones first.
        if (_animation is not null)
        {
            var (width, font) = (Cover.ActualWidth, TitleText.FontSize);
            _animation.Stop();
            _animation = null;
            if (width > 0) Cover.Width = Cover.Height = width;
            TitleText.FontSize = font;
        }
        if (!animate || !Helpers.Motion.IsEnabled)
        {
            Cover.Width = Cover.Height = cover;
            TitleText.FontSize = title;
            TitleText.LineHeight = lineHeight;
            SetDetailVisibility(detail);
            return;
        }
        var story = new Storyboard();
        void Animate(DependencyObject target, string property, double to)
        {
            var a = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized),
                EnableDependentAnimation = true,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, property);
            story.Children.Add(a);
        }
        Animate(Cover, "Width", cover);
        Animate(Cover, "Height", cover);
        Animate(TitleText, "FontSize", title);
        story.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_animation, story)) return;
            // Make the end values the real ones, so the next animation starts from here.
            story.Stop();
            _animation = null;
            Cover.Width = Cover.Height = cover;
            TitleText.FontSize = title;
            TitleText.LineHeight = lineHeight;
            if (!_compact) SetDetailVisibility(Visibility.Visible);
        };
        _animation = story;
        story.Begin();
    }

    /// <summary>Subtitle, meta and notes: shown only when expanded and when they have something to say.</summary>
    private void SetDetailVisibility(Visibility visibility)
    {
        var shown = visibility == Visibility.Visible;
        SubtitleLink.Visibility = shown && !string.IsNullOrEmpty(Subtitle) ? Visibility.Visible : Visibility.Collapsed;
        MetaLine.Visibility = shown && !string.IsNullOrEmpty(Meta) ? Visibility.Visible : Visibility.Collapsed;
        UpdateSmart();
        Notes.Visibility = shown && !string.IsNullOrWhiteSpace(Description) ? Visibility.Visible : Visibility.Collapsed;
    }

    private static ScrollViewer? FindScroller(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>([root]);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (node is ScrollViewer sv) return sv;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
        }
        return null;
    }

    // ------------------------------------------------------------------ actions

    private LibraryActions Actions_ => App.Services.GetRequiredService<LibraryActions>();

    private void OnPlay(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);

    private void OnShuffle(object sender, RoutedEventArgs e) => ShuffleRequested?.Invoke(this, EventArgs.Empty);

    private void OnAddToPlaylist(object sender, RoutedEventArgs e)
    {
        if (Item is { } item)
            MediaMenu.AddToPlaylistFlyout(item).ShowAt(AddToPlaylistButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
            {
                Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft,
            });
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        if (Item is { } item) MediaMenu.Show(MoreButton, item);
    }

    private void OnSubtitleClick(object sender, RoutedEventArgs e)
    {
        if (Item is Album a) Actions_.OpenArtist(a.ArtistRatingKey, a.ArtistTitle, a.ArtistThumb);
    }

    private void OnNotesTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (NotesText.MaxLines != 0) NotesToggle.Visibility = NotesText.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleNotes(object sender, RoutedEventArgs e)
    {
        var expanded = NotesText.MaxLines == 0;
        NotesText.MaxLines = expanded ? 2 : 0;
        NotesToggle.Content = expanded ? "More" : "Less";
    }
}
