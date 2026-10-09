using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace HumeMusic.Views;

/// <summary>Shared pieces for the app's dialogs.</summary>
internal static class Dialogs
{
    public static XamlRoot? Root => App.MainWindow?.Content?.XamlRoot;

    public static ContentDialog Create(string title, XamlRoot? root = null) => Styled(new ContentDialog
    {
        XamlRoot = root ?? Root,
        Title = title,
        DefaultButton = ContentDialogButton.Primary,
    });

    /// <summary>The app's dialog look: its buttons are the same capsules as everywhere else.</summary>
    public static ContentDialog Styled(ContentDialog dialog)
    {
        var resources = Application.Current.Resources;
        dialog.Style = (Style)resources["DefaultContentDialogStyle"];
        dialog.PrimaryButtonStyle = (Style)resources["HumePrimaryButtonStyle"];
        dialog.SecondaryButtonStyle = (Style)resources["HumeSecondaryButtonStyle"];
        dialog.CloseButtonStyle = (Style)resources["HumeSecondaryButtonStyle"];
        // A default button is given WinUI's own square-cornered accent style, over the capsule; so the
        // dialog has none, and Enter presses the primary button here instead.
        if (dialog.DefaultButton == ContentDialogButton.Primary)
        {
            dialog.DefaultButton = ContentDialogButton.None;
            dialog.KeyDown += (_, e) =>
            {
                if (e.Key != Windows.System.VirtualKey.Enter || e.Handled) return;
                if (CloseOnEnter.TryGetValue(dialog, out _))
                {
                    dialog.Hide();
                    e.Handled = true;
                    return;
                }
                if (!dialog.IsPrimaryButtonEnabled || string.IsNullOrEmpty(dialog.PrimaryButtonText)) return;
                if (Find(dialog, "PrimaryButton") is Button primary && primary.IsEnabled)
                {
                    new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(primary).Invoke();
                    e.Handled = true;
                }
            };
        }
        return dialog;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ContentDialog, object> CloseOnEnter = [];

    /// <summary>Enter closes the dialog rather than pressing its primary button (for anything destructive).</summary>
    public static void PreferClose(ContentDialog dialog) => CloseOnEnter.AddOrUpdate(dialog, true);

    private static DependencyObject? Find(DependencyObject parent, string name)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (Find(child, name) is { } found) return found;
        }
        return null;
    }

    public static TextBlock Text(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.WrapWholeWords,
        Style = (Style)Application.Current.Resources["HumeBodyText"],
    };

    public static TextBlock Note(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.WrapWholeWords,
        Style = (Style)Application.Current.Resources["HumeSecondaryText"],
    };

    public static Button SecondaryButton(string text) => new() { Content = text, Style = (Style)Application.Current.Resources["HumeSecondaryButtonStyle"] };

    /// <summary>Room for a dialog's content beyond the usual width (a dialog's own content is then set a little narrower).</summary>
    public static void Wide(ContentDialog dialog, double width) => dialog.Resources["ContentDialogMaxWidth"] = width;

    /// <summary>The corners of a small picture in a row: an artist's is round, an album's or song's the thumbnail radius.</summary>
    public static CornerRadius ArtistRadius => new(20);

    public static CornerRadius AlbumRadius => (CornerRadius)Application.Current.Resources["HumeRadiusThumb"];

    /// <summary>
    /// A row in a dialog's list, as everywhere: a small picture, a title with what it is under it, and anything at the far end
    /// (a remove button, a "Change"). Give it <c>HumeGroupRowStyle</c> in a group, or leave it plain in a list of matches.
    /// </summary>
    public static Grid MediaRow(string? thumb, string title, string? detail, string icon, CornerRadius radius, double size, FrameworkElement? trailing = null)
    {
        var resources = Application.Current.Resources;
        var row = new Grid { ColumnSpacing = 12, Padding = new Thickness(4, 2, 4, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new ArtworkImage
        {
            Width = size, Height = size, DecodeSize = (int)size * 2, SourcePath = thumb, ArtworkCornerRadius = radius, PlaceholderIcon = icon,
        });
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = title, Style = (Style)resources["HumeBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis } },
        };
        if (!string.IsNullOrEmpty(detail))
            text.Children.Add(new TextBlock { Text = detail, Style = (Style)resources["HumeSecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        if (trailing is not null)
        {
            trailing.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(trailing, 2);
            row.Children.Add(trailing);
        }
        return row;
    }

    /// <summary>An image file the listener picks (PNG, WebP or JPEG), as bytes; null if they cancel.</summary>
    public static async Task<byte[]?> PickImageAsync()
    {
        if (App.MainWindow is not { } window) return null;
        var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(window.AppWindow.Id)
        {
            SuggestedStartLocation = Microsoft.Windows.Storage.Pickers.PickerLocationId.PicturesLibrary,
            ViewMode = Microsoft.Windows.Storage.Pickers.PickerViewMode.Thumbnail,
        };
        foreach (var type in new[] { ".png", ".webp", ".jpg", ".jpeg" }) picker.FileTypeFilter.Add(type);
        var file = await picker.PickSingleFileAsync();
        return file is { Path: { Length: > 0 } path } ? await File.ReadAllBytesAsync(path) : null;
    }
}

/// <summary>Creating, editing and deleting playlists.</summary>
public static class PlaylistDialogs
{

    private static LibraryActions Actions => App.Services.GetRequiredService<LibraryActions>();

    /// <summary>A new playlist holding <paramref name="items"/>, named by the listener.</summary>
    public static async Task NewAsync(IReadOnlyList<MediaItem> items, string? suggestedName = null, string? description = null, string? coverPath = null)
    {
        if (items.Count == 0) return;
        var dialog = Dialogs.Create("New Playlist");
        var name = new TextBox { Header = "Name", PlaceholderText = "My playlist", Text = suggestedName ?? string.Empty, MinWidth = 380 };
        dialog.Content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                name,
                Dialogs.Note(items.Count == 1 ? $"Starts with {items[0].Title}." : $"Starts with {items.Count} items."),
            },
        };
        dialog.PrimaryButtonText = "Create";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        dialog.Opened += (_, _) => { name.Focus(FocusState.Programmatic); name.SelectAll(); };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            if (await Actions.CreatePlaylistAsync(name.Text.Trim(), items, description, coverPath) is null) e.Cancel = true;
            deferral.Complete();
        };
        await dialog.ShowAsync();
    }

    /// <summary>Asks before deleting: a playlist is gone from the server for every app that uses it.</summary>
    public static async Task DeleteAsync(Playlist playlist)
    {
        var dialog = Dialogs.Create($"Delete “{playlist.Title}”?");
        dialog.Content = Dialogs.Text("This deletes the playlist from your Plex server, for every app that uses it. The songs stay in your library.");
        dialog.PrimaryButtonText = "Delete Playlist";
        dialog.PrimaryButtonStyle = (Style)Application.Current.Resources["HumeDangerButtonStyle"];
        dialog.CloseButtonText = "Keep It";
        Dialogs.PreferClose(dialog);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await Actions.DeletePlaylistAsync(playlist);
    }
}


/// <summary>
/// Meloday's mix builders: choose artists (or albums) and they play together, shuffled and topped up
/// as they go; the mix can also be kept as a playlist.
/// </summary>
public static class MixBuilderDialog
{
    private const int MaxChoices = 10;

    public static async Task ShowAsync(StationKind kind)
    {
        var albums = kind == StationKind.AlbumMixBuilder;
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section) return;
        var resources = Application.Current.Resources;
        var chosen = new List<MediaItem>();

        var dialog = Dialogs.Create(albums ? "Album Radio Builder" : "Artist Radio Builder");
        dialog.PrimaryButtonText = "Play Radio";
        dialog.SecondaryButtonText = "Save as Playlist";
        dialog.CloseButtonText = "Cancel";

        var heading = new TextBlock { Style = (Style)resources["HumeGroupHeaderText"] };
        var rows = new StackPanel { Style = (Style)resources["HumeGroupRowsStyle"] };
        var group = new StackPanel
        {
            Style = (Style)resources["HumeGroupStyle"],
            Visibility = Visibility.Collapsed,
            Children = { heading, new Border { Style = (Style)resources["HumeGroupListStyle"], Child = rows } },
        };
        var suggestions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var suggestionsHost = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new TextBlock { Text = "You might add", Style = (Style)resources["HumeBodyStrongText"] },
                new ScrollViewer { Content = suggestions, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled },
            },
        };
        SearchPicker search = null!;
        search = new SearchPicker(albums ? "Search for an album" : "Search for an artist", async query =>
        {
            var results = await client.SearchAsync(query, section.Key, 8);
            return albums
                ? results.Albums.Select(a => new PickResult(a, a.Title, a.ArtistTitle, a.Thumb, "Disc3", Dialogs.AlbumRadius)).ToList()
                : results.Artists.Select(a => new PickResult(a, a.Title, null, a.Thumb, "MicVocal", Dialogs.ArtistRadius)).ToList();
        }, item => Add(item));

        dialog.Content = new StackPanel
        {
            Spacing = 24,
            Width = 480,
            Children =
            {
                Dialogs.Text(albums
                    ? "Choose up to 10 albums and Meloday plays them together, shuffled."
                    : "Choose up to 10 artists and Meloday plays their music together, shuffled."),
                search.Root,
                group,
                suggestionsHost,
            },
        };

        void Refresh()
        {
            rows.Children.Clear();
            foreach (var item in chosen)
            {
                var remove = new Button
                {
                    Style = (Style)resources["HumeIconButtonStyle"],
                    Content = new LucideIcon { Icon = "X", Width = (double)resources["HumeIconXS"], Height = (double)resources["HumeIconXS"] },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {item.Title}");
                remove.Click += (_, _) => { chosen.Remove(item); Refresh(); };
                var row = Dialogs.MediaRow(item.Thumb, item.Title, (item as Album)?.ArtistTitle, albums ? "Disc3" : "MicVocal",
                    albums ? Dialogs.AlbumRadius : Dialogs.ArtistRadius, 40, remove);
                row.Style = (Style)resources["HumeGroupRowStyle"];
                rows.Children.Add(row);
            }
            heading.Text = $"{(albums ? "Albums" : "Artists")} · {chosen.Count} of {MaxChoices}";
            group.Visibility = chosen.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = chosen.Count > 0;
            search.Box.IsEnabled = chosen.Count < MaxChoices;
            if (!albums) _ = SuggestAsync();
        }

        void Add(MediaItem item)
        {
            if (chosen.Count >= MaxChoices || chosen.Any(c => c.RatingKey == item.RatingKey)) return;
            chosen.Add(item);
            Refresh();
        }

        // Artists like the ones chosen, one tap to add.
        async Task SuggestAsync()
        {
            if (chosen.Count == 0)
            {
                suggestionsHost.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                var related = await client.GetRelatedHubsAsync(chosen[^1].RatingKey, 10);
                var similar = related.Where(h => h.Identifier == "artist.similar").SelectMany(h => h.Items.OfType<Artist>())
                    .Where(a => chosen.All(c => c.RatingKey != a.RatingKey)).Take(6).ToList();
                suggestions.Children.Clear();
                foreach (var artist in similar)
                {
                    var button = Dialogs.SecondaryButton(string.Empty);
                    button.Content = new IconLabel { Icon = "Plus", Text = artist.Title };
                    button.Click += (_, _) => Add(artist);
                    suggestions.Children.Add(button);
                }
                suggestionsHost.Visibility = similar.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (PlexException)
            {
                suggestionsHost.Visibility = Visibility.Collapsed;
            }
        }

        Refresh();
        var result = await dialog.ShowAsync();
        if (chosen.Count == 0 || result == ContentDialogResult.None) return;

        var names = chosen.Select(c => c.Title).ToList();
        var title = names.Count switch
        {
            1 => $"{names[0]} Radio",
            2 => $"{names[0]} & {names[1]} Radio",
            _ => $"{names[0]}, {names[1]} & More Radio",
        };
        var mix = new BuiltMix
        {
            RatingKey = "built:" + string.Join(',', chosen.Select(c => c.RatingKey)),
            Title = title,
            ArtistKeys = albums ? [] : chosen.Select(c => c.RatingKey).ToList(),
            AlbumKeys = albums ? chosen.Select(c => c.RatingKey).ToList() : [],
        };
        var actions = App.Services.GetRequiredService<LibraryActions>();
        if (result == ContentDialogResult.Primary)
        {
            await actions.PlayAsync(mix);
        }
        else
        {
            var tracks = await App.Services.GetRequiredService<MixService>().SampleAsync(mix, 100);
            if (tracks.Count > 0) await PlaylistDialogs.NewAsync(tracks, title);
        }
    }
}
