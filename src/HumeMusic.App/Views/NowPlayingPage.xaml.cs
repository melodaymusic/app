using HumeMusic.Services;
using System.ComponentModel;
using HumeMusic.Helpers;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace HumeMusic.Views;

/// <summary>Full-window Now Playing view with large artwork and complete transport controls.</summary>
public sealed partial class NowPlayingPage : Page
{
    // Layout sizes (effective pixels), following the interface size.
    private static double SidePadding => UiScale.Size(40);
    private static double ColumnGap => UiScale.Size(56);
    private static double MinInfoWidth => UiScale.Size(340, 300);
    private static double MaxInfoWidth => UiScale.Size(500);
    private static double MaxArt => UiScale.Size(600);
    private static double MinSideBySideArt => UiScale.Size(240, 200);


    public NowPlayingPage()
    {
        Player = App.Services.GetRequiredService<PlayerViewModel>();
        // Playback controls stay in shades of white whatever colour the page has taken.
        Accent.InstallNeutral(Resources);
        InitializeComponent();
        SeekSliderHelper.Attach(Progress, Player);
    }

    public PlayerViewModel Player { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Player.PropertyChanged += OnPlayerChanged;
        Panels.PropertyChanged += OnPanelsChanged;
        SyncLyricsButton();
        UpdateVisibility();
        Ambient.ForceDark = true;
        _ = UpdateBackdropAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Player.PropertyChanged -= OnPlayerChanged;
        Panels.PropertyChanged -= OnPanelsChanged;
        _backdropVersion++;
        Ambient.Clear();
    }

    private static AmbientService Ambient => App.Services.GetRequiredService<AmbientService>();
    private int _backdropVersion;

    /// <summary>The page sits on the playing track's UltraBlur, like Plex's own player.</summary>
    private async Task UpdateBackdropAsync()
    {
        var version = ++_backdropVersion;
        var track = App.Services.GetRequiredService<PlayerService>().CurrentTrack;
        var palette = track?.Palette ?? await App.Services.GetRequiredService<ArtworkService>().GetUltraBlurPaletteAsync(track?.AlbumThumb ?? track?.Thumb);
        if (version == _backdropVersion) Ambient.PagePalette = palette;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerViewModel.Track))
        {
            UpdateVisibility();
            _ = UpdateBackdropAsync();
        }
    }

    private void UpdateVisibility()
    {
        var visible = Player.HasTrack ? Visibility.Visible : Visibility.Collapsed;
        Cover.Visibility = visible;
        Info.Visibility = visible;
    }

    /// <summary>
    /// Sizes the artwork and info column from the space actually available so nothing can overflow:
    /// side by side when there is room for a decent cover and a full control column, stacked
    /// (artwork above controls) otherwise.
    /// </summary>
    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        // The page runs under the floating top bar; only the space below it is usable.
        var height = e.NewSize.Height - UiScale.TopInset;
        var available = Math.Max(0, width - SidePadding * 2);

        var info = Math.Clamp(available * 0.46, MinInfoWidth, MaxInfoWidth);
        var art = Math.Min(Math.Min(available - info - ColumnGap, height - 96), MaxArt);

        if (art >= MinSideBySideArt)
        {
            Grid.SetRow(Info, 0);
            Grid.SetColumn(Info, 1);
            Grid.SetRow(UpNextPanel, 1);
            Layout.ColumnSpacing = ColumnGap;
            Cover.HorizontalAlignment = HorizontalAlignment.Left;
            Info.Width = info;
            UpNextPanel.Width = art + ColumnGap + info;
            TitleText.FontSize = UiScale.Text(40);
            TitleText.LineHeight = UiScale.Text(48);
        }
        else
        {
            // Stacked: the cover takes at most half the height so controls stay reachable.
            art = Math.Clamp(Math.Min(available, (height - 96) * 0.5), 160, UiScale.Size(380));
            info = Math.Min(available, MaxInfoWidth);
            Grid.SetRow(Info, 1);
            Grid.SetColumn(Info, 0);
            Grid.SetRow(UpNextPanel, 2);
            Layout.ColumnSpacing = 0;
            Cover.HorizontalAlignment = HorizontalAlignment.Center;
            Info.Width = info;
            UpNextPanel.Width = info;
            TitleText.FontSize = UiScale.Text(32);
            TitleText.LineHeight = UiScale.Text(40);
        }

        Cover.Width = art;
        Cover.Height = art;
        Layout.Margin = new Thickness(SidePadding, 0, SidePadding, 0);
    }

    private static PanelService Panels => App.Services.GetRequiredService<PanelService>();

    private void OnLyricsClick(object sender, RoutedEventArgs e)
    {
        Panels.Toggle(SidePanel.Lyrics);
        SyncLyricsButton();
    }

    private void SyncLyricsButton() => LyricsToggle.IsChecked = Panels.Open == SidePanel.Lyrics;

    private void OnPanelsChanged(object? sender, PropertyChangedEventArgs e) => SyncLyricsButton();

    private void OnUpNextDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is QueueItemViewModel item) Player.JumpToCommand.Execute(item);
    }

    private void OnUpNextKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && sender is ListView { SelectedItem: QueueItemViewModel item })
        {
            Player.JumpToCommand.Execute(item);
            e.Handled = true;
        }
    }
}
