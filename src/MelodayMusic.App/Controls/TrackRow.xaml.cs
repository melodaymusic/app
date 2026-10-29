using System.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace MelodayMusic.Controls;

public enum TrackRowMode
{
    Library,
    Album,
    Ranked,
}

public sealed partial class TrackRow : UserControl
{
    private PlayerService? _player;
    private bool _chartRow;

    public void SetChartPosition(int rank, int? movement)
    {
        _chartRow = true;
        Mode = TrackRowMode.Ranked;
        Rank = rank;
        RankColumn.Width = new GridLength(UiScale.Size(52, 44));
        MovementText.Visibility = Visibility.Visible;
        MovementText.Text = movement is null ? "NEW" : movement > 0 ? "▲ " + movement : movement < 0 ? "▼ " + Math.Abs(movement.Value) : "—";
        MovementText.Foreground = movement is > 0 ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 74, 190, 116))
            : movement is < 0 ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 235, 97, 97))
            : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["MelodayTextSecondaryBrush"];
        AutomationProperties.SetName(MovementText, movement is null ? "New chart entry" : movement > 0 ? $"Up {movement} places" : movement < 0 ? $"Down {Math.Abs(movement.Value)} places" : "Position unchanged");
    }

    public TrackRow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, e) => ApplyWidth(e.NewSize.Width);
        GotFocus += (_, _) => SetHover(_pointerOver);
        LostFocus += (_, _) => DispatcherQueue.TryEnqueue(() => SetHover(_pointerOver));
    }

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Track), typeof(TrackRow), new PropertyMetadata(null, (d, _) => ((TrackRow)d).Update()));

    public Track? Track
    {
        get => (Track?)GetValue(TrackProperty);
        set => SetValue(TrackProperty, value);
    }

    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(TrackRowMode), typeof(TrackRow), new PropertyMetadata(TrackRowMode.Library, (d, _) => ((TrackRow)d).Update()));

    public TrackRowMode Mode
    {
        get => (TrackRowMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty RankProperty = DependencyProperty.Register(
        nameof(Rank), typeof(int), typeof(TrackRow), new PropertyMetadata(0, (d, _) => ((TrackRow)d).Update()));

    public int Rank
    {
        get => (int)GetValue(RankProperty);
        set => SetValue(RankProperty, value);
    }

    public static readonly DependencyProperty ShowPlaysProperty = DependencyProperty.Register(
        nameof(ShowPlays), typeof(bool), typeof(TrackRow), new PropertyMetadata(false, (d, _) => ((TrackRow)d).ApplyWidth(((TrackRow)d).ActualWidth)));

    public bool ShowPlays
    {
        get => (bool)GetValue(ShowPlaysProperty);
        set => SetValue(ShowPlaysProperty, value);
    }

    public static readonly DependencyProperty ShowTrackArtistProperty = DependencyProperty.Register(
        nameof(ShowTrackArtist), typeof(bool), typeof(TrackRow), new PropertyMetadata(true, (d, _) => ((TrackRow)d).Update()));

    public bool ShowTrackArtist
    {
        get => (bool)GetValue(ShowTrackArtistProperty);
        set => SetValue(ShowTrackArtistProperty, value);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _player ??= App.Services.GetRequiredService<PlayerService>();
        _player.PropertyChanged += OnPlayerChanged;
        UpdatePlaying();
        UpdateRemove(false);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_player is not null) _player.PropertyChanged -= OnPlayerChanged;
    }

    private void OnPlayerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerService.CurrentTrack)) UpdatePlaying();
    }

    private void Update()
    {
        var t = Track;
        if (t is null) return;
        if (IsLoaded) UpdateRemove(false);
        TitleText.Text = t.Title;
        var showArtist = Mode != TrackRowMode.Album || ShowTrackArtist;
        ArtistText.Text = showArtist ? t.DisplayArtist : string.Empty;
        ArtistText.Visibility = showArtist ? Visibility.Visible : Visibility.Collapsed;
        AlbumText.Text = t.AlbumTitle ?? string.Empty;
        Rating.Item = t;
        DurationText.Text = Format.Duration(t.Duration);
        PlaysText.Text = Format.Plays(t.ViewCount);

        var album = Mode == TrackRowMode.Album;
        Art.Visibility = album ? Visibility.Collapsed : Visibility.Visible;
        Art.SourcePath = album ? null : t.DisplayThumb;
        NumberText.Visibility = album ? Visibility.Visible : Visibility.Collapsed;
        NumberText.Text = Format.TrackNumber(t.TrackNumber);
        var ranked = Mode == TrackRowMode.Ranked && Rank > 0;
        RankColumn.Width = ranked ? new GridLength(_chartRow ? Services.UiScale.Size(52, 44) : Services.UiScale.Size(24, 20)) : new GridLength(0);
        RankText.Visibility = ranked ? Visibility.Visible : Visibility.Collapsed;
        RankText.Text = ranked ? Rank.ToString() : string.Empty;
        AutomationProperties.SetName(this, $"{t.Title}, {t.DisplayArtist}, {DurationText.Text}");
        ApplyWidth(ActualWidth);
        UpdatePlaying();
    }

    private void ApplyWidth(double width)
    {
        if (width <= 0) return;
        var showAlbum = Mode != TrackRowMode.Album && width >= 620;
        AlbumColumn.Width = showAlbum ? new GridLength(2, GridUnitType.Star) : new GridLength(0);
        AlbumText.Visibility = showAlbum ? Visibility.Visible : Visibility.Collapsed;
        var showPlays = ShowPlays && width >= 760;
        PlaysColumn.Width = showPlays ? new GridLength(Services.UiScale.Size(84)) : new GridLength(0);
        PlaysText.Visibility = showPlays ? Visibility.Visible : Visibility.Collapsed;
        _showRating = width >= 560;
        RatingColumn.Width = _showRating ? GridLength.Auto : new GridLength(0);
        Rating.Visibility = _showRating ? Visibility.Visible : Visibility.Collapsed;
        UpdateRating(hover: false);
    }

    private void UpdatePlaying()
    {
        var playing = _player?.CurrentTrack is { } current && Track is { } t && current.RatingKey == t.RatingKey;
        TitleText.Foreground = playing
            ? (Brush)Application.Current.Resources["MelodayAccentTextBrush"]
            : (Brush)Application.Current.Resources["MelodayTextPrimaryBrush"];
        PlayingIcon.Visibility = playing && Mode == TrackRowMode.Album ? Visibility.Visible : Visibility.Collapsed;
        if (Mode == TrackRowMode.Album) NumberText.Visibility = playing ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => SetHover(true);

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => SetHover(false);

    private void SetHover(bool on)
    {
        _pointerOver = on;
        on |= HasFocusWithin();
        var overArt = Art.Visibility == Visibility.Visible;
        PlayButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        ArtShade.Visibility = on && overArt ? Visibility.Visible : Visibility.Collapsed;
        PlayGlyph.Foreground = overArt
            ? new SolidColorBrush(Microsoft.UI.Colors.White)
            : (Brush)Application.Current.Resources["MelodayTextPrimaryBrush"];
        MoreButton.Opacity = on ? 1 : 0;
        UpdateRemove(on);
        UpdateRating(on);
        if (on && !overArt)
        {
            NumberText.Visibility = Visibility.Collapsed;
            PlayingIcon.Visibility = Visibility.Collapsed;
        }
        else if (!on)
        {
            NumberText.Visibility = Mode == TrackRowMode.Album ? Visibility.Visible : Visibility.Collapsed;
            UpdatePlaying();
        }
    }

    private bool _showRating;
    private bool _pointerOver;

    private bool HasFocusWithin()
    {
        if (XamlRoot is null) return false;
        for (var node = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; node is not null; node = VisualTreeHelper.GetParent(node))
            if (ReferenceEquals(node, this)) return true;
        return false;
    }

    private void UpdateRating(bool hover)
    {
        if (!_showRating) return;
        Rating.Opacity = hover || Rating.HasRating ? 1 : 0;
        Rating.IsHitTestVisible = hover || Rating.HasRating;
    }

    internal void UpdateRemove(bool on)
    {
        var host = HostList();
        var command = host is not null ? TrackListBehavior.GetRemoveCommand(host) : null;
        var dupe = host is not null ? TrackListBehavior.GetDupeCommand(host) : null;
        var width = command is not null || dupe is not null ? GridLength.Auto : new GridLength(0);
        if (RemoveColumn.Width != width) RemoveColumn.Width = width;
        RemoveButton.Visibility = command is not null ? Visibility.Visible : Visibility.Collapsed;
        DupeButton.Visibility = dupe is not null ? Visibility.Visible : Visibility.Collapsed;
        var active = on || HostContainer()?.IsSelected == true;
        var shown = command is not null && active && command.CanExecute(DataContext);
        RemoveButton.Opacity = shown ? 1 : 0;
        RemoveButton.IsHitTestVisible = shown;
        var dupeShown = dupe is not null && active && dupe.CanExecute(DataContext);
        DupeButton.Opacity = dupeShown ? 1 : 0;
        DupeButton.IsHitTestVisible = dupeShown;
    }

    private void OnDupeClick(object sender, RoutedEventArgs e)
    {
        if (HostList() is { } list && TrackListBehavior.GetDupeCommand(list) is { } command && command.CanExecute(DataContext))
            command.Execute(DataContext);
    }

    private void OnRemoveClick(object sender, RoutedEventArgs e)
    {
        if (HostList() is { } list && TrackListBehavior.GetRemoveCommand(list) is { } command && command.CanExecute(DataContext))
            command.Execute(DataContext);
    }

    private ListViewBase? HostList()
    {
        for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is ListViewBase list) return list;
        return null;
    }

    private Microsoft.UI.Xaml.Controls.Primitives.SelectorItem? HostContainer()
    {
        for (var node = VisualTreeHelper.GetParent(this); node is not null; node = VisualTreeHelper.GetParent(node))
            if (node is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem item) return item;
        return null;
    }

    private void OnPlayClick(object sender, RoutedEventArgs e)
    {
        if (HostList() is { } list && TrackListBehavior.GetPlayCommand(list) is { } play && play.CanExecute(DataContext))
        {
            play.Execute(DataContext);
            return;
        }
        if (Track is { } t) TrackListBehavior.RequestPlay(this, t);
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (Track is not { } t) return;
        var (tracks, index) = TrackListBehavior.GetContext(this, t);
        MediaMenu.Show(MoreButton, t, tracks, index);
    }

    private void OnContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (Track is not { } t) return;
        args.Handled = true;
        var (tracks, index) = TrackListBehavior.GetContext(this, t);
        if (args.TryGetPosition(sender, out var point)) MediaMenu.Show((FrameworkElement)sender, t, tracks, index, point);
        else MediaMenu.Show((FrameworkElement)sender, t, tracks, index);
    }
}
