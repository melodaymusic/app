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

    /// <summary>Renames a playlist and edits its description.</summary>
    public static async Task EditAsync(Playlist playlist)
    {
        var dialog = Dialogs.Create("Edit Playlist");
        var name = new TextBox { Header = "Name", Text = playlist.Title, MinWidth = 380 };
        var summary = new TextBox
        {
            Header = "Description",
            Text = playlist.Summary ?? string.Empty,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 110,
        };
        dialog.Content = new StackPanel { Spacing = 12, Children = { name, summary } };
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        name.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = name.Text.Trim().Length > 0;
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            if (!await Actions.EditPlaylistAsync(playlist, name.Text.Trim(), summary.Text.Trim())) e.Cancel = true;
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
        var chosen = new List<MediaItem>();

        var dialog = Dialogs.Create(albums ? "Album Mix Builder" : "Artist Mix Builder");
        var search = new AutoSuggestBox
        {
            PlaceholderText = albums ? "Add an album" : "Add an artist",
            QueryIcon = new SymbolIcon(Symbol.Find),
            MinWidth = 440,
        };
        var list = new StackPanel { Spacing = 8 };
        var suggestionsLabel = Dialogs.Note("You might add");
        var suggestions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var suggestionsHost = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Children = { suggestionsLabel, new ScrollViewer { Content = suggestions, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled } },
        };
        dialog.Content = new StackPanel
        {
            Spacing = 16,
            MinWidth = 460,
            Children =
            {
                Dialogs.Text(albums
                    ? "Choose up to 10 albums and Meloday plays them together, shuffled."
                    : "Choose up to 10 artists and Meloday plays their music together, shuffled."),
                search,
                list,
                suggestionsHost,
            },
        };
        dialog.PrimaryButtonText = "Play Mix";
        dialog.SecondaryButtonText = "Save as Playlist";
        dialog.CloseButtonText = "Cancel";

        void Refresh()
        {
            list.Children.Clear();
            foreach (var item in chosen)
            {
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new ArtworkImage
                {
                    Width = 32, Height = 32, DecodeSize = 64, SourcePath = item.Thumb,
                    ArtworkCornerRadius = new CornerRadius(albums ? 4 : 16), PlaceholderIcon = albums ? "Disc3" : "MicVocal",
                });
                var name = new TextBlock { Text = item.Title, Style = (Style)Application.Current.Resources["HumeBodyText"], VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var remove = new Button
                {
                    Style = (Style)Application.Current.Resources["HumeIconButtonStyle"],
                    Content = new LucideIcon { Icon = "X", Width = 14, Height = 14 },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {item.Title}");
                remove.Click += (_, _) => { chosen.Remove(item); Refresh(); };
                Grid.SetColumn(remove, 2);
                row.Children.Add(remove);
                list.Children.Add(row);
            }
            dialog.IsPrimaryButtonEnabled = dialog.IsSecondaryButtonEnabled = chosen.Count > 0;
            search.IsEnabled = chosen.Count < MaxChoices;
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
                    var button = Dialogs.SecondaryButton("+ " + artist.Title);
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

        var searchVersion = 0;
        search.TextChanged += async (box, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var version = ++searchVersion;
            var query = box.Text.Trim();
            if (query.Length < 2)
            {
                box.ItemsSource = null;
                return;
            }
            await Task.Delay(250);
            if (version != searchVersion) return;
            try
            {
                var results = await client.SearchAsync(query, section.Key, 8);
                if (version != searchVersion) return;
                box.ItemsSource = albums
                    ? results.Albums.Select(a => new SearchChoice(a, $"{a.Title} — {a.ArtistTitle}")).ToList()
                    : results.Artists.Select(a => new SearchChoice(a, a.Title)).ToList();
            }
            catch (PlexException)
            {
                box.ItemsSource = null;
            }
        };
        search.SuggestionChosen += (box, args) =>
        {
            if (args.SelectedItem is SearchChoice choice) Add(choice.Item);
            box.Text = string.Empty;
            box.ItemsSource = null;
        };

        Refresh();
        var result = await dialog.ShowAsync();
        if (chosen.Count == 0 || result == ContentDialogResult.None) return;

        var names = chosen.Select(c => c.Title).ToList();
        var title = names.Count switch
        {
            1 => $"{names[0]} Mix",
            2 => $"{names[0]} & {names[1]} Mix",
            _ => $"{names[0]}, {names[1]} & More Mix",
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

    private sealed record SearchChoice(MediaItem Item, string Text)
    {
        public override string ToString() => Text;
    }
}

/// <summary>Choosing an artist's photo or backdrop from the images Plex found, or uploading one.</summary>
public static class ArtworkPickerDialog
{
    /// <summary>Returns whether the image changed.</summary>
    public static async Task<bool> ShowAsync(Artist artist, ArtworkKind kind, XamlRoot root)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client) return false;
        var photo = kind == ArtworkKind.Poster;
        var dialog = Dialogs.Create(photo ? "Artist Photo" : "Artist Backdrop", root);
        var status = Dialogs.Note(string.Empty);
        var busy = new ProgressRing { IsActive = true, Width = 24, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 420, Visibility = Visibility.Collapsed };
        var upload = Dialogs.SecondaryButton("Upload Image…");
        dialog.Content = new StackPanel
        {
            Spacing = 16,
            MinWidth = 520,
            Children =
            {
                Dialogs.Text($"Choose one of the images Plex found for {artist.Title}, or upload your own."),
                busy, status, grid, upload,
            },
        };
        dialog.PrimaryButtonText = "Use This Image";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = false;
        var options = new List<ArtworkOption>();
        var changed = false;
        var current = -1;
        grid.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = grid.SelectedIndex >= 0 && grid.SelectedIndex != current;

        upload.Click += async (_, _) =>
        {
            try
            {
                if (await Dialogs.PickImageAsync() is not { } bytes) return;
                status.Text = "Uploading…";
                await client.UploadArtworkAsync(artist.RatingKey, kind, bytes);
                changed = true;
                dialog.Hide();
            }
            catch (Exception ex) when (ex is PlexException or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                status.Text = "That image couldn't be uploaded.";
            }
        };
        dialog.PrimaryButtonClick += async (_, e) =>
        {
            if (grid.SelectedIndex < 0 || grid.SelectedIndex >= options.Count) return;
            var deferral = e.GetDeferral();
            try
            {
                await client.SelectArtworkAsync(artist.RatingKey, kind, options[grid.SelectedIndex]);
                changed = true;
            }
            catch (PlexException)
            {
                status.Text = "Plex couldn't use that image. Try another.";
                e.Cancel = true;
            }
            deferral.Complete();
        };

        var show = dialog.ShowAsync();
        try
        {
            options.AddRange(await client.GetArtworkOptionsAsync(artist.RatingKey, kind));
        }
        catch (PlexException)
        {
            status.Text = "Plex couldn't list the images for this artist.";
        }
        busy.IsActive = false;
        busy.Visibility = Visibility.Collapsed;
        if (options.Count == 0 && status.Text.Length == 0) status.Text = "Plex hasn't found any images for this artist. You can upload one.";
        var (w, h) = photo ? (150, 150) : (240, 135);
        foreach (var option in options)
        {
            var uri = option.Thumb is { } thumb && thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? new Uri(thumb)
                : client.BuildArtworkUri(option.Thumb ?? option.Key, w * 2, h * 2);
            var image = new Image { Stretch = Stretch.UniformToFill, Width = w, Height = h };
            if (uri is not null) image.Source = new BitmapImage(uri) { DecodePixelWidth = w * 2 };
            var tile = new Border { CornerRadius = new CornerRadius(photo ? w / 2.0 : 8), Child = image, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128)) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile, $"Image {options.IndexOf(option) + 1}{(option.Selected ? ", in use" : string.Empty)}{(option.Provider is { } p ? $", from {p}" : string.Empty)}");
            grid.Items.Add(tile);
        }
        if (options.Count > 0) grid.Visibility = Visibility.Visible;
        current = options.FindIndex(o => o.Selected);
        grid.SelectedIndex = current;
        dialog.IsPrimaryButtonEnabled = false;
        await show;
        return changed;
    }
}
