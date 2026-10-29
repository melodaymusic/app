using MelodayMusic.Services;
using MelodayMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Views;

public sealed partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        Accent.InstallNeutral(Resources);
        InitializeComponent();
        SeekSliderHelper.Attach(ProgressSlider, Player);
        Loaded += (_, _) => { _panels.PropertyChanged += OnPanelsChanged; SyncPanelButtons(); };
        Unloaded += (_, _) => _panels.PropertyChanged -= OnPanelsChanged;
        SizeChanged += (_, e) => VisualStateManager.GoToState(this,
            e.NewSize.Width >= UiScale.Size(860) ? "WideState" : e.NewSize.Width >= UiScale.Size(700) ? "MediumState"
            : e.NewSize.Width >= UiScale.Size(640) ? "CompactState" : e.NewSize.Width >= UiScale.Size(520) ? "TightState" : "NarrowState", false);
    }

    public PlayerViewModel Player { get; }

    private readonly PanelService _panels = App.Services.GetRequiredService<PanelService>();
    private bool _syncingPanel;
    private void OnPanelsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => SyncPanelButtons();

    private void SyncPanelButtons()
    {
        _syncingPanel = true;
        QueueToggle.IsChecked = _panels.Open == SidePanel.Queue;
        LyricsToggle.IsChecked = _panels.Open == SidePanel.Lyrics;
        _syncingPanel = false;
    }

    public void CastShadowOnto(UIElement receiver)
    {
        Helpers.Elevation.Apply(Pill, receiver, 24);
    }

    private void OnPanelToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_syncingPanel) return;
        _panels.Toggle(ReferenceEquals(sender, LyricsToggle) ? SidePanel.Lyrics : SidePanel.Queue);
        SyncPanelButtons();
    }

    private void OnMiniPlayer(object sender, RoutedEventArgs e) => App.Services.GetRequiredService<MiniPlayerService>().Open();

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
