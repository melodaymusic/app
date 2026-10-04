using HumeMusic.Helpers;
using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class PlaylistPage : Page
{
    private Playlist? _playlist;

    public PlaylistPage()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistViewModel>();
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelChanged;
        Loaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().PlaylistsChanged += OnPlaylistsChanged;
        Unloaded += (_, _) => App.Services.GetRequiredService<LibraryActions>().PlaylistsChanged -= OnPlaylistsChanged;
    }

    // Renamed, added to or edited elsewhere in the app: show it.
    private void OnPlaylistsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () => await ViewModel.RefreshAsync());

    private void OnViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlaylistViewModel.Playlist)) UpdateWash();
    }

    private static DetailCache Cache => App.Services.GetRequiredService<DetailCache>();
    private string? _cacheKey;

    /// <summary>The top of the page takes the UltraBlur of the playlist's artwork.</summary>
    private void UpdateWash()
    {
        if (ViewModel.Playlist is { } playlist && ReferenceEquals(Frame?.Content, this))
            _ = App.Services.GetRequiredService<AmbientService>().ShowHeaderAsync(playlist, playlist.DisplayThumb,
                () => ReferenceEquals(Frame?.Content, this) && ReferenceEquals(playlist, ViewModel.Playlist));
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        App.Services.GetRequiredService<AmbientService>().Clear();
        if (_cacheKey is not null && Scrollers.Find(TrackList) is { } sv) Cache.SetScroll(_cacheKey, sv.VerticalOffset);
    }

    public PlaylistViewModel ViewModel { get; private set; }

    private LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Playlist p) return;
        _playlist = p;
        // A recently visited playlist comes back as it was left, without loading again.
        _cacheKey = "playlist:" + p.RatingKey;
        var vm = Cache.Get(_cacheKey, () => App.Services.GetRequiredService<PlaylistViewModel>(), out var isNew, e.NavigationMode == NavigationMode.Back);
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
            await vm.LoadAsync(p);
            if (vm.HasError) Cache.Forget(_cacheKey);
        }
        else
        {
            Scrollers.Restore(TrackList, Cache.GetScroll(_cacheKey));
            // Playlists change elsewhere (Plex Web, other apps): check quietly and pick up changes.
            if (Cache.Age(_cacheKey) > TimeSpan.FromSeconds(20))
            {
                Cache.MarkFresh(_cacheKey);
                await vm.RefreshAsync();
            }
        }
    }

    private async void OnTracksReordered(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (args.DropResult != Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move) return;
        foreach (var track in args.Items.OfType<Track>())
        {
            if (!await ViewModel.SaveMoveAsync(track))
            {
                App.Services.GetRequiredService<NotificationService>().ShowError("Couldn't reorder the playlist", "Plex didn't accept the new order.", autoHide: true);
                return;
            }
        }
    }

    private async void OnRetry(object? sender, EventArgs e)
    {
        if (_playlist is not null) await ViewModel.LoadAsync(_playlist);
    }

    // Play/Shuffle resolve the whole playlist, not just the rows loaded so far.
    private void OnPlay(object? sender, EventArgs e)
    {
        if (ViewModel.Playlist is { } p) _ = Actions.PlayAsync(p);
    }

    private void OnShuffle(object? sender, EventArgs e)
    {
        if (ViewModel.Playlist is { } p) _ = Actions.PlayAsync(p, shuffle: true);
    }
}
