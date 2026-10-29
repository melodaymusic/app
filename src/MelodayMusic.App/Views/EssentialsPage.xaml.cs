using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace MelodayMusic.Views;

public sealed partial class EssentialsPage : Page
{
    private readonly EssentialsService _service = App.Services.GetRequiredService<EssentialsService>();
    private readonly ILogger<EssentialsPage> _log = App.Services.GetRequiredService<ILogger<EssentialsPage>>();
    private Playlist? _playlist;
    private Artist? _artist;
    private IReadOnlyList<Track> _tracks = [];
    private bool _active, _playing;
    private int _version;

    public EssentialsPage()
    {
        InitializeComponent();
        Search.RegisterPropertyChangedCallback(Controls.PageSearch.TextProperty, (_, _) => ApplySearch());
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _active = true;
        _playlist = e.Parameter as Playlist;
        Header.Item = _playlist;
        Header.Artwork = _playlist?.Thumb;
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _active = false;
        _version++;
        base.OnNavigatedFrom(e);
    }

    private async Task LoadAsync(bool force = false)
    {
        if (_playlist is null || Essentials.ArtistOf(_playlist.RatingKey) is not { } artistKey) return;
        var version = ++_version;
        State.ErrorMessage = null;
        State.IsEmpty = false;
        State.IsLoading = _tracks.Count == 0;
        try
        {
            if (App.Services.GetRequiredService<Core.Services.PlexSession>().Client is not { } client) return;
            var artist = _artist ??= await client.GetArtistAsync(artistKey);
            var list = artist is null ? null : await _service.GetAsync(artist, force);
            if (!_active || version != _version) return;
            if (list is null)
            {
                _tracks = [];
                Tracks.ItemsSource = _tracks;
                State.IsEmpty = true;
                return;
            }
            _playlist = list.Playlist;
            _tracks = list.Tracks;
            Header.Item = list.Playlist;
            Header.Artwork = list.CoverPath;
            ApplySearch();
            UpdateStatus();
        }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogWarning(ex, "Essentials page load failed");
            if (_active && version == _version) State.ErrorMessage = "Couldn't load this playlist. Please try again.";
        }
        finally
        {
            if (version == _version) State.IsLoading = false;
        }
    }

    private void UpdateStatus()
    {
        if (_artist is null) return;
        Header.Meta = Helpers.Format.Count(_tracks.Count, "song", "songs") + " · " + Helpers.Format.LongDuration(TimeSpan.FromMilliseconds(_tracks.Sum(t => t.DurationMs)));
        Header.Description = _playlist?.Summary;
        Header.StatusText = "";
    }

    private async void OnPlay(object? sender, EventArgs e) => await PlayAsync(shuffle: false);

    private async void OnShuffle(object? sender, EventArgs e) => await PlayAsync(shuffle: true);

    private async Task PlayAsync(bool shuffle)
    {
        if (_tracks.Count == 0 || _playing) return;
        _playing = true;
        try { await App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(_tracks, shuffle: shuffle); }
        catch (Exception ex) when (ex is Core.Plex.PlexException or HttpRequestException)
        {
            _log.LogWarning(ex, "Essentials playback failed");
            App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't play this playlist", "Check your Plex connection and try again.");
        }
        finally { _playing = false; }
    }

    private async void OnRetry(object? sender, EventArgs e) => await LoadAsync(force: true);

    private void OnTrackContainer(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not Track track || args.ItemContainer.ContentTemplateRoot is not Controls.TrackRow row) return;
        row.Mode = Controls.TrackRowMode.Ranked;
        row.Rank = _tracks.ToList().IndexOf(track) + 1;
    }

    private void ApplySearch()
    {
        var query = Search.Text.Trim();
        Tracks.ItemsSource = query.Length == 0 ? _tracks : _tracks.Where(t => t.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || t.DisplayArtist.Contains(query, StringComparison.CurrentCultureIgnoreCase) || t.AlbumTitle?.Contains(query, StringComparison.CurrentCultureIgnoreCase) == true).ToList();
    }
}
