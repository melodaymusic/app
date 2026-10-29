using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed record BrowseCategory(string Label, string Icon, TagKind? Kind, bool IsCollections = false) : MelodayMusic.Controls.IChipTab
{
    public override string ToString() => Label;
}

public sealed partial class BrowseViewModel : ViewModelBase
{
    private readonly Dictionary<string, IReadOnlyList<MediaItem>> _cache = [];
    private int _loadedVersion = -1;

    public BrowseViewModel(PlexSession session, ILogger<BrowseViewModel> log) : base(session, log)
    {
        SelectedCategory = Categories[0];
        SelectedSort = SortOptions[0];
    }

    public IReadOnlyList<BrowseCategory> Categories { get; } =
    [
        new("Genres", "Tags", TagKind.Genre),
        new("Moods", "Smile", TagKind.Mood),
        new("Styles", "Palette", TagKind.Style),
        new("Decades", "Calendar", TagKind.Decade),
    ];

    public ObservableCollection<MediaItem> Items { get; } = [];

    public IReadOnlyList<SortOption> SortOptions { get; } =
    [
        new("Default", "default"),
        new("Name (A–Z)", "name"),
        new("Name (Z–A)", "name:desc"),
        new("Largest first", "count:desc"),
        new("Smallest first", "count"),
    ];

    [ObservableProperty] public partial SortOption SelectedSort { get; set; } = null!;

    partial void OnSelectedSortChanged(SortOption value)
    {
        if (_cache.TryGetValue(SelectedCategory.Label, out var items)) Fill(items);
    }

    private static int Size(MediaItem item) => item is TagItem tag
        ? tag.Count ?? (tag.Members.Sum(m => m.Count ?? 0) is var total and > 0 ? total : tag.Members.Count)
        : 0;

    private IEnumerable<MediaItem> Ordered(IReadOnlyList<MediaItem> items) => (SelectedSort?.Value ?? "default") switch
    {
        "name" => items.OrderBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
        "name:desc" => items.OrderByDescending(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
        "count:desc" => items.OrderByDescending(Size).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
        "count" => items.OrderBy(Size).ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase),
        _ => items,
    };

    [ObservableProperty] public partial BrowseCategory SelectedCategory { get; set; }

    partial void OnSelectedCategoryChanged(BrowseCategory value)
    {
        if (_loadedVersion >= 0) _ = LoadCategoryAsync();
    }

    public async Task EnsureLoadedAsync(int libraryVersion)
    {
        if (_loadedVersion != libraryVersion) _cache.Clear();
        if (_loadedVersion == libraryVersion && Items.Count > 0) return;
        _loadedVersion = libraryVersion;
        await LoadCategoryAsync();
    }

    public async Task LoadCategoryAsync()
    {
        var category = SelectedCategory;
        if (_cache.TryGetValue(category.Label, out var cached))
        {
            Fill(cached);
            return;
        }
        Items.Clear();
        await RunAsync(async () =>
        {
            IReadOnlyList<MediaItem> items;
            if (category.IsCollections)
            {
                items = await Client.GetCollectionsAsync(SectionKey);
            }
            else
            {
                var kind = category.Kind!.Value;
                var type = kind == TagKind.Mood ? PlexTypes.Track : PlexTypes.Album;
                var tags = await Client.GetTagsAsync(SectionKey, kind, type);
                if (tags.Count == 0 && kind == TagKind.Mood) tags = await Client.GetTagsAsync(SectionKey, kind, PlexTypes.Album);
                tags = tags.Where(t => !t.Title.Equals("ArtistPopular", StringComparison.OrdinalIgnoreCase)).ToList();
                items = kind == TagKind.Decade ? tags.OrderByDescending(t => t.Title).ToList() : TagFamilies.Group(tags, kind);
            }
            if (SelectedCategory != category) return;
            _cache[category.Label] = items;
            Fill(items);
        });
    }

    private void Fill(IReadOnlyList<MediaItem> items)
    {
        Items.Clear();
        foreach (var i in Ordered(items)) Items.Add(i);
        IsEmpty = Items.Count == 0;
    }
}
