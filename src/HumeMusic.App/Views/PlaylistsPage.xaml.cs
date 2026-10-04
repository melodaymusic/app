using HumeMusic.Core.Models;
using HumeMusic.Services;
using HumeMusic.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace HumeMusic.Views;

public sealed partial class PlaylistsPage : Page
{
    public PlaylistsPage()
    {
        ViewModel = App.Services.GetRequiredService<PlaylistsViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// A new playlist: Plex makes playlists from music, so it starts with the queue; with nothing
    /// queued, it explains where playlists are started from.
    /// </summary>
    private async void OnNewPlaylist(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var tracks = App.Services.GetRequiredService<PlayerService>().Queue.Items.Select(i => i.Track).ToList();
        if (tracks.Count > 0)
        {
            await PlaylistDialogs.NewAsync(tracks);
            return;
        }
        var dialog = Dialogs.Create("New Playlist", XamlRoot);
        dialog.Content = Dialogs.Text("A new playlist starts with some music. Choose Add to playlist → New playlist from any song, album or artist's … menu, or save your queue as a playlist.");
        dialog.CloseButtonText = "Got It";
        Dialogs.PreferClose(dialog);
        await dialog.ShowAsync();
    }

    private void OnNewSmartPlaylist(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => _ = SmartPlaylistDialog.NewAsync();

    public PlaylistsViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ApplySort(e.Parameter as string);
        await ViewModel.EnsureLoadedAsync(App.Services.GetRequiredService<ShellViewModel>().LibraryVersion);
    }

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MediaItem item) App.Services.GetRequiredService<LibraryActions>().Open(item);
    }

    private async void OnRetry(object? sender, EventArgs e) => await ViewModel.ReloadAsync();
}
