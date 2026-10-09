using System.ComponentModel;
using HumeMusic.Controls;
using HumeMusic.Helpers;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace HumeMusic.Views;

/// <summary>
/// Artist page: the artist image dissolving into Plex's UltraBlur for the artist, with the name (or
/// logo), actions and facts along the bottom of it; section tabs; the top 10 beside the latest
/// release; the discography, similar artists, the listener's playlists the artist is on, and the
/// biography.
/// </summary>
public sealed partial class ArtistPage : Page
{
    private readonly AmbientService _ambient = App.Services.GetRequiredService<AmbientService>();
    private readonly List<(Button Button, TextBlock Label, Rectangle Underline, FrameworkElement? Target)> _tabs = [];
    private readonly ArtistLogoService _logos = App.Services.GetRequiredService<ArtistLogoService>();
    private Artist? _artist;
    private ArtworkPalette? _palette;
    private bool _active;
    private int _paletteVersion;
    private int _logoVersion;
    private int _revealVersion;
    // What the header waits for before it appears: the logo, the page colour and the backdrop colours.
    private Task _logoTask = Task.CompletedTask;
    private Task _accentTask = Task.CompletedTask;
    private Task _paletteTask = Task.CompletedTask;
    private UniformColumns? _skeletonFacts;
    private Storyboard? _skeletonPulse;

    public ArtistPage()
    {
        ViewModel = App.Services.GetRequiredService<ArtistViewModel>();
        InitializeComponent();
        Attach(ViewModel);
        SizeChanged += (_, _) => LayoutHero();
        FactsList.Loaded += (_, _) => LayoutHero();
        BuildTabs();
        var name = App.Services.GetRequiredService<PlexPass>().IsAvailable ? "Sonic Radio" : "Radio";
        ToolTipService.SetToolTip(SonicButton, name);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SonicButton, name);
        LogoImage.MaxLogoHeight = UiScale.Size(168, 110);
        LogoImage.MaxLogoWidth = UiScale.Size(600, 360);
        Helpers.Motion.Transitions(HeroText, Helpers.Motion.Emphasized, translation: true);
        Helpers.Motion.Transitions(HeroSkeleton, Helpers.Motion.Standard);
        Loaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().ItemChanged += OnItemChanged;
        Unloaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().ItemChanged -= OnItemChanged;
    }

    // Their photo, backdrop, logo or details were changed (from a menu): show them.
    private void OnItemChanged(object? sender, Core.Models.MediaItem item) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (item.RatingKey == ViewModel.Artist?.RatingKey) await ViewModel.RefreshArtistAsync();
    });

    // ------------------------------------------------------------------ artist logo

    /// <summary>
    /// With artist logos on, the artist's logo (fanart.tv's, or the one the listener chose) stands in
    /// for the name; the name shows whenever there is no logo.
    /// </summary>
    private void UpdateLogo()
    {
        var version = ++_logoVersion;
        var artist = ViewModel.Artist;
        var load = artist is not null && ViewModel.HasFullArtist ? _logos.GetAsync(artist) : Task.FromResult<PreparedLogo?>(null);
        _logoTask = load;
        _ = ShowLogoAsync(load, artist, version);
    }

    private async Task ShowLogoAsync(Task<PreparedLogo?> load, Artist? artist, int version)
    {
        PreparedLogo? logo;
        try
        {
            logo = await load;
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or System.Runtime.InteropServices.COMException)
        {
            logo = null;
        }
        if (version != _logoVersion || !ReferenceEquals(artist, ViewModel.Artist)) return;
        LogoImage.Tinted = _logos.UsesColourDrop;
        if (logo is not null) ApplyLogoColour(animate: false);
        LogoImage.Logo = logo;
        AutomationProperties.SetName(LogoImage, artist?.Title ?? string.Empty);
        LogoImage.Visibility = logo is null ? Visibility.Collapsed : Visibility.Visible;
        NameText.Visibility = logo is null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Colour Drop: the logo takes the page's colour, the same as its Play button.</summary>
    private void ApplyLogoColour(bool animate)
    {
        var rgb = Accent.Source is { } source ? Core.Artwork.MelodayPalette.Accent(source, dark: true) : 0xF4F2EE;
        LogoImage.SetColour(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb), animate);
    }

    private void OnAccentChanged(object? sender, EventArgs e)
    {
        if (_active) ApplyLogoColour(animate: true);
    }

    private void OnLogosChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(UpdateLogo);

    // ------------------------------------------------------------------ loading

    /// <summary>
    /// While the artist loads, a skeleton in the header's own shape stands in for it (bottom-aligned,
    /// like the header), so nothing appears piecemeal or moves when it arrives.
    /// </summary>
    private void ShowHeroSkeleton()
    {
        ++_revealVersion;
        HeroText.Opacity = 0;
        HeroText.Translation = new System.Numerics.Vector3(0, 12, 0);
        HeroText.IsHitTestVisible = false;
        if (HeroSkeleton.Children.Count == 0) BuildHeroSkeleton();
        HeroSkeleton.Visibility = Visibility.Visible;
        HeroSkeleton.Opacity = 1;
        Helpers.Motion.Transitions(HeroText, Helpers.Motion.Emphasized, translation: true);
        Helpers.Motion.Transitions(HeroSkeleton, Helpers.Motion.Standard);
        if (Helpers.Motion.IsEnabled) _skeletonPulse?.Begin();
    }

    /// <summary>
    /// The header appears in one movement once the artist, their facts, their logo and the page's
    /// colours are ready (or after a short wait, so a slow logo never holds the page back).
    /// </summary>
    private async Task RevealHeroAsync()
    {
        var version = _revealVersion;
        await Task.WhenAny(Task.WhenAll(_logoTask, _accentTask, _paletteTask), Task.Delay(2000));
        if (version != _revealVersion) return;
        HeroText.Opacity = 1;
        HeroText.Translation = System.Numerics.Vector3.Zero;
        HeroText.IsHitTestVisible = true;
        HeroSkeleton.Opacity = 0;
        await Task.Delay(250);
        if (version != _revealVersion) return;
        HeroSkeleton.Visibility = Visibility.Collapsed;
        _skeletonPulse?.Stop();
    }

    private void ShowHeroNow()
    {
        ++_revealVersion;
        HeroText.Opacity = 1;
        HeroText.Translation = System.Numerics.Vector3.Zero;
        HeroText.IsHitTestVisible = true;
        HeroSkeleton.Visibility = Visibility.Collapsed;
        _skeletonPulse?.Stop();
    }

    private void BuildHeroSkeleton()
    {
        var bones = new StackPanel { Spacing = UiScale.Size(20, 16) };
        var control = UiScale.Target(40);
        bones.Children.Add(SkeletonView.Block(UiScale.Size(380, 240), UiScale.Text(64), 10));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        actions.Children.Add(SkeletonView.Block(UiScale.Size(104), control, control / 2));
        actions.Children.Add(SkeletonView.Block(UiScale.Size(124), control, control / 2));
        actions.Children.Add(SkeletonView.Block(control, control, control / 2));
        actions.Children.Add(SkeletonView.Block(control, control, control / 2));
        var stars = SkeletonView.Block(UiScale.Size(104), UiScale.Size(18), 4);
        stars.Margin = new Thickness(8, 0, 0, 0);
        actions.Children.Add(stars);
        bones.Children.Add(actions);

        _skeletonFacts = new UniformColumns { Columns = 4, ColumnSpacing = UiScale.Size(20, 16), RowSpacing = UiScale.Size(20, 16), Margin = new Thickness(0, 8, 0, 0) };
        for (var i = 0; i < 8; i++)
        {
            var fact = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            fact.Children.Add(SkeletonView.Block(UiScale.Size(20), UiScale.Size(20), UiScale.Size(10)));
            var lines = new StackPanel { Spacing = 6 };
            lines.Children.Add(SkeletonView.Block(UiScale.Size(64 + i * 11 % 30), UiScale.Size(11), 4));
            lines.Children.Add(SkeletonView.Block(UiScale.Size(110 + i * 29 % 70), UiScale.Size(15), 4));
            fact.Children.Add(lines);
            _skeletonFacts.Children.Add(fact);
        }
        bones.Children.Add(_skeletonFacts);
        HeroSkeleton.Children.Add(bones);

        var fade = new DoubleAnimation
        {
            From = 1,
            To = 0.45,
            Duration = TimeSpan.FromMilliseconds(900),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(fade, bones);
        Storyboard.SetTargetProperty(fade, "Opacity");
        _skeletonPulse = new Storyboard();
        _skeletonPulse.Children.Add(fade);
        LayoutHero();
    }

    public ArtistViewModel ViewModel { get; private set; }

    private static DetailCache Cache => App.Services.GetRequiredService<DetailCache>();
    private string? _cacheKey;

    private void Attach(ArtistViewModel vm)
    {
        vm.PropertyChanged += OnViewModelChanged;
        vm.AlbumGroups.CollectionChanged += OnSectionsChanged;
        vm.SimilarArtists.CollectionChanged += OnSectionsChanged;
        vm.FeaturedPlaylists.CollectionChanged += OnSectionsChanged;
    }

    private void Detach(ArtistViewModel vm)
    {
        vm.PropertyChanged -= OnViewModelChanged;
        vm.AlbumGroups.CollectionChanged -= OnSectionsChanged;
        vm.SimilarArtists.CollectionChanged -= OnSectionsChanged;
        vm.FeaturedPlaylists.CollectionChanged -= OnSectionsChanged;
    }

    private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) => BuildTabs();

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    public string AboutHeader(string? name) => string.IsNullOrEmpty(name) ? "About" : $"About {name}";

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _ambient.Wash = null;
        _ambient.ForceDark = true;
        _ambient.PagePalette = _palette;
        Accent.SourceChanged += OnAccentChanged;
        _logos.Changed += OnLogosChanged;
        if (e.Parameter is not Artist artist) return;
        _artist = artist;
        // A recently visited artist comes back as it was left, without loading again.
        _cacheKey = "artist:" + artist.RatingKey;
        var vm = Cache.Get(_cacheKey, () => App.Services.GetRequiredService<ArtistViewModel>(), out var isNew, e.NavigationMode == NavigationMode.Back);
        if (!ReferenceEquals(vm, ViewModel))
        {
            Detach(ViewModel);
            ViewModel = vm;
            Attach(vm);
            Bindings.Update();
        }
        if (isNew) ShowHeroSkeleton();
        UpdateArtwork();
        UpdateLogo();
        BuildTabs();
        if (isNew)
        {
            await vm.LoadAsync(artist);
            if (vm.HasError) Cache.Forget(_cacheKey);
            await RevealHeroAsync();
        }
        else
        {
            ShowHeroNow();
            Scrollers.Restore(Scroller, Cache.GetScroll(_cacheKey));
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _active = false;
        Accent.SourceChanged -= OnAccentChanged;
        _logos.Changed -= OnLogosChanged;
        _ambient.Clear();
        if (_cacheKey is not null) Cache.SetScroll(_cacheKey, Scroller.VerticalOffset);
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ArtistViewModel.Artist):
                UpdateArtwork();
                UpdateLogo();
                break;
            case nameof(ArtistViewModel.LatestRelease):
                LatestArt.SourcePath = ViewModel.LatestRelease?.Thumb;
                BuildTabs();
                break;
            case nameof(ArtistViewModel.IsLoading):
                if (!ViewModel.IsLoading) BuildTabs();
                break;
            case nameof(ArtistViewModel.HasAbout):
                BuildTabs();
                break;
        }
    }

    // ------------------------------------------------------------------ hero & backdrop

    /// <summary>Plex's wide artist background when there is one, otherwise the portrait.</summary>
    private void UpdateArtwork()
    {
        var a = ViewModel.Artist;
        var known = a?.Art is not null || ViewModel.HasFullArtist;
        HeroImage.SourcePath = known ? a?.Art ?? a?.Thumb : null;
        Portrait.SourcePath = a?.Thumb;
        SummaryText.MaxLines = 8;
        SummaryToggle.Content = "Read More";
        if (known)
        {
            _paletteTask = UpdatePaletteAsync(a);
            _accentTask = _ambient.SetAccentFromAsync(a?.Art ?? a?.Thumb, () => _active && ReferenceEquals(a, ViewModel.Artist));
        }
    }

    /// <summary>
    /// The page backdrop is an UltraBlur of the colours where the hero image meets the page (its left
    /// and bottom edges), so the image dissolves into colours that are really in it. Plex's own
    /// UltraBlur colours (from the image's corners) and the artist's metadata palette are fallbacks.
    /// </summary>
    private async Task UpdatePaletteAsync(Artist? artist)
    {
        if (artist is null) return;
        var version = ++_paletteVersion;
        // Colours of the image actually shown (the backdrop), so the page and the image's faded edges match.
        var artwork = App.Services.GetRequiredService<ArtworkService>();
        var image = artist.Art ?? artist.Thumb;
        var palette = await artwork.GetEdgePaletteAsync(image) ?? await artwork.GetUltraBlurPaletteAsync(image) ?? artist.Palette;
        if (version != _paletteVersion || palette is null) return;
        _palette = palette;
        if (_active) _ambient.PagePalette = palette;
    }

    /// <summary>
    /// Sizes come from the page alone (never from content), so this can't feed back into layout.
    /// Wide: the image fills the right ~60% of the hero and fades out to its left, the text sits
    /// beside it. Narrow: the image spans the top and fades out downwards into the text.
    /// </summary>
    private void LayoutHero()
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0) return;
        var wide = width >= UiScale.Size(900);
        // Artist backdrops are 16:9: the image box keeps that shape so the whole backdrop shows, flush
        // with the right edge of the window, rather than a tall box that crops its sides.
        var imageWidth = wide ? Math.Round(width * 0.8) : width;
        var imageHeight = Math.Round(Math.Clamp(imageWidth * 9 / 16, UiScale.Size(320, 280), height * 0.85));

        HeroImage.Height = imageHeight;
        HeroImage.HorizontalAlignment = wide ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        HeroImage.Width = wide ? imageWidth : double.NaN;
        HeroImage.FadeLeft = wide ? 0.55 : 0;
        // The facts run along the bottom of the hero, over the part of the image that has dissolved.
        HeroImage.FadeBottom = 0.5;
        Hero.MinHeight = imageHeight;

        // Everything sits at the bottom of the hero, leaving the image the open space above. It never
        // runs up under the top bar, and when stacked it starts where the image has mostly dissolved.
        var margin = wide
            ? new Thickness(0, UiScale.TopInset, 0, UiScale.Size(8))
            : new Thickness(0, Math.Round(imageHeight * 0.45), 0, UiScale.Size(8));
        HeroText.Margin = margin;
        HeroSkeleton.Margin = margin;
        HeroIdentity.MaxWidth = wide ? Math.Max(UiScale.Size(380), Math.Round(width * 0.5)) : double.PositiveInfinity;
        // Four facts to a row across the header; fewer when the page is narrow.
        var columns = width < UiScale.Size(560) ? 1 : width < UiScale.Size(900) ? 2 : 4;
        if (FactsList.ItemsPanelRoot is UniformColumns facts) facts.Columns = columns;
        if (_skeletonFacts is not null) _skeletonFacts.Columns = columns;

        var state = width < UiScale.Size(1000) ? "NarrowState" : width < UiScale.Size(1240) ? "MediumState" : "WideState";
        VisualStateManager.GoToState(this, state, false);
    }

    // ------------------------------------------------------------------ tabs

    /// <summary>Tabs for the sections this artist actually has.</summary>
    private void BuildTabs()
    {
        var current = _tabs.FirstOrDefault(t => t.Underline.Opacity > 0).Label?.Text;
        _tabs.Clear();
        Tabs.Children.Clear();
        AddTab("Overview", null);
        if (ViewModel.AlbumGroups.Count > 0) AddTab("Albums", AlbumsSection);
        if (ViewModel.TopSongs.Count > 0) AddTab("Top Songs", OverviewSection);
        if (ViewModel.SimilarArtists.Count > 0) AddTab("Similar Artists", SimilarSection);
        if (ViewModel.FeaturedPlaylists.Count > 0) AddTab("Playlists", PlaylistsSection);
        if (ViewModel.HasAbout) AddTab("About", AboutSection);
        Select(_tabs.FindIndex(t => t.Label.Text == current) is var i and >= 0 ? i : 0);
    }

    private void AddTab(string title, FrameworkElement? target)
    {
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center };
        var underline = new Rectangle
        {
            Height = 2,
            RadiusX = 1,
            RadiusY = 1,
            VerticalAlignment = VerticalAlignment.Bottom,
            // The page's palette colour (white when the artwork has none).
            Fill = Accent.DarkFill ?? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            Opacity = 0,
        };
        var content = new Grid { Children = { label, underline } };
        underline.Margin = new Thickness(0, 0, 0, -1);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["HumeTabButtonStyle"],
            Content = content,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, title);
        var index = _tabs.Count;
        button.Click += (_, _) =>
        {
            Select(index);
            ScrollTo(target);
        };
        _tabs.Add((button, label, underline, target));
        Tabs.Children.Add(button);
    }

    private void Select(int index)
    {
        // The current tab's underline grows in from its centre as the others shrink away, and the
        // labels brighten and dim, all over the standard duration.
        for (var i = 0; i < _tabs.Count; i++)
        {
            var on = i == index;
            var (button, label, underline, _) = _tabs[i];
            label.OpacityTransition ??= Helpers.Motion.Scalar(Helpers.Motion.Standard);
            underline.OpacityTransition ??= Helpers.Motion.Scalar(Helpers.Motion.Standard);
            underline.ScaleTransition ??= Helpers.Motion.Vector(Helpers.Motion.Standard);
            underline.CenterPoint = new System.Numerics.Vector3((float)underline.ActualWidth / 2, 1, 0);
            underline.Opacity = on ? 1 : 0;
            underline.Scale = on ? System.Numerics.Vector3.One : new System.Numerics.Vector3(0.4f, 1, 1);
            button.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HumeOnColorPrimaryBrush"];
            label.Opacity = on ? 1 : 0.7;
        }
    }

    private double OffsetOf(FrameworkElement target) =>
        target.TransformToVisual((UIElement)Scroller.Content).TransformPoint(new Point(0, 0)).Y;

    private void ScrollTo(FrameworkElement? target)
    {
        // Land the section just below the floating top bar.
        var y = target is null ? 0 : Math.Max(0, OffsetOf(target) - UiScale.TopInset - UiScale.Size(24));
        Scroller.ChangeView(null, y, null);
    }

    /// <summary>The tab of the section at the top of the viewport is the current one.</summary>
    private void OnScrollChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate || _tabs.Count == 0) return;
        var probe = Scroller.VerticalOffset + UiScale.TopInset + Scroller.ViewportHeight * 0.3;
        var index = 0;
        for (var i = 1; i < _tabs.Count; i++)
        {
            if (_tabs[i].Target is { Visibility: Visibility.Visible } t && OffsetOf(t) <= probe) index = i;
        }
        // "Top Songs" and "Overview" share the overview section: at the very top, Overview wins.
        if (Scroller.VerticalOffset < Hero.ActualHeight * 0.5) index = 0;
        Select(index);
    }

    // ------------------------------------------------------------------ actions

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (_artist is not null) await ViewModel.LoadAsync(_artist);
    }

    private void OnPlay(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Artist is { } a) _ = Actions.PlayAsync(a);
    }

    private void OnShuffle(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Artist is { } a) _ = Actions.PlayAsync(a, shuffle: true);
    }

    private void OnRadio(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Artist is { } a) _ = Actions.PlaySonicRadioAsync(a);
    }

    /// <summary>The artist's title, summary, photo, backdrop and logo: edited together, and kept on the Plex server.</summary>
    private async void OnEditImages(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Artist is not { } a) return;
        if (await ItemDetailsDialog.ShowAsync(a, root: XamlRoot)) await ViewModel.RefreshArtistAsync();
    }

    private void OnMore(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Artist is not { } a) return;
        MediaMenu.Create(a).ShowAt(MoreButton, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
        {
            Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight,
        });
    }

    private void OnOpenLatest(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LatestRelease is { } album) Actions.Open(album);
    }

    private void OnPlayLatest(object sender, RoutedEventArgs e)
    {
        if (ViewModel.LatestRelease is { } album) _ = Actions.PlayAsync(album);
    }


    /// <summary>Only offer "Read More" when the biography is actually cut short.</summary>
    private void OnSummaryTrimmedChanged(TextBlock sender, IsTextTrimmedChangedEventArgs args)
    {
        if (SummaryText.MaxLines != 0) SummaryToggle.Visibility = SummaryText.IsTextTrimmed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleSummary(object sender, RoutedEventArgs e)
    {
        var expanded = SummaryText.MaxLines == 0;
        SummaryText.MaxLines = expanded ? 8 : 0;
        SummaryToggle.Content = expanded ? "Read More" : "Show Less";
    }
}
