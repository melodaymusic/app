using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace MelodayMusic.Views;

public static class PlaylistDialogs
{
    private static LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    public static async Task NewAsync(IReadOnlyList<MediaItem> items, string? suggestedName = null, string? description = null, string? coverPath = null)
    {
        if (items.Count == 0) return;
        var dialog = Dialogs.Create("New Playlist");
        var name = new TextBox { Header = "Name", PlaceholderText = "My playlist", Text = suggestedName ?? string.Empty, MinWidth = Dialogs.ContentWidth(380) };
        var status = Dialogs.Note(string.Empty);
        status.Visibility = Visibility.Collapsed;
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                name,
                Dialogs.Note(items.Count == 1 ? $"Starts with {items[0].Title}." : $"Starts with {items.Count} items."),
                status,
            },
        };
        dialog.PrimaryButtonText = "Create";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        dialog.Opened += (_, _) => { name.Focus(FocusState.Programmatic); name.SelectAll(); };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            if (!await Dialogs.SaveAsync(e, async () => await Actions.CreatePlaylistAsync(name.Text.Trim(), items, description, coverPath) is not null))
            {
                status.Text = "Couldn't create the playlist. Check your connection and try again.";
                status.Visibility = Visibility.Visible;
            }
        };
        await dialog.ShowAsync();
    }

    public static async Task DeleteAsync(Playlist playlist)
    {
        var dialog = Dialogs.Create($"Delete “{playlist.Title}”?");
        dialog.Content = Dialogs.Text("This deletes the playlist from your Plex server, for every app that uses it. The songs stay in your library.");
        dialog.PrimaryButtonText = "Delete Playlist";
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["MelodayDangerButtonStyle"];
        dialog.CloseButtonText = "Keep It";
        Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await Actions.DeletePlaylistAsync(playlist);
    }
}
