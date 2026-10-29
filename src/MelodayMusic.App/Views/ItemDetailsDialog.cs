using System.Runtime.InteropServices.WindowsRuntime;
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

public static class ItemDetailsDialog
{
    private sealed record Entry(ArtworkOption? Option, byte[]? Bytes, BitmapImage? Preview, Core.Logos.FanartLogo? Fanart = null);

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
            Children = { title, summary, Dialogs.Note("Changes apply to your Plex library.") },
        };
        bool DetailsDirty() => title.Text.Trim() != item.Title || summary.Text.Trim() != originalSummary.Trim();

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
                ArtworkKind.Poster => artist is not null ? "Photo" : "Cover",
                ArtworkKind.Backdrop => "Backdrop",
                _ => "Transparent logo",
            }));
            Grid.SetColumn(upload, 1);
            header.Children.Add(upload);
            var body = new StackPanel { Spacing = 12, Children = { header, busy, status } };
            var scroller = new ScrollViewer { Content = tiles, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
            var panel = new Grid { RowSpacing = 12, Height = Dialogs.ContentHeight(400), Visibility = Visibility.Collapsed };
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
                var inset = tab.Kind == ArtworkKind.Logo ? 12 : 0;
                var image = new Image { Stretch = tab.Kind == ArtworkKind.Logo ? Stretch.Uniform : Stretch.UniformToFill, Width = w - inset * 2, Height = h - inset * 2 };
                if (entry.Preview is not null) image.Source = entry.Preview;
                else if (entry.Fanart is { } fanart) image.Source = new BitmapImage(new Uri(fanart.Url)) { DecodePixelWidth = (int)w * 2 };
                else if (entry.Option is { } option)
                {
                    var uri = option.Thumb is { } thumb && thumb.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        ? new Uri(thumb)
                        : client.BuildArtworkUri(option.Thumb ?? option.Key, (int)w * 2, (int)h * 2);
                    if (uri is not null) image.Source = new BitmapImage(uri) { DecodePixelWidth = (int)w * 2 };
                }
                var art = new Border
                {
                    Width = w,
                    Height = h,
                    CornerRadius = round && tab.Kind == ArtworkKind.Poster ? new CornerRadius(w / 2) : (CornerRadius)resources["MelodayRadiusArtwork"],
                    Background = (Brush)resources[tab.Kind == ArtworkKind.Logo ? "MelodayOverlayGlassBrush" : "MelodaySubtleFillBrush"],
                    Padding = new Thickness(inset),
                    Child = image,
                };
                var chosen = index == tab.Chosen;
                var caption = entry.Bytes is not null ? "Your image" : index == tab.Current ? "Current" : entry.Fanart is not null ? "fanart.tv" : string.Empty;
                var stack = new StackPanel { Spacing = 6, Children = { art } };
                stack.Children.Add(new TextBlock
                {
                    Text = caption.Length > 0 ? caption : " ",
                    Style = (Style)resources["MelodaySecondaryText"],
                    HorizontalAlignment = HorizontalAlignment.Center,
                });
                var tile = new Button
                {
                    Style = (Style)resources["MelodayTileButtonStyle"],
                    Content = stack,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Background = chosen ? (Brush)resources["MelodayChipSelectedBrush"] : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tile,
                    $"{LabelOf(tab.Kind)} {index + 1}{(index == tab.Current ? ", in use" : string.Empty)}{(chosen ? ", chosen" : string.Empty)}{(entry.Option?.Provider is { } p ? $", from {p}" : entry.Fanart is not null ? ", from fanart.tv" : string.Empty)}");
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

        var pages = new List<(ChipTab Chip, FrameworkElement Panel)> { (new ChipTab("details", "Details", "Pencil"), detailsPanel) };
        foreach (var tab in tabs)
            pages.Add((new ChipTab(tab.Kind.ToString(), LabelOf(tab.Kind), tab.Kind switch { ArtworkKind.Poster => "Image", ArtworkKind.Backdrop => "Monitor", _ => "Signature" }), tab.Panel));
        var chips = new ChipTabs { AutomationLabel = "Edit sections", ItemsSource = pages.Select(p => p.Chip).ToList() };
        var host = new Grid { Height = Dialogs.ContentHeight(400) };
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

        var saveStatus = Dialogs.Note(string.Empty);
        saveStatus.Visibility = Visibility.Collapsed;
        dialog.Content = new StackPanel { Spacing = 16, Width = Dialogs.ContentWidth(620), Children = { chips, host, saveStatus } };
        dialog.Opened += (_, _) => { if (first.Key == "details") { title.Focus(FocusState.Programmatic); title.SelectAll(); } };

        dialog.PrimaryButtonClick += async (_, e) =>
        {
            if (!await Dialogs.SaveAsync(e, async () =>
            {
                if (title.Text.Trim().Length == 0) return false;
                if (DetailsDirty())
                {
                    var saved = item is Playlist playlist
                        ? await actions.EditPlaylistAsync(playlist, title.Text.Trim(), summary.Text.Trim())
                        : await actions.EditDetailsAsync(item, title.Text.Trim(), summary.Text.Trim());
                    if (!saved) return false;
                    changed = true;
                }
                foreach (var tab in tabs.Where(t => t.Dirty))
                {
                    var entry = tab.Entries[tab.Chosen];
                    try
                    {
                        if (entry.Bytes is { } bytes)
                        {
                            if (item is Playlist && tab.Kind == ArtworkKind.Poster) await client.SetPlaylistCoverAsync(item.RatingKey, bytes);
                            else await client.UploadArtworkAsync(item.RatingKey, tab.Kind, bytes);
                        }
                        else if (entry.Option is { } option) await client.SelectArtworkAsync(item.RatingKey, tab.Kind, option);
                        var logos = App.Services.GetRequiredService<ArtistLogoService>();
                        if (artist is not null && entry.Fanart is { } fanart)
                        {
                            if (!await logos.ChooseAsync(artist, fanart)) throw new PlexException("fanart.tv logo unavailable");
                        }
                        else if (artist is not null && tab.Kind == ArtworkKind.Logo) await logos.ResetAsync(artist);
                        tab.Current = tab.Chosen;
                        changed = true;
                    }
                    catch (PlexException)
                    {
                        tab.Status.Text = "Plex couldn't use that image. Try another.";
                        chips.SelectedItem = pages.First(p => p.Panel == tab.Panel).Chip;
                        return false;
                    }
                }
                return true;
            }))
            {
                saveStatus.Text = "Couldn't save all changes. Check your connection and try again.";
                saveStatus.Visibility = Visibility.Visible;
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
                if (artist is not null && tab.Kind == ArtworkKind.Logo) await AddFanartAsync(tab, artist);
                if (tab.Entries.Count == 0) tab.Status.Text = tab.Kind == ArtworkKind.Logo && artist is not null
                    ? "Neither Plex nor fanart.tv has a logo for this artist. You can upload one."
                    : "Plex hasn't found any images for this. You can upload one.";
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

    private static async Task AddFanartAsync(PictureTab tab, Artist artist)
    {
        var logos = App.Services.GetRequiredService<ArtistLogoService>();
        if (!logos.HasKey)
        {
            tab.Status.Text = "Add a fanart.tv key in Settings to choose from its logos too.";
            return;
        }
        try
        {
            var (result, found, current) = await logos.GetOptionsAsync(artist);
            if (result == Core.Logos.FanartResult.InvalidKey)
            {
                tab.Status.Text = "fanart.tv didn't accept the key in Settings, so only Plex's logos are shown.";
                return;
            }
            foreach (var logo in found)
            {
                tab.Entries.Add(new Entry(null, null, null, logo));
                if (logo.Url == current) tab.Current = tab.Chosen = tab.Entries.Count - 1;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            tab.Status.Text = "fanart.tv couldn't be reached, so only Plex's logos are shown.";
        }
    }
}
