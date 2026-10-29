using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class TagViewModel : ViewModelBase
{
    private bool _ready;

    public TagViewModel(PlexSession session, ILogger<TagViewModel> log) : base(session, log)
    {
        SelectedSort = SortOptions[0];
    }

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new("Title", "titleSort"),
        new("Artist", "artist.titleSort,album.titleSort"),
        new("Release year", "year:desc,titleSort"),
        new("Recently Added", "addedAt:desc"),
        new("Recently Played", "lastViewedAt:desc"),
        new("Most Played", "viewCount:desc"),
        new("Rating", "userRating:desc"),
    ];

    [ObservableProperty] public partial SortOption SelectedSort { get; set; }

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_ready && Tag is { } tag) _ = LoadAsync(tag);
    }

    [ObservableProperty] public partial TagItem? Tag { get; private set; }
    [ObservableProperty] public partial IncrementalCollection<Album>? Albums { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    public string KindLabel => Tag?.TagKind.ToString() ?? string.Empty;

    public async Task LoadAsync(TagItem tag)
    {
        if (Tag is null || Tag.TagKind != tag.TagKind || Tag.FilterValue != tag.FilterValue)
        {
            _ready = false;
            SelectedSort = SortOptions[0];
        }
        _ready = true;
        Tag = tag;
        var sort = SelectedSort.Value;
        OnPropertyChanged(nameof(KindLabel));
        var field = PlexServerClient.TagField(tag.TagKind);
        var filter = new KeyValuePair<string, string?>(tag.TagKind == TagKind.Mood ? "track.mood" : field, tag.FilterValue);
        var albums = new IncrementalCollection<Album>((start, size, ct) =>
            Client.GetAlbumsAsync(SectionKey, start, size, sort, ct, filter), 120);
        albums.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
        albums.PageLoaded += (_, _) => CountText = Format.Count(albums.TotalCount ?? albums.Count, "album", "albums");
        Albums = albums;
        await RunAsync(async () =>
        {
            await albums.LoadFirstPageAsync();
            if (albums.Count == 0 && tag.TagKind == TagKind.Mood)
            {
                var alt = new IncrementalCollection<Album>((start, size, ct) =>
                    Client.GetAlbumsAsync(SectionKey, start, size, sort, ct, new KeyValuePair<string, string?>("mood", tag.FilterValue)), 120);
                alt.PageLoaded += (_, _) => CountText = Format.Count(alt.TotalCount ?? alt.Count, "album", "albums");
                await alt.LoadFirstPageAsync();
                Albums = alt;
            }
            IsEmpty = Albums!.Count == 0;
        });
    }
}
