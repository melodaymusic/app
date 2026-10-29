using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
