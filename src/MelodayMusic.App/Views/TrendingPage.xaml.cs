using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class TrendingPage : Page
{
    private readonly TrendingService _service = App.Services.GetRequiredService<TrendingService>();
    private readonly ILogger<TrendingPage> _log = App.Services.GetRequiredService<ILogger<TrendingPage>>();
    private bool _artistMode, _setting;
    private bool _active, _loading, _playing;
    private int _loadVersion;
    private IReadOnlyList<Track> _tracks = [];
    private IReadOnlyList<Artist> _artists = [];
    public TrendingPage()
    {
        InitializeComponent();
        ChartSearch.RegisterPropertyChangedCallback(Controls.PageSearch.TextProperty, (_, _) => ApplySearch());
        ChartPlexSync.RegisterPropertyChangedCallback(Controls.PlexSyncButton.IsOnProperty, async (_, _) =>
        {
            if (_setting) return;
            await _service.SetKeepOnPlexAsync(ChartPlexSync.IsOn);
            UpdateStatus();
        });
        Loaded += (_, _) => { _active = true; _service.Changed += OnChanged; };
        Unloaded += (_, _) => { _active = false; _loadVersion++; _service.Changed -= OnChanged; };
    }
    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (!_service.IsAvailable)
        {
            DispatcherQueue.TryEnqueue(() => App.Services.GetRequiredService<NavigationService>().Navigate(typeof(HomePage)));
            return;
        }
        _active = true;
        _artistMode = e.Parameter as string == "artists";
        Header.Title = _artistMode ? "Last.fm Trending Artists" : "Last.fm Top 100 Tracks";
        Header.Visibility = _artistMode ? Visibility.Visible : Visibility.Collapsed;
        ChartHeader.Visibility = _artistMode ? Visibility.Collapsed : Visibility.Visible;
        ChartHeader.Item = TrendingService.Playlist;
        ChartHeader.Description = TrendingService.Playlist.Summary;
        ChartHeader.Artwork = _service.CoverPath;
        ChartHeader.PlaceholderIcon = "ListMusic";
        Tracks.Visibility = _artistMode ? Visibility.Collapsed : Visibility.Visible;
        Artists.Visibility = _artistMode ? Visibility.Visible : Visibility.Collapsed;
        PlexSync.Visibility = _artistMode ? Visibility.Collapsed : Visibility.Visible;
        State.SkeletonKind = _artistMode ? Controls.SkeletonKind.Tiles : Controls.SkeletonKind.Tracks;
        State.SkeletonRound = _artistMode;
        State.EmptyText = _artistMode ? "No trending artists match your library yet." : "No chart tracks match your library yet.";
        await LoadAsync();
    }
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _loadVersion++;
        base.OnNavigatedFrom(e);
    }
    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () =>
        { if (_active) await LoadAsync(quiet: true); });
    private void UpdateStatus()
    {
        _setting = true; ChartPlexSync.IsOn = _service.KeepOnPlex; _setting = false;
        ChartHeader.Item = TrendingService.Playlist;
        ChartHeader.Artwork = _service.CoverPath;
        ChartHeader.Meta = Helpers.Format.Count(_tracks.Count, "track", "tracks") + " · " + Helpers.Format.LongDuration(TimeSpan.FromMilliseconds(_tracks.Sum(t => t.DurationMs)));
        ChartHeader.Description = TrendingService.Playlist.Summary;
        ChartHeader.StatusText = _service.PlexError is { } problem ? "Couldn't update Plex: " + problem : "Updates every six hours";
        Header.Subtitle = !_artistMode && _service.PlexError is { } error ? $"Couldn't update Plex: {error}"
            : $"{(_artistMode ? Helpers.Format.Count(_artists.Count, "artist", "artists") : Helpers.Format.Count(_tracks.Count, "track", "tracks"))} · Updates every six hours";
    }
    private async Task ShowSpinnerAsync(int version)
    {
        await Task.Delay(150);
        if (_active && _loading && version == _loadVersion) State.IsLoading = true;
    }
    private async Task LoadAsync(bool quiet = false, bool force = false)
    {
        if (!_active) return;
        if (!_service.IsAvailable)
        {
            App.Services.GetRequiredService<NavigationService>().Navigate(typeof(HomePage));
            return;
        }
        var version = ++_loadVersion;
        var artistMode = _artistMode;
        _loading = !quiet && (artistMode ? _artists.Count : _tracks.Count) == 0;
        State.IsLoading = false;
        State.ErrorMessage = null;
        State.IsEmpty = false;
        if (_loading) _ = ShowSpinnerAsync(version);
        try
        {
            if (force) await _service.RefreshAsync(artistMode);
            var artists = artistMode ? await _service.GetArtistsAsync() : _artists;
            var tracks = !artistMode ? (await _service.GetAsync())?.InLibrary ?? [] : _tracks;
            if (!_active || version != _loadVersion || artistMode != _artistMode) return;
            if (artistMode) { _artists = artists; Artists.ItemsSource = artists; }
            else { _tracks = tracks; ApplySearch(); }
            State.IsEmpty = (_artistMode ? _artists.Count : _tracks.Count) == 0;
            State.ErrorMessage = State.IsEmpty ? (artistMode ? _service.ArtistsError : _service.TracksError) : null;
            PlayAllButton.IsEnabled = !State.IsEmpty && !_playing;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Trending page load failed");
            if (_active && version == _loadVersion) State.ErrorMessage = "Couldn't load trending music. Please try again.";
        }
        finally
        {
            if (version == _loadVersion) { _loading = false; State.IsLoading = false; }
        }
    }
    private async void OnPlay(object sender, RoutedEventArgs e)
    {
        if (_playing) return;
        _playing = true;
        PlayAllButton.IsEnabled = false;
        try
        {
            if (_artistMode) await _service.PlayArtistsAsync(_artists);
            else await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(_tracks);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Trending playback failed");
            App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't play trending music", "Check your Plex connection and try again.");
        }
        finally
        {
            _playing = false;
            PlayAllButton.IsEnabled = (_artistMode ? _artists.Count : _tracks.Count) > 0;
        }
    }
    private void OnArtistClick(object sender, ItemClickEventArgs e)
    { if (e.ClickedItem is Artist artist) App.Services.GetRequiredService<LibraryActions>().Open(artist); }
    private async void OnRetry(object? sender, EventArgs e) => await LoadAsync(force: true);
    private void OnChartPlay(object? sender, EventArgs e) => OnPlay(this, new RoutedEventArgs());
    private async void OnChartShuffle(object? sender, EventArgs e)
    {
        if (_tracks.Count == 0 || _playing) return;
        _playing = true;
        try { await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(_tracks, shuffle: true); }
        catch (Exception ex) { _log.LogWarning(ex, "Chart shuffle failed"); App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't play the chart", "Check your Plex connection."); }
        finally { _playing = false; }
    }
    private void OnTrackContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue) return;
        if (args.Item is Track track && args.ItemContainer.ContentTemplateRoot is Controls.TrackRow row)
        {
            var position = _service.Positions.GetValueOrDefault(track.RatingKey);
            row.SetChartPosition(position?.Rank ?? args.ItemIndex + 1, position?.Movement);
        }
    }
    private void ApplySearch()
    {
        var query = ChartSearch.Text.Trim();
        Tracks.ItemsSource = query.Length == 0 ? _tracks : _tracks.Where(t => t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || t.DisplayArtist.Contains(query, StringComparison.CurrentCultureIgnoreCase) || t.AlbumTitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true).ToList();
    }
}
