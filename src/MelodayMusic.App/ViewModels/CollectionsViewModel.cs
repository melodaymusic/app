using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

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
