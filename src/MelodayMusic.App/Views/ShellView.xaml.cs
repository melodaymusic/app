using System.ComponentModel;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace MelodayMusic.Views;

public sealed partial class NavItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required Type Page { get; init; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial bool IsSelected { get; set; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial Microsoft.UI.Xaml.Media.ImageSource? Picture { get; set; }
    public string? PictureName { get; set; }
    public bool HasPicture => Picture is not null;
    partial void OnPictureChanged(Microsoft.UI.Xaml.Media.ImageSource? value) => OnPropertyChanged(nameof(HasPicture));
    public override string ToString() => Label;
}

public sealed partial class ShellView : UserControl
{
    public void FocusNavigation() => NavList.Focus(FocusState.Programmatic);
    private readonly NavigationService _navigation;
    private readonly PlayerViewModel _player;
    private readonly List<NavItem> _items =
    [
        new() { Label = "Home", Icon = "House", Page = typeof(HomePage) },
        new() { Label = "Made for You", Icon = "Sparkles", Page = typeof(MadeForYouPage) },
        new() { Label = "Browse", Icon = "Tags", Page = typeof(BrowsePage) },
        new() { Label = "Radio", Icon = "Radio", Page = typeof(RadioPage) },
    ];
    private readonly List<NavItem> _library =
    [
        new() { Label = "Artists", Icon = "MicVocal", Page = typeof(ArtistsPage) },
        new() { Label = "Albums", Icon = "Disc3", Page = typeof(AlbumsPage) },
        new() { Label = "Tracks", Icon = "Music", Page = typeof(TracksPage) },
        new() { Label = "Playlists", Icon = "ListMusic", Page = typeof(PlaylistsPage) },
        new() { Label = "Collections", Icon = "Layers", Page = typeof(CollectionsPage) },
        new() { Label = "Folders", Icon = "Folder", Page = typeof(FoldersPage) },
        new() { Label = "Downloads", Icon = "CircleArrowDown", Page = typeof(DownloadsPage) },
    ];
    private readonly List<NavItem> _footer =
    [
        new() { Label = "Settings", Icon = "Settings", Page = typeof(SettingsPage) },
    ];
    private readonly NavItem _chartsItem = new() { Label = "Charts", Icon = "TrendingUp", Page = typeof(ChartsPage) };
    private readonly NavItem _trendsItem = new() { Label = "Trends", Icon = "ChartColumn", Page = typeof(TrendsPage) };
    private readonly ListeningService _listening;
    private readonly LastFmService _lastFm;
    private bool _syncingSearch;

    public ShellView()
    {
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        Notifications = App.Services.GetRequiredService<NotificationService>();
        Ambient = App.Services.GetRequiredService<AmbientService>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _player = App.Services.GetRequiredService<PlayerViewModel>();
        _panels = App.Services.GetRequiredService<PanelService>();
        _listening = App.Services.GetRequiredService<ListeningService>();
        _lastFm = App.Services.GetRequiredService<LastFmService>();
        InitializeComponent();
        _panels.PropertyChanged += (_, _) => ApplyQueue();
        var stage = App.Services.GetRequiredService<StageService>();
        stage.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            var shown = stage.IsOn ? Visibility.Collapsed : Visibility.Visible;
            RailGlass.Visibility = shown;
            RailScroll.Visibility = shown;
            ContentHost.Visibility = shown;
        });

        LibraryList.ItemsSource = _library;
        FooterList.ItemsSource = _footer;
        var search = App.Services.GetRequiredService<SearchViewModel>();
        search.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(SearchViewModel.Query) || SearchBox.Text == search.Query) return;
            _syncingSearch = true;
            SearchBox.Text = search.Query;
            _syncingSearch = false;
        };
        ApplyTrendsShown();
        ShowAccountPicture();
        PlayerBar.CastShadowOnto(ContentFrame);
        Unloaded += OnUnloaded;
        Loaded += OnLoaded;
        Loaded += (_, _) =>
        {
            ApplyPinnedExpanded(animate: false);
            ApplyLibraryExpanded(animate: false);
        };
        PlayerBar.SizeChanged += (_, _) => PlaceQueue();
        SizeChanged += (_, e) =>
        {
            if ((e.PreviousSize.Width >= WideQueueWidth) != (e.NewSize.Width >= WideQueueWidth))
                DispatcherQueue.TryEnqueue(ApplyQueue);
            ApplyRail(e.NewSize.Width);
        };
        ContentSurface.AddHandler(PointerPressedEvent, new PointerEventHandler(OnContentPressed), true);
        AddKeyboardShortcuts();
        PreviewKeyDown += OnPreviewKeyDown;
        PointerPressed += OnPointerPressed;
    }

    private const double WideQueueWidth = 1600;
    private const double CompactRailWidth = 1000;
    private bool? _compactRail;

    // Below CompactRailWidth the sidebar shows icons only, with each label as a tooltip.
    private void ApplyRail(double width)
    {
        var compact = width > 0 && width < UiScale.Size(CompactRailWidth);
        if (compact == _compactRail) return;
        _compactRail = compact;
        RailColumn.Width = new GridLength(compact ? 96 : 240);
        var nav = (DataTemplate)Resources[compact ? "CompactNavTemplate" : "WideNavTemplate"];
        NavList.ItemTemplate = LibraryList.ItemTemplate = FooterList.ItemTemplate = nav;
        PinnedList.ItemTemplate = (DataTemplate)Resources[compact ? "CompactPinnedTemplate" : "PinnedNavTemplate"];
        var label = compact ? Visibility.Collapsed : Visibility.Visible;
        LibraryHeader.Visibility = LibraryLabel.Visibility = PinnedLabel.Visibility = PinnedChevron.Visibility = label;
        LibraryHeaderGrid.HorizontalAlignment = PinnedHeaderGrid.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
        ToolTipService.SetToolTip(PinnedHeader, compact ? "Pinned" : null);
        ToolTipService.SetToolTip(LibrarySectionHeader, compact ? "Library" : null);
    }

    private readonly PanelService _panels;
    private bool _queueOpen => _panels.Open != SidePanel.None;

    private bool QueueBeside => ActualWidth >= WideQueueWidth;

    private void PlaceQueue()
    {
        var bottom = 16.0;
        if (!QueueBeside && PlayerBar.Visibility == Visibility.Visible && PlayerBar.ActualHeight > 0)
            bottom = PlayerBar.ActualHeight + PlayerBar.Margin.Bottom + 12;
        if (Math.Abs(QueueGlass.Margin.Bottom - bottom) > 0.5) QueueGlass.Margin = new Thickness(8, 50, 16, bottom);
    }

    public ShellViewModel Shell { get; }
    public NotificationService Notifications { get; }
    public AmbientService Ambient { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigation.Attach(ContentFrame);
        _navigation.Navigated -= OnNavigated;
        _navigation.Navigated += OnNavigated;
        Shell.PropertyChanged -= OnShellChanged;
        Shell.PropertyChanged += OnShellChanged;
        _listening.ShownChanged -= OnTrendsShownChanged;
        _listening.ShownChanged += OnTrendsShownChanged;
        _lastFm.TrendingChanged -= OnTrendsShownChanged;
        _lastFm.TrendingChanged += OnTrendsShownChanged;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (IsLoaded && App.MainWindow?.IsIntroVisible != true) NavList.Focus(FocusState.Pointer);
        });
        if (ContentFrame.Content is not null) return;
        if (!_navigation.RestorePending()) _navigation.NavigateRoot(typeof(HomePage));
    }

    private void ShowAccountPicture()
    {
        var account = App.Services.GetRequiredService<Core.Services.PlexSession>().Account;
        if (account?.Thumb is not { Length: > 0 } thumb || !Uri.TryCreate(thumb, UriKind.Absolute, out var uri)) return;
        var settings = _footer.First(i => i.Page == typeof(SettingsPage));
        settings.PictureName = account.Name;
        settings.Picture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) { DecodePixelWidth = 64, DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical };
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _navigation.Navigated -= OnNavigated;
        Shell.PropertyChanged -= OnShellChanged;
        _listening.ShownChanged -= OnTrendsShownChanged;
        _lastFm.TrendingChanged -= OnTrendsShownChanged;
    }

    private void OnTrendsShownChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(ApplyTrendsShown);

    // Charts and Trends only appear while they have something to show: a Last.fm chart that's on, or enough listening history.
    private void ApplyTrendsShown()
    {
        var charts = _lastFm.TrendingOn || _lastFm.TrendingArtistsOn;
        var trends = _listening.TrendsShown;
        if (charts == _items.Contains(_chartsItem) && trends == _items.Contains(_trendsItem) && NavList.ItemsSource is not null) return;
        _items.Remove(_chartsItem);
        _items.Remove(_trendsItem);
        var at = Math.Clamp(_items.FindIndex(i => i.Page == typeof(RadioPage)) + 1, 0, _items.Count);
        if (trends) _items.Insert(at, _trendsItem);
        if (charts) _items.Insert(at, _chartsItem);
        NavList.ItemsSource = null;
        NavList.ItemsSource = _items;
        if (_navigation.CurrentPage == typeof(TrendsPage) && !trends || _navigation.CurrentPage == typeof(ChartsPage) && !charts)
            _navigation.NavigateRoot(typeof(HomePage));
        else if (IsLoaded) OnNavigated(this, null);
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.LibraryVersion) && IsLoaded)
        {
            ContentFrame.BackStack.Clear();
            ContentFrame.Content = null;
            _navigation.NavigateRoot(typeof(HomePage));
        }
    }

    private void OnNavClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NavItem item)
        {
            _navigation.Navigate(item.Page);
        }
    }

    private void OnNavigated(object? sender, NavigationEventArgs? e)
    {
        BackButton.IsEnabled = _navigation.CanGoBack;
        var page = _navigation.CurrentPage;
        foreach (var (list, items) in new[] { (NavList, _items), (LibraryList, _library), (FooterList, _footer) })
        {
            var match = items.FirstOrDefault(i => i.Page == page);
            list.SelectedItem = match;
            foreach (var item in items) item.IsSelected = ReferenceEquals(item, match);
        }

        PlayerBar.Visibility = page == typeof(NowPlayingPage) ? Visibility.Collapsed : Visibility.Visible;
        PlaceQueue();

        if (page != typeof(SearchPage) && !string.IsNullOrEmpty(SearchBox.Text))
        {
            _syncingSearch = true;
            SearchBox.Text = string.Empty;
            _syncingSearch = false;
        }
        if (page != typeof(SearchPage)) CollapseSearch();
    }

    private bool _searchOpen;
    private Storyboard? _searchAnimation;

    private void OnSearchButtonClick(object sender, RoutedEventArgs e) => OpenSearch();

    private void OpenSearch()
    {
        if (!_searchOpen)
        {
            _searchOpen = true;
            SearchButton.Visibility = Visibility.Collapsed;
            SearchBox.Visibility = Visibility.Visible;
            AnimateSearch(UiScale.Size(480, 300), 1);
        }
        SearchBox.UpdateLayout();
        SearchBox.Focus(FocusState.Keyboard);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_searchOpen && !IsSearchFocused()) SearchBox.Focus(FocusState.Keyboard);
        });
    }

    private bool IsSearchFocused()
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, SearchBox)) return true;
        return false;
    }

    private void CollapseSearch()
    {
        if (!_searchOpen || !string.IsNullOrEmpty(SearchBox.Text) || _navigation.CurrentPage == typeof(SearchPage)) return;
        _searchOpen = false;
        AnimateSearch(UiScale.Size(40, 32), 0, () =>
        {
            if (_searchOpen) return;
            SearchBox.Visibility = Visibility.Collapsed;
            SearchButton.Visibility = Visibility.Visible;
        });
    }

    private void OnSearchLostFocus(object sender, RoutedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
            for (var node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
                if (ReferenceEquals(node, SearchBox)) return;
            CollapseSearch();
        });
    }

    private void AnimateSearch(double width, double opacity, Action? completed = null)
    {
        _searchAnimation?.Stop();
        if (!Helpers.Motion.IsEnabled)
        {
            _searchAnimation = null;
            SearchBox.Width = width;
            SearchBox.Opacity = opacity;
            completed?.Invoke();
            return;
        }
        var story = new Storyboard();
        var grow = new DoubleAnimation
        {
            To = width,
            Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Emphasized),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        Storyboard.SetTarget(grow, SearchBox);
        Storyboard.SetTargetProperty(grow, "Width");
        var fade = new DoubleAnimation { To = opacity, Duration = TimeSpan.FromMilliseconds(opacity > 0 ? 140 : 220) };
        Storyboard.SetTarget(fade, SearchBox);
        Storyboard.SetTargetProperty(fade, "Opacity");
        story.Children.Add(grow);
        story.Children.Add(fade);
        if (completed is not null) story.Completed += (_, _) => completed();
        _searchAnimation = story;
        story.Begin();
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => _navigation.GoBack();

    public HomeViewModel Home { get; } = App.Services.GetRequiredService<HomeViewModel>();

    private void OnPinnedNavClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Core.Models.MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private void OnPinnedReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;
        App.Services.GetRequiredService<LibraryActions>().ReorderPins(PinnedList.Items.OfType<Core.Models.MediaItem>().Select(i => i.RatingKey));
    }

    private void OnPinnedContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (args.OriginalSource is not FrameworkElement { DataContext: Core.Models.MediaItem item } source) return;
        args.Handled = true;
        if (args.TryGetPosition(source, out var point)) Controls.MediaMenu.Show(source, item, at: point);
        else Controls.MediaMenu.Show(source, item);
    }

    private void OnPinnedHeaderClick(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.GetRequiredService<Core.Settings.SettingsStore>();
        settings.Current.SidebarPinnedExpanded = !settings.Current.SidebarPinnedExpanded;
        settings.Save();
        ApplyPinnedExpanded(animate: true);
    }

    private void OnLibraryHeaderClick(object sender, RoutedEventArgs e)
    {
        var settings = App.Services.GetRequiredService<Core.Settings.SettingsStore>();
        settings.Current.SidebarLibraryExpanded = !settings.Current.SidebarLibraryExpanded;
        settings.Save();
        ApplyLibraryExpanded(animate: true);
    }

    private void ApplyPinnedExpanded(bool animate) =>
        ApplyExpanded(PinnedList, PinnedChevronRotation, App.Services.GetRequiredService<Core.Settings.SettingsStore>().Current.SidebarPinnedExpanded, animate);

    private void ApplyLibraryExpanded(bool animate)
    {
        var expanded = App.Services.GetRequiredService<Core.Settings.SettingsStore>().Current.SidebarLibraryExpanded;
        ApplyExpanded(LibraryList, LibraryChevronRotation, expanded, animate);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(LibrarySectionHeader, expanded ? "Library, expanded" : "Library, collapsed");
    }

    private static void ApplyExpanded(UIElement list, Microsoft.UI.Xaml.Media.RotateTransform chevron, bool expanded, bool animate)
    {
        list.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        var angle = expanded ? 0 : -90;
        if (!animate || !Helpers.Motion.IsEnabled)
        {
            chevron.Angle = angle;
            return;
        }
        var turn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = angle,
            Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Standard),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(turn, chevron);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(turn, "Angle");
        story.Children.Add(turn);
        story.Begin();
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_syncingSearch || args.Reason == AutoSuggestionBoxTextChangeReason.SuggestionChosen) return;
        var vm = App.Services.GetRequiredService<SearchViewModel>();
        if (_navigation.CurrentPage != typeof(SearchPage) && !string.IsNullOrWhiteSpace(sender.Text))
            _navigation.Navigate(typeof(SearchPage));
        vm.Query = sender.Text;
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_navigation.CurrentPage != typeof(SearchPage)) _navigation.Navigate(typeof(SearchPage));
        var vm = App.Services.GetRequiredService<SearchViewModel>();
        vm.Query = args.QueryText;
    }

    public void ToggleQueue() => _panels.Toggle(SidePanel.Queue);

    private void SetQueueOpen(bool open)
    {
        if (!open) _panels.Open = SidePanel.None;
    }

    private void OnRailScrollSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var height = Math.Max(0, RailScroll.ActualHeight);
        if (Math.Abs(Rail.MinHeight - height) > 0.5) Rail.MinHeight = height;
    }

    private void ApplyQueue()
    {
        PlaceQueue();
        ContentSurface.Margin = new Thickness(0, 0, _queueOpen && QueueBeside ? QueueGlass.Width + 16 : 0, 0);
        SetQueueMotion();
        QueuePanel.Visibility = _panels.Open == SidePanel.Queue ? Visibility.Visible : Visibility.Collapsed;
        LyricsPanel.Visibility = _panels.Open == SidePanel.Lyrics ? Visibility.Visible : Visibility.Collapsed;
        QueueGlass.Visibility = _queueOpen ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetQueueMotion()
    {
        if (!Helpers.Motion.IsEnabled)
        {
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetImplicitShowAnimation(QueueGlass, null);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetImplicitHideAnimation(QueueGlass, null);
            return;
        }
        var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(QueueGlass).Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f));
        Microsoft.UI.Composition.CompositionAnimationGroup Slide(float fromX, float fromOpacity, float toX, float toOpacity, double ms)
        {
            var move = compositor.CreateVector3KeyFrameAnimation();
            move.Target = "Translation";
            move.InsertKeyFrame(0, new System.Numerics.Vector3(fromX, 0, 0));
            move.InsertKeyFrame(1, new System.Numerics.Vector3(toX, 0, 0), ease);
            move.Duration = TimeSpan.FromMilliseconds(ms);
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.Target = "Opacity";
            fade.InsertKeyFrame(0, fromOpacity);
            fade.InsertKeyFrame(1, toOpacity, ease);
            fade.Duration = TimeSpan.FromMilliseconds(ms);
            var group = compositor.CreateAnimationGroup();
            group.Add(move);
            group.Add(fade);
            return group;
        }
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetIsTranslationEnabled(QueueGlass, true);
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetImplicitShowAnimation(QueueGlass, Slide(32, 0, 0, 1, Helpers.Motion.Emphasized));
        Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetImplicitHideAnimation(QueueGlass, Slide(0, 1, 32, 0, Helpers.Motion.Standard));
    }

    private void OnContentPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_queueOpen || QueueBeside) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { Tag: "SidePanelToggle" }) return;
        SetQueueOpen(false);
    }

    private void AddKeyboardShortcuts()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action action)
        {
            var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
            acc.Invoked += (_, a) =>
            {
                if (mods == VirtualKeyModifiers.Control && key is VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down && IsEditingText()) return;
                action();
                a.Handled = true;
            };
            KeyboardAccelerators.Add(acc);
        }

        Add(VirtualKey.Right, VirtualKeyModifiers.Control, () => _player.NextCommand.Execute(null));
        Add(VirtualKey.Left, VirtualKeyModifiers.Control, () => _player.PreviousCommand.Execute(null));
        Add(VirtualKey.Up, VirtualKeyModifiers.Control, () => _player.VolumeUpCommand.Execute(null));
        Add(VirtualKey.Down, VirtualKeyModifiers.Control, () => _player.VolumeDownCommand.Execute(null));
        Add(VirtualKey.P, VirtualKeyModifiers.Control, () => _player.PlayPauseCommand.Execute(null));
        Add(VirtualKey.S, VirtualKeyModifiers.Control, () => _player.ToggleShuffleCommand.Execute(null));
        Add(VirtualKey.R, VirtualKeyModifiers.Control, () => _player.CycleRepeatCommand.Execute(null));
        Add(VirtualKey.M, VirtualKeyModifiers.Control, () => _player.ToggleMuteCommand.Execute(null));
        Add(VirtualKey.Q, VirtualKeyModifiers.Control, ToggleQueue);
        Add(VirtualKey.L, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => _panels.Toggle(SidePanel.Lyrics));
        Add(VirtualKey.F11, VirtualKeyModifiers.None, () => App.Services.GetRequiredService<StageService>().Toggle());
        Add(VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => App.Services.GetRequiredService<MiniPlayerService>().Toggle());
        Add(VirtualKey.F, VirtualKeyModifiers.Control, OpenSearch);
        Add(VirtualKey.L, VirtualKeyModifiers.Control, () => _navigation.Navigate(typeof(NowPlayingPage)));
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, () => _navigation.GoBack());
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private bool IsEditingText()
    {
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is TextBox or AutoSuggestBox or PasswordBox or RichEditBox) return true;
        return false;
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && App.Services.GetRequiredService<StageService>() is { IsOn: true } stage)
        {
            stage.Exit();
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Escape && _queueOpen && !IsEditingText())
        {
            SetQueueOpen(false);
            e.Handled = true;
            return;
        }
        if (e.Key != VirtualKey.Space) return;
        for (var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; focused is not null; focused = VisualTreeHelper.GetParent(focused))
            if (focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox or ButtonBase or ToggleSwitch or ComboBox or ListViewItem or GridViewItem or Slider) return;
        _player.PlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed)
        {
            _navigation.GoBack();
            e.Handled = true;
        }
    }
}
