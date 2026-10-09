using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

public sealed record BrowseCategory(string Label, string Icon, TagKind? Kind, bool IsCollections = false) : HumeMusic.Controls.IChipTab
{
    public override string ToString() => Label;
}

/// <summary>Browse by genre, mood, style, decade and collection.</summary>
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

    /// <summary>How the categories (genres, moods, styles, decades) are ordered: as Browse gathers them, by name, or by how much is in each.</summary>
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

    /// <summary>How much is in a category: its own count, or for a family the counts of the tags it gathers (and, with none given, how many it gathers).</summary>
    private static int Size(MediaItem item) => item is TagItem tag
        ? tag.Count ?? (tag.Members.Sum(m => m.Count ?? 0) is var total and > 0 ? total : tag.Members.Count)
        : 0;

    /// <summary>The items in the chosen order (Default is the order Browse gathers them in).</summary>
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
                // Moods are richest on tracks; genres, styles and decades are defined per album.
                var type = kind == TagKind.Mood ? PlexTypes.Track : PlexTypes.Album;
                var tags = await Client.GetTagsAsync(SectionKey, kind, type);
                if (tags.Count == 0 && kind == TagKind.Mood) tags = await Client.GetTagsAsync(SectionKey, kind, PlexTypes.Album);
                // Genres, styles and moods are gathered into families (Pop, Chill & Calm...), each with a hub.
                // "ArtistPopular" is Plex's own marker, not a mood anyone chose.
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

/// <summary>Albums (and a track mix) for one genre / mood / style / decade.</summary>
public sealed partial class TagViewModel : ViewModelBase
{
    private bool _ready;

    public TagViewModel(PlexSession session, ILogger<TagViewModel> log) : base(session, log)
    {
        SelectedSort = SortOptions[0];
    }

    /// <summary>The orders the albums of a genre, mood, style or decade can be in.</summary>
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
        // A different tag starts in the default order.
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
                // Some servers only expose moods at album level.
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

/// <summary>Contents of a Plex collection.</summary>
public sealed partial class CollectionViewModel : ViewModelBase
{
    public CollectionViewModel(PlexSession session, ILogger<CollectionViewModel> log) : base(session, log) { }

    [ObservableProperty] public partial MusicCollection? Collection { get; private set; }
    public ObservableCollection<MediaItem> Items { get; } = [];

    /// <summary>Items can be dragged into a new order (not in smart collections, which Plex fills by rules).</summary>
    public bool CanReorder => Collection is { IsSmart: false };

    partial void OnCollectionChanged(MusicCollection? value) => OnPropertyChanged(nameof(CanReorder));

    /// <summary>Saves a drag reorder to Plex; reloads the order from Plex if the server refuses.</summary>
    public async Task<bool> SaveMoveAsync(MediaItem moved)
    {
        if (Collection is not { } collection) return false;
        var index = Items.IndexOf(moved);
        var after = index > 0 ? Items[index - 1].RatingKey : null;
        try
        {
            await Client.MoveCollectionItemAsync(collection.RatingKey, moved.RatingKey, after);
            return true;
        }
        catch (PlexException ex)
        {
            Log.LogWarning("Couldn't reorder {Collection}: {Message}", collection.Title, ex.Message);
            await LoadAsync(collection);
            return false;
        }
    }

    /// <summary>Fetches the collection again (after its cover, backdrop, logo or details changed), without reloading the page.</summary>
    public async Task RefreshCollectionAsync()
    {
        if (Collection is not { } current || Session.Client is not { } client) return;
        try
        {
            if (await client.GetMetadataAsync(current.RatingKey) is { } m) Collection = PlexMapper.ToCollection(m);
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Collection not refreshed: {Message}", ex.Message);
        }
    }

    public async Task LoadAsync(MusicCollection collection)
    {
        Collection = collection;
        await RunAsync(async () =>
        {
            var items = await Client.GetCollectionItemsAsync(collection.RatingKey);
            Items.Clear();
            foreach (var i in items) Items.Add(i);
            IsEmpty = Items.Count == 0;
        });
    }
}
