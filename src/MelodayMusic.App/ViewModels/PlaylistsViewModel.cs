using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        App.Services.GetRequiredService<Services.LibraryActions>().PlaylistsChanged += (_, _) =>
            dispatcher?.TryEnqueue(async () => await ReloadAsync());
        App.Services.GetRequiredService<Services.TrendingService>().Changed += (_, _) =>
            dispatcher?.TryEnqueue(async () => await ReloadAsync());
        App.Services.GetRequiredService<Services.MadePlaylistService>().Changed += (_, _) =>
            dispatcher?.TryEnqueue(async () => await ReloadAsync());
    }

    protected override async Task<PagedResult<Playlist>> FetchAsync(int start, int size, string sort, CancellationToken ct)
    {
        var trendingService = App.Services.GetRequiredService<Services.TrendingService>();
        var made = App.Services.GetRequiredService<Services.MadePlaylistService>();
        if (trendingService.IsOn || made.ManagedPlexKeys.Count > 0)
        {
            var all = new List<Playlist>();
            for (var offset = 0; ; offset += 500)
            {
                var page = await Client.GetPlaylistsAsync(offset, 500, ct, sort == "local:tracks" ? "titleSort" : sort);
                all.AddRange(page.Items);
                if (page.Items.Count < 500) break;
            }
            var visible = made.WithoutCopies(trendingService.ForDisplay(all));
            return LocalSort.Page(sort == "local:tracks"
                ? visible.OrderByDescending(p => p.TrackCount ?? 0).ThenBy(p => p.Title).ToList() : visible, start, size);
        }
        if (sort == "local:tracks")
        {
            var all = await Client.GetPlaylistsAsync(0, 5000, ct);
            return LocalSort.Page(all.Items.OrderByDescending(p => p.TrackCount ?? 0).ThenBy(p => p.Title).ToList(), start, size);
        }
        return await Client.GetPlaylistsAsync(start, size, ct, sort);
    }

    protected override string Noun(int count) => Format.Count(count, "playlist", "playlists");
}
