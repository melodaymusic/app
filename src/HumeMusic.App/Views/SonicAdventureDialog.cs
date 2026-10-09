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
        var resources = Application.Current.Resources;

        var dialog = Dialogs.Create("Sonic Adventure");
        dialog.PrimaryButtonText = "Start Adventure";
        dialog.CloseButtonText = "Cancel";
        dialog.IsPrimaryButtonEnabled = false;

        // Each end of the journey is a song: once chosen it is shown, with a way to change it; until then a search finds it.
        End from = null!, to = null!;
        from = new End("From", "Search for the song to start with", client, section.Key, t => { start = t; Update(); });
        to = new End("To", "Search for the song to end up at", client, section.Key, t => { end = t; Update(); });
        from.Change.Click += (_, _) => { start = null; Update(); from.Search.Box.Focus(FocusState.Programmatic); };
        to.Change.Click += (_, _) => { end = null; Update(); to.Search.Box.Focus(FocusState.Programmatic); };

        dialog.Content = new StackPanel
        {
            Spacing = 24,
            Width = 480,
            Children =
            {
                Dialogs.Text("Choose a song to start with and one to end up at. The music travels between them, each song a small sonic step from the last."),
                from.Root,
                to.Root,
            },
        };

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

    /// <summary>One end of the journey: its heading, the song once chosen (in a group row, with Change), and the search before.</summary>
    private sealed class End
    {
        private readonly Border _chosen;
        private readonly Border _group;

        public End(string heading, string placeholder, PlexServerClient client, string sectionKey, Action<Track> picked)
        {
            var resources = Application.Current.Resources;
            Search = new SearchPicker(placeholder, async query =>
            {
                var results = await client.SearchAsync(query, sectionKey, 8);
                return results.Tracks.Select(t => new PickResult(t, t.Title, Details(t), t.AlbumThumb ?? t.Thumb, "Music", Dialogs.AlbumRadius)).ToList();
            }, item => { if (item is Track t) picked(t); });
            Change = Dialogs.SecondaryButton("Change");
            _chosen = new Border { Style = (Style)resources["HumeGroupListStyle"] };
            _group = _chosen;
            _group.Visibility = Visibility.Collapsed;
            Root = new StackPanel
            {
                Style = (Style)resources["HumeGroupStyle"],
                Children = { new TextBlock { Text = heading, Style = (Style)resources["HumeGroupHeaderText"] }, _group, Search.Root },
            };
        }

        public StackPanel Root { get; }

        public SearchPicker Search { get; }

        public Button Change { get; }

        public void Show(Track? track)
        {
            // The Change button moves to the new row, so it leaves the old one first.
            _chosen.Child = null;
            _group.Visibility = track is null ? Visibility.Collapsed : Visibility.Visible;
            Search.Root.Visibility = track is null ? Visibility.Visible : Visibility.Collapsed;
            if (track is null) return;
            var row = Dialogs.MediaRow(track.AlbumThumb ?? track.Thumb, track.Title, Details(track), "Music", Dialogs.AlbumRadius, 40, Change);
            row.Style = (Style)Application.Current.Resources["HumeGroupRowStyle"];
            _chosen.Child = row;
        }
    }

    private static string Details(Track t) =>
        string.IsNullOrEmpty(t.AlbumTitle) ? t.DisplayArtist : $"{t.DisplayArtist} · {t.AlbumTitle}";
}
