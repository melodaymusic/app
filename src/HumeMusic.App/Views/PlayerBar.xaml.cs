using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace HumeMusic.Views;

/// <summary>Persistent playback controls shown at the bottom of the window.</summary>
public sealed partial class PlayerBar : UserControl
{
    public PlayerBar()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        // Playback controls stay in shades of white whatever colour the page has taken.
        Accent.InstallNeutral(Resources);
        InitializeComponent();
        SeekSliderHelper.Attach(ProgressSlider, Player);
        _panels.PropertyChanged += (_, _) => SyncPanelButtons();
        // The bar stretches to the space it's given, so its state can't change its own width.
        SizeChanged += (_, e) => VisualStateManager.GoToState(this,
            e.NewSize.Width >= UiScale.Size(860) ? "WideState" : e.NewSize.Width >= UiScale.Size(700) ? "MediumState"
            : e.NewSize.Width >= UiScale.Size(640) ? "CompactState" : e.NewSize.Width >= UiScale.Size(520) ? "TightState" : "NarrowState", false);
    }

    public PlayerViewModel Player { get; }

    private readonly PanelService _panels = App.Services.GetRequiredService<PanelService>();
    private bool _syncingPanel;

    /// <summary>
    /// The queue and lyrics buttons show which panel is open (lit only then). They follow the panel
    /// service, so they can never disagree with what is on screen.
    /// </summary>
    private void SyncPanelButtons()
    {
        _syncingPanel = true;
        QueueToggle.IsChecked = _panels.Open == SidePanel.Queue;
        LyricsToggle.IsChecked = _panels.Open == SidePanel.Lyrics;
        _syncingPanel = false;
    }

    /// <summary>Gives the floating player a soft elevation shadow over the content behind it.</summary>
    public void CastShadowOnto(UIElement receiver)
    {
        Helpers.Elevation.Apply(Pill, receiver, 24);
    }

    // Checked/Unchecked (not Click) so keyboard, mouse and UI Automation all reach it. The button's own
    // flip is only a request: the panel service decides, and the buttons are set to match.
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

/// <summary>
/// Makes a position slider seek once when the user releases it, instead of on every pixel of a drag.
/// </summary>
public static class SeekSliderHelper
{
    public static void Attach(Slider slider, PlayerViewModel player)
    {
        slider.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, _) => player.BeginSeek()), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, _) => player.CommitSeek(slider.Value)), handledEventsToo: true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) => player.CommitSeek(slider.Value)), handledEventsToo: true);
    }
}
