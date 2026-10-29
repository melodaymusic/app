using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed partial class CollectionViewModel : ViewModelBase
{
    public CollectionViewModel(PlexSession session, ILogger<CollectionViewModel> log) : base(session, log) { }

    [ObservableProperty] public partial MusicCollection? Collection { get; private set; }
    public ObservableCollection<MediaItem> Items { get; } = [];

    public bool CanReorder => Collection is { IsSmart: false };

    partial void OnCollectionChanged(MusicCollection? value) => OnPropertyChanged(nameof(CanReorder));

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
