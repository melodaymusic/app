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
    }

    public IReadOnlyList<BrowseCategory> Categories { get; } =
    [
        new("Genres", "Tags", TagKind.Genre),
        new("Moods", "Smile", TagKind.Mood),
        new("Styles", "Palette", TagKind.Style),
        new("Decades", "Calendar", TagKind.Decade),
    ];

    public ObservableCollection<MediaItem> Items { get; } = [];

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
        foreach (var i in items) Items.Add(i);
        IsEmpty = Items.Count == 0;
    }
}

/// <summary>Albums (and a track mix) for one genre / mood / style / decade.</summary>
public sealed partial class TagViewModel : ViewModelBase
{
    public TagViewModel(PlexSession session, ILogger<TagViewModel> log) : base(session, log) { }

    [ObservableProperty] public partial TagItem? Tag { get; private set; }
    [ObservableProperty] public partial IncrementalCollection<Album>? Albums { get; private set; }
    [ObservableProperty] public partial string CountText { get; private set; } = string.Empty;

    public string KindLabel => Tag?.TagKind.ToString() ?? string.Empty;

    public async Task LoadAsync(TagItem tag)
    {
        Tag = tag;
        OnPropertyChanged(nameof(KindLabel));
        var field = PlexServerClient.TagField(tag.TagKind);
        var filter = new KeyValuePair<string, string?>(tag.TagKind == TagKind.Mood ? "track.mood" : field, tag.FilterValue);
        var albums = new IncrementalCollection<Album>((start, size, ct) =>
            Client.GetAlbumsAsync(SectionKey, start, size, "titleSort", ct, filter), 120);
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
                    Client.GetAlbumsAsync(SectionKey, start, size, "titleSort", ct, new KeyValuePair<string, string?>("mood", tag.FilterValue)), 120);
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
