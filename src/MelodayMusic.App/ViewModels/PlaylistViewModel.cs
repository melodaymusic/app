using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class PlaylistViewModel : ViewModelBase
{
    private List<Track>? _all;
    private int _viewVersion;

    public PlaylistViewModel(PlexSession session, ILogger<PlaylistViewModel> log) : base(session, log)
    {
        SortOptions = Core.Playlists.PlaylistView.Sorts.Select(s => new SortOption(s.Label, s.Key)).ToList();
        SelectedSort = SortOptions[0];
    }

    [ObservableProperty] public partial Playlist? Playlist { get; private set; }
    [ObservableProperty] public partial IncrementalCollection<Track>? Tracks { get; private set; }
    [ObservableProperty] public partial string MetaText { get; private set; } = string.Empty;

    public IReadOnlyList<SortOption> SortOptions { get; }

    [ObservableProperty] public partial SortOption SelectedSort { get; set; }

    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    [ObservableProperty] public partial System.Collections.IList? Shown { get; private set; }

    [ObservableProperty] public partial bool NoMatches { get; private set; }

    private bool IsPlain => SelectedSort.Value == Core.Playlists.PlaylistView.Order && string.IsNullOrWhiteSpace(SearchText);

    public bool CanReorder => Playlist is { IsSmart: false } && IsPlain;

    public System.Windows.Input.ICommand? RemoveCommand => Playlist is { IsSmart: false } ? RemoveTrackCommand : null;

    partial void OnPlaylistChanged(Playlist? value)
    {
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(RemoveCommand));
        RemoveTrackCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedSortChanged(SortOption value) => ViewChanged();

    partial void OnSearchTextChanged(string value) => ViewChanged();

    partial void OnTracksChanged(IncrementalCollection<Track>? value)
    {
        _all = null;
        ViewChanged();
    }

    private void ViewChanged()
    {
        OnPropertyChanged(nameof(CanReorder));
        _ = ApplyViewAsync();
    }

    private async Task ApplyViewAsync()
    {
        var version = ++_viewVersion;
        var tracks = Tracks;
        if (IsPlain || tracks is null || Playlist is null)
        {
            Shown = tracks;
            NoMatches = false;
            return;
        }
        List<Track> all;
        try
        {
            all = await AllTracksAsync(Playlist.RatingKey);
        }
        catch (PlexException ex)
        {
            Log.LogInformation("The whole playlist couldn't be read to sort or search it: {Message}", ex.Message);
            return;
        }
        if (version != _viewVersion || !ReferenceEquals(tracks, Tracks)) return;
        var view = Core.Playlists.PlaylistView.Sort(all.Where(t => Core.Playlists.PlaylistView.Matches(t, SearchText)), SelectedSort.Value).ToList();
        Shown = new System.Collections.ObjectModel.ObservableCollection<Track>(view);
        NoMatches = view.Count == 0 && all.Count > 0;
    }

    private async Task<List<Track>> AllTracksAsync(string playlistKey)
    {
        if (_all is { } known) return known;
        var list = new List<Track>();
        PagedResult<Track> page;
        do
        {
            page = await Client.GetPlaylistItemsAsync(playlistKey, list.Count, 500);
            list.AddRange(page.Items);
        }
        while (page.Items.Count > 0 && list.Count < (page.TotalSize ?? int.MaxValue));
        return _all = list;
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanRemoveTrack))]
    private async Task RemoveTrackAsync(Track? track)
    {
        if (track is null || Playlist is not { } playlist || Shown is not { } shown || track.PlaylistItemId is not { } id) return;
        var index = shown.IndexOf(track);
        if (index < 0) return;
        shown.RemoveAt(index);
        if (!ReferenceEquals(shown, Tracks)) Tracks?.Remove(track);
        _all?.Remove(track);
        IsEmpty = (Tracks?.Count ?? 0) == 0;
        try
        {
            await Client.RemovePlaylistItemAsync(playlist.RatingKey, id);
        }
        catch (PlexException ex)
        {
            Log.LogWarning("Couldn't remove {Track} from {Playlist}: {Message}", track.Title, playlist.Title, ex.Message);
            ErrorMessage = null;
            App.Services.GetRequiredService<Services.NotificationService>().ShowError("Couldn't remove the track", "Plex didn't accept the change.", autoHide: true);
            await LoadAsync(playlist);
        }
    }

    private bool CanRemoveTrack(Track? track) => Playlist is { IsSmart: false } && track?.PlaylistItemId is not null;

    public async Task<bool> SaveMoveAsync(Track moved)
    {
        if (Playlist is not { } playlist || Tracks is not { } tracks || moved.PlaylistItemId is not { } id) return false;
        var index = tracks.IndexOf(moved);
        var after = index > 0 ? tracks[index - 1].PlaylistItemId : null;
        try
        {
            await Client.MovePlaylistItemAsync(playlist.RatingKey, id, after);
            return true;
        }
        catch (PlexException ex)
        {
            Log.LogWarning("Couldn't reorder {Playlist}: {Message}", playlist.Title, ex.Message);
            await LoadAsync(playlist);
            return false;
        }
    }

    public async Task LoadAsync(Playlist seed)
    {
        Playlist = seed;
        UpdateMeta(seed);
        var tracks = new IncrementalCollection<Track>((start, size, ct) => Client.GetPlaylistItemsAsync(seed.RatingKey, start, size, ct), 150);
        tracks.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
        Tracks = tracks;
        await RunAsync(async () =>
        {
            var fullTask = Client.GetPlaylistAsync(seed.RatingKey);
            await Task.WhenAll(fullTask, tracks.LoadFirstPageAsync());
            if (fullTask.Result is { } full)
            {
                Playlist = full;
                UpdateMeta(full);
            }
            IsEmpty = tracks.Count == 0;
        });
    }

    public async Task RefreshAsync()
    {
        if (Playlist is not { } current || Session.Client is not { } client) return;
        try
        {
            var fresh = new IncrementalCollection<Track>((start, size, ct) =>
            {
                using (Core.Caching.ResponseCache.FetchFresh())
                    return client.GetPlaylistItemsAsync(current.RatingKey, start, size, ct);
            }, 150);
            Task<Playlist?> fullTask;
            using (Core.Caching.ResponseCache.FetchFresh())
            {
                fullTask = client.GetPlaylistAsync(current.RatingKey);
                await Task.WhenAll(fullTask, fresh.LoadFirstPageAsync());
            }
            if (!ReferenceEquals(current, Playlist)) return;
            if (fullTask.Result is { } full)
            {
                Playlist = full;
                UpdateMeta(full);
            }
            var loaded = Tracks?.Take(fresh.Count).Select(t => (t.RatingKey, t.PlaylistItemId, t.UserRating)) ?? [];
            var countChanged = fullTask.Result is { } f && f.TrackCount != current.TrackCount;
            if (countChanged || !loaded.SequenceEqual(fresh.Select(t => (t.RatingKey, t.PlaylistItemId, t.UserRating))) || (Tracks?.Count ?? 0) < fresh.Count)
            {
                fresh.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
                Tracks = fresh;
                IsEmpty = fresh.Count == 0;
            }
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Playlist refresh skipped: {Message}", ex.Message);
        }
    }

    private void UpdateMeta(Playlist p)
    {
        var bits = new List<string>();
        if (p.IsSmart) bits.Add("Smart playlist");
        if (p.TrackCount is > 0) bits.Add(Format.Count(p.TrackCount, "track", "tracks"));
        if (p.DurationMs > 0) bits.Add(Format.LongDuration(TimeSpan.FromMilliseconds(p.DurationMs)));
        MetaText = string.Join(" · ", bits);
    }
}
