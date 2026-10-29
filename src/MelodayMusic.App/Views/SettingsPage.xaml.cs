using MelodayMusic.Controls;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        Layout = App.Services.GetRequiredService<Services.HomeLayoutService>();
        InitializeComponent();
        SetUpSections();
    }

    private static readonly ChipTab[] Sections =
    [
        new("general", "General", "Settings"),
        new("appearance", "Appearance", "Palette"),
        new("home", "Home", "House"),
        new("playback", "Playback", "AudioWaveform"),
        new("meloday", "Meloday", "Sparkles"),
        new("analysis", "Audio Analysis", "AudioWaveform"),
        new("connections", "Connections", "Server"),
        new("about", "About", "Info"),
    ];

    private void SetUpSections()
    {
        var settings = App.Services.GetRequiredService<Core.Settings.SettingsStore>();
        SectionNav.ItemsSource = Sections;
        CompactSectionNav.ItemsSource = Sections;
        var open = Sections.FirstOrDefault(t => t.Key == SectionKey(settings.Current.SettingsTab)) ?? Sections[0];
        SectionNav.SelectedItem = open;
        CompactSectionNav.SelectedItem = open;
        ShowSection(open.Key, resetScroll: false);
        SectionNav.SelectionChanged += (_, _) =>
        {
            if (SectionNav.SelectedItem is not ChipTab tab) return;
            CompactSectionNav.SelectedItem = tab;
            SwitchSection(tab.Key);
            settings.Current.SettingsTab = tab.Key;
            settings.Save();
        };
    }

    private void OnCompactSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CompactSectionNav.SelectedItem is ChipTab tab && !ReferenceEquals(SectionNav.SelectedItem, tab))
            SectionNav.SelectedItem = tab;
    }

    private void OnSettingsSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (SectionNav is null || SettingsScroller is null || CompactSectionNav is null) return;
        var compact = e.NewSize.Width < Services.UiScale.Size(600);
        SectionNav.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactSectionNav.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        SettingsLayoutRoot.ColumnDefinitions[0].Width = compact ? new GridLength(0) : new GridLength(168);
        SettingsLayoutRoot.ColumnSpacing = compact ? 0 : Services.UiScale.Size(24);
        Grid.SetColumn(SettingsScroller, compact ? 0 : 1);
        Grid.SetColumnSpan(SettingsScroller, compact ? 2 : 1);
    }

    private int _switch;

    private UIElement[] Groups => [AppearanceGroup, BackgroundGroup, GeneralGroup, YourListeningGroup, StorageGroup, HomeGroup,
        AccountGroup, PlexPassGroup, LibraryGroup, LastFmGroup, MusicBrainzGroup, ListenBrainzGroup, ServicesGroup, ListeningGroup, PlaybackGroup,
        TasteGroup, MelodayGroup, LiveRadioGroup, AnalysisGroup, DupesGroup, MutingGroup, ArtistGroup, AboutGroup];

    private static string SectionKey(string? key) => key switch
    {
        "plex" => "connections", "artists" => "appearance", "dupes" or "muting" => "meloday", _ => key ?? "general",
    };

    private void SwitchSection(string key)
    {
        var version = ++_switch;
        foreach (var group in Groups) Reset(group);
        var leaving = Groups.Where(g => g.Visibility == Visibility.Visible).ToList();
        if (!Helpers.Motion.IsEnabled || leaving.Count == 0)
        {
            ShowSection(key, resetScroll: true);
            return;
        }
        var first = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(leaving[0]);
        var compositor = first.Compositor;
        var batch = compositor.CreateScopedBatch(Microsoft.UI.Composition.CompositionBatchTypes.Animation);
        foreach (var group in leaving)
        {
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(1, 0, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(1f, 1f)));
            fade.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Quick);
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(group).StartAnimation("Opacity", fade);
        }
        batch.Completed += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (version != _switch) return;
            foreach (var group in leaving) Helpers.Anim.SuppressExit(group);
            ShowSection(key, resetScroll: true);
            foreach (var group in Groups)
            {
                var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(group);
                visual.StopAnimation("Opacity");
                Helpers.Anim.RestoreAppear(group);
                if (group.Visibility != Visibility.Visible) { visual.Opacity = 1; continue; }
                visual.Opacity = 0;
                var appear = compositor.CreateScalarKeyFrameAnimation();
                appear.InsertKeyFrame(0, 0);
                appear.InsertKeyFrame(1, 1, compositor.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.2f, 0f), new System.Numerics.Vector2(0f, 1f)));
                appear.Duration = TimeSpan.FromMilliseconds(Helpers.Motion.Standard);
                visual.StartAnimation("Opacity", appear);
            }
            DispatcherQueue.TryEnqueue(async () =>
            {
                await Task.Delay((int)Helpers.Motion.Standard + 40);
                if (version != _switch) return;
                foreach (var group in Groups) Reset(group);
            });
        });
        batch.End();
    }

    private static void Reset(UIElement group)
    {
        var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(group);
        visual.StopAnimation("Opacity");
        visual.Opacity = 1;
    }

    private void ShowSection(string key, bool resetScroll)
    {
        void Show(UIElement group, params string[] sections) =>
            group.Visibility = sections.Contains(key) ? Visibility.Visible : Visibility.Collapsed;
        Show(AppearanceGroup, "appearance");
        Show(BackgroundGroup, "general");
        Show(GeneralGroup, "general");
        Show(YourListeningGroup, "general");
        Show(StorageGroup, "general");
        Show(HomeGroup, "home");
        Show(AccountGroup, "connections");
        Show(PlexPassGroup, "connections");
        Show(LibraryGroup, "connections");
        Show(LastFmGroup, "connections");
        Show(MusicBrainzGroup, "connections");
        Show(ListenBrainzGroup, "connections");
        Show(ServicesGroup, "connections");
        Show(ListeningGroup, "connections");
        Show(PlaybackGroup, "playback");
        Show(TasteGroup, "meloday");
        Show(MelodayGroup, "meloday");
        Show(LiveRadioGroup, "meloday");
        Show(AnalysisGroup, "analysis");
        Show(DupesGroup, "meloday");
        Show(MutingGroup, "meloday");
        Show(ArtistGroup, "appearance");
        Show(AboutGroup, "about");
        if (resetScroll) SettingsScroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    public SettingsViewModel ViewModel { get; }
    private void OnQuit(object sender, RoutedEventArgs e) => App.MainWindow?.Quit();
    private async void OnCompareMusicEngines(object sender, RoutedEventArgs e) => await MusicEngineComparisonDialog.ShowAsync(XamlRoot);

    public Services.HomeLayoutService Layout { get; }

    private async void OnSaveFanartKey(object sender, RoutedEventArgs e)
    {
        var key = FanartKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (await ViewModel.SaveLogoKeyAsync(key)) FanartKeyBox.Password = string.Empty;
    }

    private async void OnImportLogos(object sender, RoutedEventArgs e)
    {
        if (App.MainWindow is not { } window) return;
        var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
        };
        if (await picker.PickSingleFolderAsync() is { Path: { Length: > 0 } folder }) await ViewModel.ImportLogosAsync(folder);
    }

    private async void OnSaveOpenAiKey(object sender, RoutedEventArgs e)
    {
        var key = OpenAiKeyBox.Password;
        if (string.IsNullOrWhiteSpace(key)) return;
        if (await ViewModel.SaveAiKeyAsync(key)) OpenAiKeyBox.Password = string.Empty;
    }

    private async void OnConnectLastFm(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.ConnectLastFmAsync(LastFmKeyBox.Password, LastFmSecretBox.Password))
        {
            LastFmKeyBox.Password = string.Empty;
            LastFmSecretBox.Password = string.Empty;
        }
    }

    private async void OnSaveLastFmKey(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.IsEnabled = false;
        try { if (await ViewModel.SaveLastFmKeyAsync(LastFmKeyBox.Password)) LastFmKeyBox.Password = ""; }
        finally { button.IsEnabled = true; }
    }

    private async void OnConnectListenBrainz(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.IsEnabled = false;
        try { if (await ViewModel.ConnectListenBrainzAsync(ListenBrainzTokenBox.Password)) ListenBrainzTokenBox.Password = ""; }
        finally { button.IsEnabled = true; }
    }

    private void OnOpenAiKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnSaveOpenAiKey(sender, e);
    }

    private void OnSkipGenresDone(object sender, RoutedEventArgs e) => ViewModel.SaveMelodaySkipGenres();

    private void OnSkipGenresKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.SaveMelodaySkipGenres();
    }

    private void OnAvoidWordsDone(object sender, RoutedEventArgs e) => ViewModel.SaveMelodayAvoidWords();

    private void OnAvoidWordsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.SaveMelodayAvoidWords();
    }

    private void OnFanartKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        OnSaveFanartKey(sender, e);
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSection(sender, -1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSection(sender, 1);

    private void MoveSection(object sender, int direction)
    {
        if ((sender as FrameworkElement)?.Tag is not Services.HomeSection section) return;
        var index = Layout.Sections.IndexOf(section);
        Layout.Move(index, index + direction);
    }

    private void OnUnmute(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.MuteRow row) ViewModel.UnmuteCommand.Execute(row);
    }

    private void OnUnmerge(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ViewModels.DupeRow row) ViewModel.UnmergeCommand.Execute(row);
    }

    private const double DragEdge = 96, DragSpeed = 24;
    private DispatcherTimer? _dragScroll;
    private double _dragY = double.NaN;
    private DateTime _lastDragStep;

    private void OnSectionsDragStarting(object sender, DragItemsStartingEventArgs e)
    {
        _dragY = double.NaN;
        if (_dragScroll is null)
        {
            _dragScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _dragScroll.Tick += (_, _) => ScrollWhileDragging();
            AddHandler(DragOverEvent, new DragEventHandler((_, args) =>
            {
                _dragY = args.GetPosition(SettingsScroller).Y;
                ScrollWhileDragging();
            }), true);
        }
        AllowDrop = true;
        SettingsScroller.AllowDrop = true;
        _dragScroll.Start();
    }

    private void ScrollWhileDragging()
    {
        if (double.IsNaN(_dragY) || DateTime.UtcNow - _lastDragStep < TimeSpan.FromMilliseconds(14)) return;
        _lastDragStep = DateTime.UtcNow;
        var height = SettingsScroller.ViewportHeight;
        var depth = _dragY < DragEdge ? _dragY - DragEdge : _dragY > height - DragEdge ? _dragY - (height - DragEdge) : 0;
        if (depth == 0) return;
        var step = Math.Clamp(depth / DragEdge, -1.5, 1.5) * DragSpeed;
        var offset = Math.Clamp(SettingsScroller.VerticalOffset + step, 0, SettingsScroller.ScrollableHeight);
        SettingsScroller.ChangeView(null, offset, null, true);
    }

    private void OnSectionsReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        _dragScroll?.Stop();
        _dragY = double.NaN;
        AllowDrop = false;
        SettingsScroller.AllowDrop = false;
        Layout.SaveOrder();
    }

    private void OnResetHome(object sender, RoutedEventArgs e) => Layout.Reset();

    private async void OnSupport(object sender, RoutedEventArgs e) => await Windows.System.Launcher.LaunchUriAsync(new Uri("https://ko-fi.com/dvdbsh"));

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
        if (e.Parameter is string key && Sections.FirstOrDefault(tab => tab.Key == SectionKey(key)) is { } section)
        {
            SectionNav.SelectedItem = section;
            if (key is "muting" or "dupes")
                DispatcherQueue.TryEnqueue(async () =>
                {
                    await Task.Delay((int)(Helpers.Motion.Standard + Helpers.Motion.Quick + 50));
                    (key == "muting" ? MutingGroup : DupesGroup).StartBringIntoView();
                });
        }
    }
}
