using MelodayMusic.Controls;
using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace MelodayMusic.Views;

public sealed partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        Accent.InstallNeutral(Resources);
        InitializeComponent();
        SeekSliderHelper.Attach(ProgressSlider, Player);
        Loaded += (_, _) =>
        {
            _panels.PropertyChanged += OnPanelsChanged;
            _cast.PropertyChanged += OnCastChanged;
            SyncPanelButtons();
            SyncCast();
        };
        Unloaded += (_, _) =>
        {
            _panels.PropertyChanged -= OnPanelsChanged;
            _cast.PropertyChanged -= OnCastChanged;
        };
        SizeChanged += (_, e) => VisualStateManager.GoToState(this,
            e.NewSize.Width >= UiScale.Size(980) ? "WideState" : e.NewSize.Width >= UiScale.Size(820) ? "MediumState"
            : e.NewSize.Width >= UiScale.Size(640) ? "CompactState" : e.NewSize.Width >= UiScale.Size(520) ? "TightState" : "NarrowState", false);
    }

    public PlayerViewModel Player { get; }

    private readonly PanelService _panels = App.Services.GetRequiredService<PanelService>();
    private readonly CastService _cast = App.Services.GetRequiredService<CastService>();
    private MenuFlyoutSubItem? _castMenu;
    private bool _syncingPanel;
    private void OnPanelsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncPanelButtons();

    private void SyncPanelButtons()
    {
        _syncingPanel = true;
        QueueToggle.IsChecked = _panels.Open == SidePanel.Queue;
        _syncingPanel = false;
    }

    // Cast lives in the More menu; the bar only shows it while another device is playing, as a status you can tap.
    private void OnCastChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        SyncCast();
        if (_castMenu is not null) CastButton.Fill(_castMenu.Items, _cast);
    });

    private void SyncCast() => CastToggle.Visibility = _cast.IsCasting ? Visibility.Visible : Visibility.Collapsed;

    private async void OnMore(object sender, RoutedEventArgs e)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.Top };
        var lyrics = new ToggleMenuFlyoutItem { Text = "Lyrics", IsChecked = _panels.Open == SidePanel.Lyrics, KeyboardAcceleratorTextOverride = "Ctrl+Shift+L" };
        lyrics.Click += (_, _) => _panels.Toggle(SidePanel.Lyrics);
        menu.Items.Add(lyrics);
        var mini = new MenuFlyoutItem { Text = "Mini Player", KeyboardAcceleratorTextOverride = "Ctrl+Shift+M" };
        mini.Click += (_, _) => App.Services.GetRequiredService<MiniPlayerService>().Open();
        menu.Items.Add(mini);
        menu.Items.Add(new MenuFlyoutSeparator());
        var cast = new MenuFlyoutSubItem { Text = _cast.Current is { } device ? $"Playing on {device.Name}" : "Play on Another Device" };
        CastButton.Fill(cast.Items, _cast);
        menu.Items.Add(cast);
        _castMenu = cast;
        menu.Closed += (_, _) => { if (ReferenceEquals(_castMenu, cast)) _castMenu = null; };
        menu.ShowAt(MoreButton);
        await _cast.SearchAsync();
        if (ReferenceEquals(_castMenu, cast)) CastButton.Fill(cast.Items, _cast);
    }

    public void CastShadowOnto(UIElement receiver)
    {
        Helpers.Elevation.Apply(Pill, receiver, 24);
    }

    private void OnPanelToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPanel) return;
        _panels.Toggle(SidePanel.Queue);
        SyncPanelButtons();
    }

    private void OnOpenNowPlaying(object sender, RoutedEventArgs e)
    {
        if (Player.HasTrack) App.Services.GetRequiredService<NavigationService>().Navigate(typeof(NowPlayingPage));
    }
}

public static class SeekSliderHelper
{
    public static void Attach(Slider slider, PlayerViewModel player)
    {
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => player.BeginSeek()), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => player.CommitSeek(slider.Value)), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => player.CommitSeek(slider.Value)), handledEventsToo: true);
        slider.PreviewKeyDown += (_, e) => { if (IsSeekKey(e.Key)) player.BeginSeek(); };
        slider.AddHandler(UIElement.KeyUpEvent, new KeyEventHandler((_, e) =>
            { if (IsSeekKey(e.Key)) player.CommitSeek(slider.Value); }), handledEventsToo: true);
        slider.LostFocus += (_, _) => player.CommitSeek(slider.Value);
        slider.Unloaded += (_, _) => player.CancelSeek();
    }

    private static bool IsSeekKey(Windows.System.VirtualKey key) => key is Windows.System.VirtualKey.Left
        or Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down
        or Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End
        or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown;
}
