using MelodayMusic.Controls;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MelodayMusic.Views;

public static class SonicAdventureDialog
{
    public static async Task ShowAsync(Track? start = null)
    {
        var session = App.Services.GetRequiredService<PlexSession>();
        if (session.Client is not { } client || session.Section is not { } section) return;
        start ??= App.Services.GetRequiredService<PlayerService>().CurrentTrack;
        Track? end = null;
        var resources = Application.Current.Resources;

        var dialog = Dialogs.Create("Sonic Adventure");
        dialog.PrimaryButtonText = "Start Adventure";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = false;

        End from = null!, to = null!;
        from = new End("From", "Search for the song to start with", client, section.Key, t => { start = t; Update(); });
        to = new End("To", "Search for the song to end up at", client, section.Key, t => { end = t; Update(); });
        from.Change.Click += (_, _) => { start = null; Update(); from.Search.Box.Focus(FocusState.Programmatic); };
        to.Change.Click += (_, _) => { end = null; Update(); to.Search.Box.Focus(FocusState.Programmatic); };

        dialog.Content = new ScrollViewer { MaxHeight = Dialogs.ContentHeight(520), Content = new StackPanel
        {
            Spacing = MelodayMusic.Services.UiScale.Size(24),
            Width = Dialogs.ContentWidth(480),
            Children =
            {
                Dialogs.Text("A musical path between two songs."),
                from.Root,
                to.Root,
            },
        } };

        void Update()
        {
            from.Show(start);
            to.Show(end);
            dialog.IsPrimaryButtonEnabled = start is not null && end is not null && start.RatingKey != end.RatingKey;
        }

        Update();
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && start is not null && end is not null)
            await App.Services.GetRequiredService<LibraryActions>().PlaySonicAdventureAsync(start, end);
    }

    private sealed class End
    {
        private readonly Border _chosen;
        private readonly ArtworkImage _art;
        private readonly TextBlock _title;
        private readonly TextBlock _detail;

        public End(string heading, string placeholder, PlexServerClient client, string sectionKey, Action<Track> picked)
        {
            var resources = Application.Current.Resources;
            Search = new SearchPicker(placeholder, async query =>
            {
                var results = await client.SearchAsync(query, sectionKey, 8);
                return results.Tracks.Select(t => new PickResult(t, t.Title, Details(t), t.AlbumThumb ?? t.Thumb, "Music", Dialogs.AlbumRadius)).ToList();
            }, item => { if (item is Track t) picked(t); });
            Change = Dialogs.SecondaryButton("Change");
            _art = new ArtworkImage { Width = 40, Height = 40, DecodeSize = 80,
                ArtworkCornerRadius = Dialogs.AlbumRadius, PlaceholderIcon = "Music" };
            _title = new TextBlock { Style = (Style)resources["MelodayBodyStrongText"], TextTrimming = TextTrimming.CharacterEllipsis };
            _detail = new TextBlock { Style = (Style)resources["MelodaySecondaryText"], TextTrimming = TextTrimming.CharacterEllipsis };
            var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { _title, _detail } };
            var row = new Grid { Style = (Style)resources["MelodayGroupRowStyle"] };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(text, 1);
            Grid.SetColumn(Change, 2);
            Change.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(_art);
            row.Children.Add(text);
            row.Children.Add(Change);
            _chosen = new Border { Style = (Style)resources["MelodayGroupListStyle"], Child = row, Visibility = Visibility.Collapsed };
            Root = new StackPanel
            {
                Style = (Style)resources["MelodayGroupStyle"],
                Children = { new TextBlock { Text = heading, Style = (Style)resources["MelodayGroupHeaderText"] }, _chosen, Search.Root },
            };
        }

        public StackPanel Root { get; }

        public SearchPicker Search { get; }

        public Button Change { get; }

        public void Show(Track? track)
        {
            _chosen.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
            Search.Root.Visibility = track is null ? Visibility.Visible : Visibility.Collapsed;
            if (track is null) return;
            _art.SourcePath = track.AlbumThumb ?? track.Thumb;
            _title.Text = track.Title;
            _detail.Text = Details(track);
        }
    }

    private static string Details(Track t) =>
        string.IsNullOrEmpty(t.AlbumTitle) ? t.DisplayArtist : $"{t.DisplayArtist} · {t.AlbumTitle}";
}
