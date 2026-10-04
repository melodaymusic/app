using Microsoft.Extensions.Logging;
using System.ComponentModel;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace HumeMusic.Views;

public sealed partial class NavItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required Type Page { get; init; }
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial bool IsSelected { get; set; }
    /// <summary>A picture shown instead of the icon (the listener's Plex avatar on Settings).</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] public partial Microsoft.UI.Xaml.Media.ImageSource? Picture { get; set; }
    /// <summary>The name behind the picture, for its initials while it loads.</summary>
    public string? PictureName { get; set; }
    public bool HasPicture => Picture is not null;
    partial void OnPictureChanged(Microsoft.UI.Xaml.Media.ImageSource? value) => OnPropertyChanged(nameof(HasPicture));
    // Used by the list's automation peer, so screen readers announce the label.
    public override string ToString() => Label;
}

/// <summary>Main application shell: navigation rail, content frame, queue pane and player bar.</summary>
public sealed partial class ShellView : UserControl
{
    private readonly NavigationService _navigation;
    private readonly PlayerViewModel _player;
    private readonly List<NavItem> _items =
    [
        new() { Label = "Home", Icon = "House", Page = typeof(HomePage) },
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
        new() { Label = "Downloads", Icon = "CircleArrowDown", Page = typeof(DownloadsPage) },
    ];
    private readonly List<NavItem> _footer =
    [
        new() { Label = "Settings", Icon = "Settings", Page = typeof(SettingsPage) },
    ];
    private bool _syncingSearch;

    public ShellView()
    {
        Shell = App.Services.GetRequiredService<ShellViewModel>();
        Notifications = App.Services.GetRequiredService<NotificationService>();
        Ambient = App.Services.GetRequiredService<AmbientService>();
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _player = App.Services.GetRequiredService<PlayerViewModel>();
        _panels = App.Services.GetRequiredService<PanelService>();
        InitializeComponent();
        _panels.PropertyChanged += (_, _) => ApplyQueue();

        NavList.ItemsSource = _items;
        LibraryList.ItemsSource = _library;
        FooterList.ItemsSource = _footer;
        ShowAccountPicture();
        PlayerBar.CastShadowOnto(ContentFrame);
        Unloaded += OnUnloaded;
        Loaded += OnLoaded;
        Loaded += (_, _) => ApplyPinnedExpanded(animate: false);
        PlayerBar.SizeChanged += (_, _) => PlaceQueue();
        // Crossing the wide width moves the queue beside the page or over it (after layout, never inside it).
        SizeChanged += (_, e) =>
        {
            if ((e.PreviousSize.Width >= WideQueueWidth) != (e.NewSize.Width >= WideQueueWidth))
                DispatcherQueue.TryEnqueue(ApplyQueue);
        };
        // Pressing the page closes a floating queue, as a popover would (the queue button toggles it itself).
        ContentSurface.AddHandler(PointerPressedEvent, new PointerEventHandler(OnContentPressed), true);
        AddKeyboardShortcuts();
        PreviewKeyDown += OnPreviewKeyDown;
        PointerPressed += OnPointerPressed;
    }

    // From this window width the queue sits beside the page rather than floating over it.
    private const double WideQueueWidth = 1600;
    private readonly PanelService _panels;
    private bool _queueOpen => _panels.Open != SidePanel.None;

    private bool QueueBeside => ActualWidth >= WideQueueWidth;

    /// <summary>
    /// Floating over the page, the queue ends above the player bar rather than covering it; beside
    /// the page (a very wide window) it runs the full height, as the bar sits under the page alone.
    /// </summary>
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

    // Subscriptions to app-wide services live only while the shell is on screen, so a shell that is
    // replaced (sign-out, interface size change) stops receiving events and can be collected.
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _navigation.Attach(ContentFrame);
        _navigation.Navigated -= OnNavigated;
        _navigation.Navigated += OnNavigated;
        Shell.PropertyChanged -= OnShellChanged;
        Shell.PropertyChanged += OnShellChanged;
        // Start with focus in the sidebar, marked as pointer focus: left to itself WinUI gives initial
        // focus to the first thing on the page and draws a keyboard focus box around it before the
        // listener has touched the keyboard. Keyboard focus still shows as soon as Tab is used.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => NavList.Focus(FocusState.Pointer));
        if (ContentFrame.Content is not null) return;
        if (!_navigation.RestorePending()) _navigation.NavigateRoot(typeof(HomePage));
    }

    /// <summary>Settings shows the listener's Plex avatar in place of the gear, when they have one.</summary>
    private void ShowAccountPicture()
    {
        var account = App.Services.GetRequiredService<Core.Services.PlexSession>().Account;
        if (account?.Thumb is not { Length: > 0 } thumb || !Uri.TryCreate(thumb, UriKind.Absolute, out var uri)) return;
        var settings = _footer[0];
        settings.PictureName = account.Name;
        settings.Picture = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) { DecodePixelWidth = 64, DecodePixelType = Microsoft.UI.Xaml.Media.Imaging.DecodePixelType.Logical };
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _navigation.Navigated -= OnNavigated;
        Shell.PropertyChanged -= OnShellChanged;
    }

    private void OnShellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.LibraryVersion) && IsLoaded)
        {
            // A different library was selected: start again from Home.
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

        // The Now Playing page has its own full transport controls.
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

    // ------------------------------------------------------------------ search field

    private bool _searchOpen;
    private Storyboard? _searchAnimation;

    private void OnSearchButtonClick(object sender, RoutedEventArgs e) => OpenSearch();

    /// <summary>Opens the search field from its circle and puts the cursor in it at once.</summary>
    private void OpenSearch()
    {
        if (!_searchOpen)
        {
            _searchOpen = true;
            SearchButton.Visibility = Visibility.Collapsed;
            SearchBox.Visibility = Visibility.Visible;
            AnimateSearch(UiScale.Size(480, 300), 1);
        }
        // The field has only just become visible: give it the cursor once it has been laid out (the
        // clicked circle collapsing would otherwise leave focus nowhere), so typing starts at once.
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

    /// <summary>Folds the field back into its circle when it's empty and search isn't open.</summary>
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
        // Focus moves into the suggestion list while typing; only fold up once focus has truly left.
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

    // ------------------------------------------------------------------ pinned section

    public HomeViewModel Home { get; } = App.Services.GetRequiredService<HomeViewModel>();

    private void OnPinnedNavClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Core.Models.MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    /// <summary>The pins were dragged into a new order: it is kept.</summary>
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

    /// <summary>Opens or closes the sidebar's Pinned list; the chevron points down when open.</summary>
    private void ApplyPinnedExpanded(bool animate)
    {
        var expanded = App.Services.GetRequiredService<Core.Settings.SettingsStore>().Current.SidebarPinnedExpanded;
        PinnedList.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        var angle = expanded ? 0 : -90;
        if (!animate || !Helpers.Motion.IsEnabled)
        {
            PinnedChevronRotation.Angle = angle;
            return;
        }
        var turn = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            To = angle,
            Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Standard),
            EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
        };
        var story = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(turn, PinnedChevronRotation);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(turn, "Angle");
        story.Children.Add(turn);
        story.Begin();
    }

    // ------------------------------------------------------------------ search

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

    // ------------------------------------------------------------------ queue pane

    /// <summary>The queue button and Ctrl+Q: open the queue, or close it.</summary>
    public void ToggleQueue() => _panels.Toggle(SidePanel.Queue);

    /// <summary>Closing only: a side panel can't be "set open" without saying which.</summary>
    private void SetQueueOpen(bool open)
    {
        if (!open) _panels.Open = SidePanel.None;
    }

    /// <summary>
    /// Shows or hides the queue (sliding in from the right and fading), beside the page in a very
    /// wide window (the page makes room) or floating over it. The queue button's dot always follows.
    /// </summary>
    /// <summary>The rail is at least as tall as its panel (Settings sits at the bottom); taller, it scrolls.</summary>
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

    /// <summary>A press anywhere on the page (but the queue button) closes the floating queue.</summary>
    private void OnContentPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_queueOpen || QueueBeside) return;
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is FrameworkElement { Tag: "SidePanelToggle" }) return;
        SetQueueOpen(false);
    }

    // ------------------------------------------------------------------ keyboard

    private void AddKeyboardShortcuts()
    {
        void Add(VirtualKey key, VirtualKeyModifiers mods, Action action)
        {
            var acc = new KeyboardAccelerator { Key = key, Modifiers = mods };
            acc.Invoked += (_, a) => { action(); a.Handled = true; };
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
        Add(VirtualKey.M, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, () => App.Services.GetRequiredService<MiniPlayerService>().Toggle());
        Add(VirtualKey.F, VirtualKeyModifiers.Control, OpenSearch);
        Add(VirtualKey.L, VirtualKeyModifiers.Control, () => _navigation.Navigate(typeof(NowPlayingPage)));
        Add(VirtualKey.Left, VirtualKeyModifiers.Menu, () => _navigation.GoBack());
        KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape && _queueOpen)
        {
            SetQueueOpen(false);
            e.Handled = true;
            return;
        }
        // Space toggles playback unless the user is typing or operating a control that uses Space.
        if (e.Key != VirtualKey.Space) return;
        var focused = FocusManager.GetFocusedElement(XamlRoot);
        if (focused is TextBox or AutoSuggestBox or PasswordBox or RichEditBox or ButtonBase or ToggleSwitch or ComboBox or ListViewItem or GridViewItem or Slider)
            return;
        _player.PlayPauseCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Mouse back button.
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsXButton1Pressed)
        {
            _navigation.GoBack();
            e.Handled = true;
        }
    }
}
