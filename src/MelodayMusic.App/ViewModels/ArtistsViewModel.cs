using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
