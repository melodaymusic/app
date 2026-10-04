using HumeMusic.Controls;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace HumeMusic.Views;

/// <summary>
/// Sonic Adventure (Plexamp's, from Plex's sonic analysis): choose where to start and where to end up,
/// and the music travels between them, each track a small sonic step from the last.
/// </summary>
public static class SonicAdventureDialog
{
    public static async Task ShowAsync(Track? start = null)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section) return;
        start ??= App.Services.GetRequiredService<PlayerService>().CurrentTrack;
        Track? end = null;

        var dialog = Dialogs.Create("Sonic Adventure");
        var fromRow = new Grid { ColumnSpacing = 12, Visibility = Visibility.Collapsed };
        var toRow = new Grid { ColumnSpacing = 12, Visibility = Visibility.Collapsed };
        var fromPicker = Picker("Start with a track", t => { start = t; Update(); });
        var toPicker = Picker("End up at a track", t => { end = t; Update(); });
        dialog.Content = new StackPanel
        {
            Spacing = 16,
            MinWidth = 460,
            Children =
            {
                Dialogs.Text("Choose a track to start with and one to end up at. The music travels between them, each track a small sonic step from the last."),
                Section("From", fromPicker, fromRow),
                Section("To", toPicker, toRow),
            },
        };
        dialog.PrimaryButtonText = "Start Adventure";
        dialog.CloseButtonText = "Cancel";

        void Update()
        {
            Show(fromRow, start);
            Show(toRow, end);
            dialog.IsPrimaryButtonEnabled = start is not null && end is not null && start.RatingKey != end.RatingKey;
        }

        AutoSuggestBox Picker(string placeholder, Action<Track> chosen)
        {
            var box = new AutoSuggestBox { PlaceholderText = placeholder, QueryIcon = new SymbolIcon(Symbol.Find), MinWidth = 440 };
            var version = 0;
            box.TextChanged += async (b, args) =>
            {
                if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
                var mine = ++version;
                var query = b.Text.Trim();
                if (query.Length < 2)
                {
                    b.ItemsSource = null;
                    return;
                }
                await Task.Delay(250);
                if (mine != version) return;
                try
                {
                    var results = await client.SearchAsync(query, section.Key, 8);
                    if (mine == version) b.ItemsSource = results.Tracks.Select(t => new Choice(t)).ToList();
                }
                catch (PlexException)
                {
                    b.ItemsSource = null;
                }
            };
            box.SuggestionChosen += (b, args) =>
            {
                if (args.SelectedItem is Choice choice) chosen(choice.Track);
                b.Text = string.Empty;
                b.ItemsSource = null;
            };
            return box;
        }

        Update();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && start is not null && end is not null)
            await App.Services.GetRequiredService<LibraryActions>().PlaySonicAdventureAsync(start, end);
    }

    private static StackPanel Section(string label, AutoSuggestBox picker, Grid row) => new()
    {
        Spacing = 8,
        Children = { new TextBlock { Text = label, Style = (Style)Application.Current.Resources["HumeBodyStrongText"] }, row, picker },
    };

    /// <summary>The chosen track: its artwork, title and artist.</summary>
    private static void Show(Grid row, Track? track)
    {
        row.Children.Clear();
        row.ColumnDefinitions.Clear();
        row.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
        if (track is null) return;
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var resources = Application.Current.Resources;
        row.Children.Add(new ArtworkImage
        {
            Width = 40, Height = 40, DecodeSize = 80, SourcePath = track.AlbumThumb ?? track.Thumb,
            ArtworkCornerRadius = (CornerRadius)resources["HumeRadiusThumb"], PlaceholderIcon = "Music",
        });
        var text = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = track.Title, Style = (Style)resources["HumeBodyStrongText"] },
                new TextBlock { Text = track.ArtistTitle, Style = (Style)resources["HumeSecondaryText"] },
            },
        };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
    }

    private sealed record Choice(Track Track)
    {
        public override string ToString() => $"{Track.Title} — {Track.ArtistTitle}";
    }
}
