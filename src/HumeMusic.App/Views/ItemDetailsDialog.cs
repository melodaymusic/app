using System.Runtime.InteropServices.WindowsRuntime;
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

/// <summary>
/// Everything about an album, artist, collection or playlist that can be edited, in one place and as Plex has it: the title
/// and summary, and the cover (an artist's photo), backdrop and logo, each chosen from the images Plex found or uploaded.
/// Nothing is sent until Save, and all of it is kept on the Plex server, for every app that uses it.
/// </summary>
public static class ItemDetailsDialog
{
    /// <summary>An image in one of the picture tabs: one Plex lists, or one the listener has just added.</summary>
    private sealed record Entry(ArtworkOption? Option, byte[]? Bytes, BitmapImage? Preview);

    /// <summary>One picture tab (cover, backdrop or logo): what Plex offers, what is chosen, and where it is drawn.</summary>
    private sealed class PictureTab(ArtworkKind kind)
    {
        public ArtworkKind Kind { get; } = kind;
        public List<Entry> Entries { get; } = [];
        public int Current { get; set; } = -1;
        public int Chosen { get; set; } = -1;
        public bool Dirty => Chosen >= 0 && Chosen != Current;
        public required FrameworkElement Panel { get; init; }
        public required ItemsRepeater Tiles { get; init; }
        public required TextBlock Status { get; init; }
        public required ProgressRing Busy { get; init; }
        public required Button Upload { get; init; }
    }

    /// <summary>Opens the editor, on one of its picture tabs if asked. Returns whether anything was saved.</summary>
    public static async Task<bool> ShowAsync(MediaItem item, ArtworkKind? open = null, XamlRoot? root = null)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client) return false;
        var actions = App.Services.GetRequiredService<LibraryActions>();
        var resources = Application.Current.Resources;
        var artist = item as Artist;
        var changed = false;

        var dialog = Dialogs.Create("Edit Details", root);
        Dialogs.Wide(dialog, 680);
        dialog.PrimaryButtonText = "Save";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = false;

        // ---------------------------------------------------------------- details
        var originalSummary = item switch { Album a => a.Summary, Artist r => r.Summary, MusicCollection c => c.Summary, Playlist p => p.Summary, _ => null } ?? string.Empty;
        var title = new TextBox { Header = "Title", Text = item.Title };
        var summary = new TextBox
        {
            Header = "Summary",
            Text = originalSummary,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 180,
        };
        var detailsPanel = new StackPanel
        {
            Spacing = 16,
            Children = { title, summary, Dialogs.Note("Saved on your Plex server, for every app that uses it.") },
        };
        bool DetailsDirty() => title.Text.Trim() != item.Title || summary.Text.Trim() != originalSummary.Trim();

        // ---------------------------------------------------------------- pictures
        var round = artist is not null;
        var tabs = new List<PictureTab>
        {
            NewTab(ArtworkKind.Poster),
            NewTab(ArtworkKind.Backdrop),
            NewTab(ArtworkKind.Logo),
        };

        PictureTab NewTab(ArtworkKind kind)
        {
            var status = Dialogs.Note(string.Empty);
            var busy = new ProgressRing { IsActive = true, Width = 24, Height = 24 };
            var upload = Dialogs.SecondaryButton(string.Empty);
            upload.Content = new IconLabel { Icon = "ImagePlus", Text = "Upload Image…" };
            var tiles = new ItemsRepeater
            {
                Layout = new UniformGridLayout
                {
                    MinColumnSpacing = 8,
                    MinRowSpacing = 8,
                    ItemsStretch = UniformGridLayoutItemsStretch.None,
                },
            };
            var header = new Grid { ColumnSpacing = 12 };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Children.Add(Dialogs.Note(kind switch
            {
                ArtworkKind.Poster => artist is not null ? "Choose a photo, or upload your own." : "Choose a cover, or upload your own.",
                ArtworkKind.Backdrop => "The wide picture behind the page. Choose one, or upload your own.",
                _ => "A transparent picture of the name, shown in place of the title. Choose one, or upload your own.",
            }));
            Grid.SetColumn(upload, 1);
            header.Children.Add(upload);
            var body = new StackPanel { Spacing = 12, Children = { header, busy, status } };
            var scroller = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
            var panel = new Grid { RowSpacing = 12, Height = 400, Visibility = Visibility.Collapsed };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.Children.Add(body);
            Grid.SetRow(scroller, 1);
            panel.Children.Add(scroller);
            return new PictureTab(kind) { Panel = panel, Tiles = tiles, Status = status, Busy = busy, Upload = upload };
        }

        string LabelOf(ArtworkKind kind) => kind switch
        {
            ArtworkKind.Poster => artist is not null ? "Photo" : "Cover",
            ArtworkKind.Backdrop => "Backdrop",
            _ => "Logo",
        };

        (double W, double H) SizeOf(ArtworkKind kind) => kind switch
        {
            ArtworkKind.Poster => (128, 128),
            ArtworkKind.Backdrop => (216, 122),
            _ => (216, 82),
        };

        void Render(PictureTab tab)
        {
            var (w, h) = SizeOf(tab.Kind);
            var elements = new List<UIElement>();
            for (var i = 0; i < tab.Entries.Count; i++)
            {
                var index = i;
                var entry = tab.Entries[i];
                var image = new Image { Stretch = tab.Kind == ArtworkKind.Logo ? Stretch.Uniform : Stretch.UniformToFill, Width = w, Height = h };
                if (entry.Preview is not null) image.Source = entry.Preview;
                else if (entry.Option is { } option)
                {
                    var uri = option.Thumb is { } thumb && thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new Uri(thumb)
                        : client.BuildArtworkUri(option.Thumb ?? option.Key, (int)w * 2, (int)h * 2);
                    if (uri is not null) image.Source = new BitmapImage(uri) { DecodePixelWidth = (int)w * 2 };
                }
                // Logos are mostly light, so they sit on a dark glass whatever the theme.
                var art = new Border
                {
                    Width = w,
                    Height = h,
                    CornerRadius = round && tab.Kind == ArtworkKind.Poster ? new CornerRadius(w / 2) : (CornerRadius)resources["HumeRadiusArtwork"],
                    Background = (Brush)resources[tab.Kind == ArtworkKind.Logo ? "HumeOverlayGlassBrush" : "HumeSubtleFillBrush"],
                    Padding = tab.Kind == ArtworkKind.Logo ? new Thickness(12) : new Thickness(0),
                    Child = image,
                };
                var chosen = index == tab.Chosen;
                var caption = entry.Bytes is not null ? "Your image" : index == tab.Current ? "Current" : string.Empty;
                var stack = new StackPanel { Spacing = 6, Children = { art } };
                stack.Children.Add(new TextBlock
                {
                    Text = caption.Length > 0 ? caption : " ",
                    Style = (Style)resources["HumeSecondaryText"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                // A tile is a button, so the keyboard and screen readers can choose it too; the chosen one is filled, as a chosen chip is.
                var tile = new Button
                {
                    Style = (Style)resources["HumeTileButtonStyle"],
                    Content = stack,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = chosen ? (Brush)resources["HumeChipSelectedBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile,
                    $"{LabelOf(tab.Kind)} {index + 1}{(index == tab.Current ? ", in use" : string.Empty)}{(chosen ? ", chosen" : string.Empty)}{(entry.Option?.Provider is { } p ? $", from {p}" : string.Empty)}");
                tile.Click += (_, _) =>
                {
                    tab.Chosen = index;
                    Render(tab);
                    Refresh();
                };
                elements.Add(tile);
            }
            tab.Tiles.ItemsSource = elements;
        }

        void Refresh()
        {
            dialog.IsPrimaryButtonEnabled = title.Text.Trim().Length > 0 && (DetailsDirty() || tabs.Any(t => t.Dirty));
        }
        title.TextChanged += (_, _) => Refresh();
        summary.TextChanged += (_, _) => Refresh();

        foreach (var tab in tabs)
        {
            var mine = tab;
            mine.Upload.Click += async (_, _) =>
            {
                try
                {
                    if (await Dialogs.PickImageAsync() is not { } bytes) return;
                    var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                    await stream.WriteAsync(bytes.AsBuffer());
                    stream.Seek(0);
                    var preview = new BitmapImage { DecodePixelWidth = (int)SizeOf(mine.Kind).W * 2 };
                    await preview.SetSourceAsync(stream);
                    // One picture of the listener's own at a time: a new one replaces the last, and is chosen.
                    var existing = mine.Entries.FindIndex(e => e.Bytes is not null);
                    if (existing >= 0)
                    {
                        mine.Entries[existing] = new Entry(null, bytes, preview);
                        mine.Chosen = existing;
                    }
                    else
                    {
                        mine.Entries.Insert(0, new Entry(null, bytes, preview));
                        if (mine.Current >= 0) mine.Current++;
                        mine.Chosen = 0;
                    }
                    mine.Status.Text = string.Empty;
                    Render(mine);
                    Refresh();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Runtime.InteropServices.COMException)
                {
                    mine.Status.Text = "That image couldn't be opened.";
                }
            };
        }

        // ---------------------------------------------------------------- the dialog
        var pages = new List<(ChipTab Chip, FrameworkElement Panel)> { (new ChipTab("details", "Details", "Pencil"), detailsPanel) };
        foreach (var tab in tabs)
            pages.Add((new ChipTab(tab.Kind.ToString(), LabelOf(tab.Kind), tab.Kind switch { ArtworkKind.Poster => "Image", ArtworkKind.Backdrop => "Monitor", _ => "Signature" }), tab.Panel));
        var chips = new ChipTabs { AutomationLabel = "Edit sections", ItemsSource = pages.Select(p => p.Chip).ToList() };
        var host = new Grid { Height = 400 };
        foreach (var (_, panel) in pages) host.Children.Add(panel);

        void Show(ChipTab chip)
        {
            foreach (var (c, panel) in pages) panel.Visibility = c == chip ? Visibility.Visible : Visibility.Collapsed;
        }
        chips.RegisterPropertyChangedCallback(ChipTabs.SelectedItemProperty, (_, _) =>
        {
            if (chips.SelectedItem is ChipTab chip) Show(chip);
        });
        var first = open is { } kind ? pages.First(p => p.Chip.Key == kind.ToString()).Chip : pages[0].Chip;
        chips.SelectedItem = first;
        Show(first);

        dialog.Content = new StackPanel { Spacing = 16, Width = 620, Children = { chips, host } };
        dialog.Opened += (_, _) => { if (first.Key == "details") { title.Focus(FocusState.Programmatic); title.SelectAll(); } };

        dialog.PrimaryButtonClick += async (_, e) =>
        {
            var deferral = e.GetDeferral();
            try
            {
                if (title.Text.Trim().Length == 0) { e.Cancel = true; return; }
                if (DetailsDirty())
                {
                    var saved = item is Playlist playlist
                        ? await actions.EditPlaylistAsync(playlist, title.Text.Trim(), summary.Text.Trim())
                        : await actions.EditDetailsAsync(item, title.Text.Trim(), summary.Text.Trim());
                    if (!saved) { e.Cancel = true; return; }
                    changed = true;
                }
                foreach (var tab in tabs.Where(t => t.Dirty))
                {
                    var entry = tab.Entries[tab.Chosen];
                    try
                    {
                        if (entry.Bytes is { } bytes)
                        {
                            // A playlist shows an automatic collage until a cover is chosen, so its upload is chosen as well.
                            if (item is Playlist && tab.Kind == ArtworkKind.Poster) await client.SetPlaylistCoverAsync(item.RatingKey, bytes);
                            else await client.UploadArtworkAsync(item.RatingKey, tab.Kind, bytes);
                        }
                        else if (entry.Option is { } option) await client.SelectArtworkAsync(item.RatingKey, tab.Kind, option);
                        // A logo chosen on an earlier version of Meloday Music overrides the server's; the one chosen here is the server's.
                        if (artist is not null && tab.Kind == ArtworkKind.Logo) await App.Services.GetRequiredService<ArtistLogoService>().ResetAsync(artist);
                        tab.Current = tab.Chosen;
                        changed = true;
                    }
                    catch (PlexException)
                    {
                        tab.Status.Text = "Plex couldn't use that image. Try another.";
                        chips.SelectedItem = pages.First(p => p.Panel == tab.Panel).Chip;
                        e.Cancel = true;
                        return;
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        };

        var show = dialog.ShowAsync();
        await Task.WhenAll(tabs.Select(async tab =>
        {
            try
            {
                var found = await client.GetArtworkOptionsAsync(item.RatingKey, tab.Kind);
                tab.Entries.AddRange(found.Select(o => new Entry(o, null, null)));
                tab.Current = tab.Entries.FindIndex(e => e.Option is { Selected: true });
                tab.Chosen = tab.Current;
                if (found.Count == 0) tab.Status.Text = "Plex hasn't found any images for this. You can upload one.";
            }
            catch (PlexException)
            {
                tab.Status.Text = "Plex couldn't list the images for this item.";
            }
            tab.Busy.IsActive = false;
            tab.Busy.Visibility = Visibility.Collapsed;
            Render(tab);
        }));
        await show;
        if (changed) await actions.NotifyItemChangedAsync(item);
        return changed;
    }
}
