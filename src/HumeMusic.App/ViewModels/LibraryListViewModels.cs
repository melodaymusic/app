using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

public sealed record SortOption(string Label, string Value)
{
    public override string ToString() => Label;
}

/// <summary>Shared logic for the paged Artists / Albums / Tracks / Playlists lists.</summary>
public abstract partial class PagedListViewModel<T> : ViewModelBase
{
    private int _loadedVersion = -1;

    protected PagedListViewModel(PlexSession session, ILogger log, IReadOnlyList<SortOption> sorts) : base(session, log)
    {
        SortOptions = sorts;
        SelectedSort = sorts[0];
        Items = CreateCollection();
    }

    public IReadOnlyList<SortOption> SortOptions { get; }

    [ObservableProperty] public partial SortOption SelectedSort { get; set; }
    [ObservableProperty] public partial IncrementalCollection<T> Items { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    protected abstract Task<PagedResult<T>> FetchAsync(int start, int size, string sort, CancellationToken ct);
    protected abstract string Noun(int count);

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_loadedVersion >= 0) _ = ReloadAsync();
    }

    /// <summary>Selects a sort by its Plex value (used by "See all" links from Home).</summary>
    public void ApplySort(string? value)
    {
        if (value is null) return;
        var match = SortOptions.FirstOrDefault(o => o.Value == value);
        if (match is not null && match != SelectedSort) SelectedSort = match;
    }

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion == libraryVersion && Items.Count > 0) return;
        _loadedVersion = libraryVersion;
        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        Items = CreateCollection();
        CountText = string.Empty;
        await RunAsync(async () =>
        {
            await Items.LoadFirstPageAsync();
            IsEmpty = Items.Count == 0 && !HasError;
        });
    }

    private IncrementalCollection<T> CreateCollection()
    {
        var sort = SelectedSort?.Value ?? string.Empty;
        var collection = new IncrementalCollection<T>((start, size, ct) => FetchAsync(start, size, sort, ct), 120);
        collection.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
        collection.PageLoaded += (_, _) =>
        {
            var total = collection.TotalCount ?? collection.Count;
            CountText = Noun(total);
        };
        return collection;
    }
}

public sealed partial class ArtistsViewModel : PagedListViewModel<Artist>
{
    public ArtistsViewModel(PlexSession session, ILogger<ArtistsViewModel> log) : base(session, log,
    [
        new("Name", "titleSort"),
        new("Recently Added", "addedAt:desc"),
        new("Recently Played", "lastViewedAt:desc"),
        new("Most Played", "viewCount:desc"),
        new("Rating", "userRating:desc"),
    ]) { }

    protected override Task<PagedResult<Artist>> FetchAsync(int start, int size, string sort, CancellationToken ct) =>
        Client.GetArtistsAsync(SectionKey, start, size, sort, ct);

    protected override string Noun(int count) => Format.Count(count, "artist", "artists");
}

public sealed partial class AlbumsViewModel : PagedListViewModel<Album>
{
    public AlbumsViewModel(PlexSession session, ILogger<AlbumsViewModel> log) : base(session, log,
    [
        new("Title", "titleSort"),
        new("Artist", "artist.titleSort,album.titleSort"),
        new("Release year", "year:desc,titleSort"),
        new("Recently Added", "addedAt:desc"),
        new("Recently Played", "lastViewedAt:desc"),
        new("Most Played", "viewCount:desc"),
        new("Rating", "userRating:desc"),
    ]) { }

    protected override Task<PagedResult<Album>> FetchAsync(int start, int size, string sort, CancellationToken ct) =>
        Client.GetAlbumsAsync(SectionKey, start, size, sort, ct);

    protected override string Noun(int count) => Format.Count(count, "album", "albums");
}

public sealed partial class TracksViewModel : PagedListViewModel<Track>
{
    public TracksViewModel(PlexSession session, ILogger<TracksViewModel> log) : base(session, log,
    [
        new("Title", "titleSort"),
        new("Artist", "artist.titleSort,album.titleSort,track.index"),
        new("Album", "album.titleSort,track.index"),
        new("Recently Added", "addedAt:desc"),
        new("Recently Played", "lastViewedAt:desc"),
        new("Most Played", "viewCount:desc"),
        new("Rating", "userRating:desc"),
        new("Duration", "duration:desc"),
    ]) { }

    protected override Task<PagedResult<Track>> FetchAsync(int start, int size, string sort, CancellationToken ct) =>
        Client.GetTracksAsync(SectionKey, start, size, sort, ct);

    protected override string Noun(int count) => Format.Count(count, "track", "tracks");
}

/// <summary>
/// Sorts Plex can do server-side are passed through; "local:" sorts (by track / item count, which
/// Plex rejects) load the whole list once and sort it here. Playlist and collection lists are small.
/// </summary>
internal static class LocalSort
{
    public static PagedResult<T> Page<T>(IReadOnlyList<T> all, int start, int size) => new()
    {
        Items = all.Skip(start).Take(size).ToList(),
        Offset = start,
        TotalSize = all.Count,
    };
}

public sealed partial class PlaylistsViewModel : PagedListViewModel<Playlist>
{
    public PlaylistsViewModel(PlexSession session, ILogger<PlaylistsViewModel> log) : base(session, log,
    [
        new("Name", "titleSort"),
        new("Name (Z–A)", "titleSort:desc"),
        new("Recently Played", "lastViewedAt:desc"),
        new("Recently Added", "addedAt:desc"),
        new("Recently updated", "updatedAt:desc"),
        new("Most Played", "viewCount:desc"),
        new("Most tracks", "local:tracks"),
        new("Longest", "duration:desc"),
    ])
    {
        // Playlists created, renamed or deleted anywhere in the app show up here.
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        App.Services.GetRequiredService<Services.LibraryActions>().PlaylistsChanged += (_, _) =>
            dispatcher?.TryEnqueue(async () => await ReloadAsync());
    }

    protected override async Task<PagedResult<Playlist>> FetchAsync(int start, int size, string sort, CancellationToken ct)
    {
        if (sort == "local:tracks")
        {
            var all = await Client.GetPlaylistsAsync(0, 5000, ct);
            return LocalSort.Page(all.Items.OrderByDescending(p => p.TrackCount ?? 0).ThenBy(p => p.Title).ToList(), start, size);
        }
        return await Client.GetPlaylistsAsync(start, size, ct, sort);
    }

    protected override string Noun(int count) => Format.Count(count, "playlist", "playlists");
}

public sealed partial class CollectionsViewModel : PagedListViewModel<MusicCollection>
{
    public CollectionsViewModel(PlexSession session, ILogger<CollectionsViewModel> log) : base(session, log,
    [
        new("Name", "titleSort"),
        new("Name (Z–A)", "titleSort:desc"),
        new("Recently Added", "addedAt:desc"),
        new("Recently updated", "updatedAt:desc"),
        new("Most items", "local:items"),
    ]) { }

    protected override async Task<PagedResult<MusicCollection>> FetchAsync(int start, int size, string sort, CancellationToken ct)
    {
        if (sort == "local:items")
        {
            var all = await Client.GetCollectionsAsync(SectionKey, 0, 5000, "titleSort", ct);
            return LocalSort.Page(all.Items.OrderByDescending(c => c.ChildCount ?? 0).ThenBy(c => c.Title).ToList(), start, size);
        }
        return await Client.GetCollectionsAsync(SectionKey, start, size, sort, ct);
    }

    protected override string Noun(int count) => Format.Count(count, "collection", "collections");
}
