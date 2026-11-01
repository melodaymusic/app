using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
