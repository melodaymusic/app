using System.ComponentModel;
using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Helpers;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class AlbumPage : Page
{
    private Album? _album;

    public AlbumPage()
    {
        ViewModel = App.Services.GetRequiredService<AlbumViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Loaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().ItemChanged += OnItemChanged;
        Unloaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().ItemChanged -= OnItemChanged;
    }

    // Its cover, backdrop, logo or details were changed (from the menu): show them.
    private void OnItemChanged(object? sender, MediaItem item) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (item.RatingKey == ViewModel.Album?.RatingKey) await ViewModel.RefreshAlbumAsync();
    });

    public AlbumViewModel ViewModel { get; private set; }

    private static DetailCache Cache => App.Services.GetRequiredService<DetailCache>();
    private string? _cacheKey;

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    public string MoreByHeader(string? artist) => string.IsNullOrEmpty(artist) ? "More by this artist" : $"More by {artist}";

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Album album) return;
        _album = album;
        // A recently visited album comes back as it was left, without loading again.
        _cacheKey = "album:" + album.RatingKey;
        var vm = Cache.Get(_cacheKey, () => App.Services.GetRequiredService<AlbumViewModel>(), out var isNew, e.NavigationMode == NavigationMode.Back);
        if (!ReferenceEquals(vm, ViewModel))
        {
            ViewModel.PropertyChanged -= OnViewModelChanged;
            ViewModel = vm;
            vm.PropertyChanged += OnViewModelChanged;
            Bindings.Update();
        }
        UpdateWash();
        if (isNew)
        {
            await vm.LoadAsync(album);
            if (vm.HasError) Cache.Forget(_cacheKey);
        }
        else
        {
            Scrollers.Restore(Scroller, Cache.GetScroll(_cacheKey));
        }
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AlbumViewModel.Album))
        {
            UpdateWash();
        }
    }

    /// <summary>The top of the page takes the album's Plex UltraBlur, dissolving into the page.</summary>
    private void UpdateWash()
    {
        if (ViewModel.Album is { } album && ReferenceEquals(Frame?.Content, this))
            _ = App.Services.GetRequiredService<AmbientService>().ShowHeaderAsync(album, album.Thumb,
                () => ReferenceEquals(Frame?.Content, this) && ReferenceEquals(album, ViewModel.Album));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Services.GetRequiredService<AmbientService>().Clear();
        if (_cacheKey is not null) Cache.SetScroll(_cacheKey, Scroller.VerticalOffset);
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (_album is not null) await ViewModel.LoadAsync(_album);
    }

    private void OnPlay(object? sender, EventArgs e)
    {
        if (_album is not null) App.Services.GetRequiredService<LibraryActions>().RecordPlayed(_album);
        _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.TrackList);
    }

    private void OnShuffle(object? sender, EventArgs e)
    {
        if (_album is not null) App.Services.GetRequiredService<LibraryActions>().RecordPlayed(_album);
        _ = App.Services.GetRequiredService<PlayerService>().PlayTracksAsync(ViewModel.TrackList, 0, shuffle: true);
    }
}
