using MelodayMusic.Core.Models;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class MadePlaylistPage : Page
{
    private readonly MadePlaylistService _service = App.Services.GetRequiredService<MadePlaylistService>();
    private readonly ILogger<MadePlaylistPage> _log = App.Services.GetRequiredService<ILogger<MadePlaylistPage>>();
    private Playlist? _playlist;
    private IReadOnlyList<Track> _tracks = [];
    private bool _active, _playing, _syncing;
    private int _version;

    public MadePlaylistPage()
    {
        InitializeComponent();
        Search.RegisterPropertyChangedCallback(Controls.PageSearch.TextProperty, (_, _) => ApplySearch());
        PlexSync.RegisterPropertyChangedCallback(Controls.PlexSyncButton.IsOnProperty, async (_, _) =>
        {
            if (_syncing || _playlist is null || PlexSync.IsOn == _service.KeepsOnPlex(_playlist)) return;
            await _service.SetKeepOnPlexAsync(_playlist, PlexSync.IsOn);
        });
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _playlist = e.Parameter as Playlist;
        _service.Changed += OnChanged;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _version++;
        _service.Changed -= OnChanged;
        base.OnNavigatedFrom(e);
    }

    private void OnChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (!_active || _playlist is null) return;
        _playlist = _service.Playlists.FirstOrDefault(p => p.RatingKey == _playlist.RatingKey) ?? _playlist;
        await LoadAsync();
    });

    private async Task LoadAsync()
    {
        if (_playlist is null) return;
        var version = ++_version;
        Header.Item = _playlist;
        Header.Artwork = _playlist.Thumb;
        _syncing = true;
        PlexSync.IsOn = _service.KeepsOnPlex(_playlist);
        _syncing = false;
        State.ErrorMessage = null;
        State.IsEmpty = false;
        State.IsLoading = _tracks.Count == 0;
        try
        {
            var tracks = await _service.TracksAsync(_playlist);
            if (!_active || version != _version) return;
            _tracks = tracks;
            State.IsEmpty = tracks.Count == 0;
            ApplySearch();
            Header.Meta = Helpers.Format.Count(tracks.Count, "song", "songs") + " · " + Helpers.Format.LongDuration(TimeSpan.FromMilliseconds(tracks.Sum(t => t.DurationMs)));
            Header.Description = _playlist.Summary;
            Header.StatusText = _playlist.UpdatedAt is { } made ? $"Made {made.LocalDateTime:dddd, MMMM d}" : "";
        }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Made by Meloday page load failed");
            if (_active && version == _version) State.ErrorMessage = "Couldn't load this playlist. Please try again.";
        }
        finally
        {
            if (version == _version) State.IsLoading = false;
        }
    }

    private async void OnPlay(object? sender, EventArgs e) => await PlayAsync(shuffle: false);

    private async void OnShuffle(object? sender, EventArgs e) => await PlayAsync(shuffle: true);

    private async Task PlayAsync(bool shuffle)
    {
        if (_tracks.Count == 0 || _playing || _playlist is null) return;
        _playing = true;
        try
        {
            await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(_tracks, shuffle: shuffle);
        }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException)
        {
            _log.LogWarning(ex, "Made by Meloday playback failed");
            App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't play this playlist", "Check your Plex connection and try again.");
        }
        finally { _playing = false; }
    }

    private async void OnRetry(object? sender, EventArgs e) => await LoadAsync();

    private void ApplySearch()
    {
        var query = Search.Text.Trim();
        Tracks.ItemsSource = query.Length == 0 ? _tracks : _tracks.Where(t => t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || t.DisplayArtist.Contains(query, StringComparison.CurrentCultureIgnoreCase) || t.AlbumTitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true).ToList();
    }
}
