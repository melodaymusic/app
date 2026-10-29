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
        using var lifetime = new CancellationTokenSource();
        var open = true;
        var suggestionVersion = 0;
        var pickButtons = new List<(MediaItem Item, Button Button)>();

        var dialog = Dialogs.Create(albums ? "Album Radio Builder" : "Artist Radio Builder");
        dialog.PrimaryButtonText = "Play Radio";
        dialog.SecondaryButtonText = "Save as Playlist";
        dialog.CloseButtonText = "Cancel";
        dialog.Closed += (_, _) => { open = false; lifetime.Cancel(); };

        var heading = new TextBlock { Style = (Style)resources["MelodayGroupHeaderText"] };
        var rows = new StackPanel { Style = (Style)resources["MelodayGroupRowsStyle"] };
        var group = new StackPanel
        {
            Style = (Style)resources["MelodayGroupStyle"],
            Visibility = Visibility.Collapsed,
            Children = { heading, new Border { Style = (Style)resources["MelodayGroupListStyle"], Child = rows } },
        };
        var suggestions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var suggestionsHost = new StackPanel
        {
            Spacing = 8,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new TextBlock { Text = "You might add", Style = (Style)resources["MelodayBodyStrongText"] },
                new ScrollViewer { Content = suggestions, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled },
            },
        };
        var recentPicks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiScale.Size(8) };
        var topPicks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = UiScale.Size(8) };
        var picksStatus = Dialogs.Note("Loading your listening picks…");
        StackPanel PicksSection(string title, StackPanel picks) => new()
        {
            Spacing = UiScale.Size(8), MinHeight = UiScale.Size(130),
            Children = { new TextBlock { Text = title, Style = (Style)resources["MelodayBodyStrongText"] }, picks },
        };
        var recentSection = PicksSection(albums ? "Recently played albums" : "Recently played artists", recentPicks);
        var topSection = PicksSection(albums ? "Top albums" : "Top artists", topPicks);
        SearchPicker search = null!;
        search = new SearchPicker(albums ? "Search for an album" : "Search for an artist", async query =>
        {
            var results = await client.SearchAsync(query, section.Key, 8);
            return albums
                ? results.Albums.Select(a => new PickResult(a, a.Title, a.ArtistTitle, a.Thumb, "Disc3", Dialogs.AlbumRadius)).ToList()
                : results.Artists.Select(a => new PickResult(a, a.Title, null, a.Thumb, "MicVocal", Dialogs.ArtistRadius)).ToList();
        }, item => Add(item));

        var content = new StackPanel
        {
            Spacing = UiScale.Size(24),
            Width = Dialogs.ContentWidth(480),
            Children =
            {
                Dialogs.Text(albums
                    ? "Only music from your selected albums. Choose up to 10."
                    : "Only music from your selected artists. Choose up to 10."),
                search.Root,
                recentSection,
                topSection,
                picksStatus,
                group,
                suggestionsHost,
            },
        };

        dialog.Content = new ScrollViewer
        {
            Content = content, MaxHeight = Dialogs.ContentHeight(520),
            HorizontalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        async Task LoadPicksAsync()
        {
            try
            {
                using (MelodayMusic.Core.Caching.ResponseCache.FetchFresh())
                {
                    async Task<IReadOnlyList<MediaItem>> Fetch(string sort)
                    {
                        var filters = new[] { new KeyValuePair<string, string?>("viewCount>", "0") };
                        return albums
                            ? (await client.GetAlbumsAsync(section.Key, 0, 5, sort, lifetime.Token, filters)).Items.Cast<MediaItem>().ToList()
                            : (await client.GetArtistsAsync(section.Key, 0, 5, sort, lifetime.Token, filters)).Items.Cast<MediaItem>().ToList();
                    }
                    var recent = Fetch("lastViewedAt:desc");
                    var top = Fetch("viewCount:desc");
                    await Task.WhenAll(recent, top);
                    if (!open || session.Client != client || session.Section?.Key != section.Key) return;
                    FillPicks(recentPicks, recent.Result);
                    FillPicks(topPicks, top.Result);
                    picksStatus.Text = recent.Result.Count + top.Result.Count == 0
                        ? "No listening history yet. Search above to choose your music." : string.Empty;
                    picksStatus.Visibility = string.IsNullOrEmpty(picksStatus.Text) ? Visibility.Collapsed : Visibility.Visible;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!open) return;
                Microsoft.Extensions.Logging.LoggerExtensions.LogDebug(
                    App.Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<LibraryActions>>(), ex, "Radio builder picks unavailable");
                picksStatus.Text = "Listening picks are unavailable. You can still search above.";
            }
        }

        void FillPicks(StackPanel host, IReadOnlyList<MediaItem> items)
        {
            host.Children.Clear();
            foreach (var item in items)
            {
                var size = UiScale.Size(80);
                var button = new Button
                {
                    Style = (Style)resources["MelodayTileButtonStyle"], Padding = new Thickness(UiScale.Size(4)),
                    CornerRadius = new CornerRadius(UiScale.ArtworkRadius + UiScale.Size(4)),
                    Content = new StackPanel
                    {
                        Width = size, Spacing = UiScale.Size(6),
                        Children =
                        {
                            new ArtworkImage { Width = size, Height = size, DecodeSize = 160, SourcePath = item.Thumb,
                                ArtworkCornerRadius = albums ? Dialogs.AlbumRadius : new CornerRadius(size / 2), PlaceholderIcon = albums ? "Disc3" : "MicVocal" },
                            new TextBlock { Text = item.Title, Style = (Style)resources["MelodaySecondaryText"],
                                TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center },
                        },
                    },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, "Add " + item.Title);
                ToolTipService.SetToolTip(button, item.Title);
                button.Click += (_, _) => Add(item);
                host.Children.Add(button);
                pickButtons.Add((item, button));
            }
            Refresh();
        }

        void Refresh()
        {
            foreach (var (item, button) in pickButtons)
                button.IsEnabled = chosen.Count < MaxChoices && !chosen.Any(c => c.RatingKey == item.RatingKey);
            rows.Children.Clear();
            foreach (var item in chosen)
            {
                var remove = new Button
                {
                    Style = (Style)resources["MelodayIconButtonStyle"],
                    Content = new LucideIcon { Icon = "X", Width = (double)resources["MelodayIconXS"], Height = (double)resources["MelodayIconXS"] },
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(remove, $"Remove {item.Title}");
                remove.Click += (_, _) => { chosen.Remove(item); Refresh(); };
                var row = Dialogs.MediaRow(item.Thumb, item.Title, (item as Album)?.ArtistTitle, albums ? "Disc3" : "MicVocal",
                    albums ? Dialogs.AlbumRadius : Dialogs.ArtistRadius, 40, remove);
                row.Style = (Style)resources["MelodayGroupRowStyle"];
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

        async Task SuggestAsync()
        {
            var version = ++suggestionVersion;
            if (chosen.Count == 0)
            {
                suggestionsHost.Visibility = Visibility.Collapsed;
                return;
            }
            try
            {
                var related = await client.GetRelatedHubsAsync(chosen[^1].RatingKey, 10, lifetime.Token);
                if (!open || version != suggestionVersion) return;
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
            catch (OperationCanceledException) { }
            catch (PlexException)
            {
                if (open && version == suggestionVersion) suggestionsHost.Visibility = Visibility.Collapsed;
            }
        }

        Refresh();
        _ = LoadPicksAsync();
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
