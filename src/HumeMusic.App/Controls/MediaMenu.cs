using HumeMusic.Core.Models;
using HumeMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HumeMusic.Controls;

/// <summary>Builds the right-click / "more" menu for any music item.</summary>
public static class MediaMenu
{
    public static MenuFlyout Create(MediaItem item, IReadOnlyList<Track>? contextTracks = null, int contextIndex = -1)
    {
        // A still cover in a list stands for the thing itself.
        item = LibraryActions.Real(item);
        var actions = App.Services.GetRequiredService<LibraryActions>();
        var player = App.Services.GetRequiredService<PlayerService>();
        var menu = new MenuFlyout();

        MenuFlyoutItem Add(string text, string icon, Action action)
        {
            var mi = new MenuFlyoutItem { Text = text };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
            return mi;
        }

        // The title, summary, cover, backdrop and logo are edited together, as Plex has them, and kept on the Plex server.
        void AddEditItems(MediaItem target) => Add("Edit Details…", "Pencil", () => _ = Views.ItemDetailsDialog.ShowAsync(target));

        // Mute: keep a song or an artist out of what Meloday makes, for the length set in Settings.
        void AddMuteItems(Track? song, string? artistKey, string? artistName)
        {
            var mutes = App.Services.GetRequiredService<MuteService>();
            if (song is not null)
            {
                if (mutes.SongMute(song) is { } muted) Add("Unmute Song", "BellOff", () => mutes.Unmute(muted));
                else Add($"Mute Song for {mutes.DefaultText}", "BellOff", () => _ = mutes.MuteSongAsync(song));
            }
            if (artistKey is null || artistName is null) return;
            if (mutes.ArtistMute(artistKey) is { } artistMuted) Add($"Unmute {artistName}", "BellOff", () => mutes.Unmute(artistMuted));
            else Add($"Mute {artistName} for {mutes.DefaultText}", "BellOff", () => _ = mutes.MuteArtistAsync(artistKey, artistName));
        }

        if (item is Track track && contextTracks is { Count: > 0 } && contextIndex >= 0)
            Add("Play", "Play", () => _ = player.PlayTracksAsync(contextTracks, contextIndex));
        else
            Add("Play", "Play", () => _ = actions.PlayAsync(item));

        if (item is not Track)
            Add("Shuffle", "Shuffle", () => _ = actions.PlayAsync(item, shuffle: true));

        Add("Play Next", "ListStart", () => _ = actions.PlayNextAsync(item));
        Add("Add to Queue", "ListEnd", () => _ = actions.AddToQueueAsync(item));

        if (item is Track or Album or Artist or Playlist or MusicCollection)
            menu.Items.Add(AddToPlaylist(item, contextTracks));

        // Downloads: songs kept on this computer, to play without the server.
        if (item is Track or Album or Playlist)
        {
            var downloads = App.Services.GetRequiredService<DownloadService>();
            List<string> here = item switch
            {
                Track t => downloads.IsDownloaded(t.RatingKey) ? [t.RatingKey] : [],
                Album a => downloads.Tracks.Where(t => t.AlbumRatingKey == a.RatingKey).Select(t => t.RatingKey).ToList(),
                _ => [],
            };
            if (item is not Track || here.Count == 0)
                Add("Download", "Download", async () =>
                {
                    IReadOnlyList<Track> tracks = item is Track one ? [one] : await actions.ResolveTracksAsync(item);
                    downloads.Download(tracks);
                });
            if (here.Count > 0) Add("Remove Download", "Trash2", () => _ = downloads.RemoveAsync(here));
        }

        // Radio from a song, album or artist: Plex's sonic radio with Plex Pass, Meloday's own without.
        if (item is Track or Album or Artist)
        {
            var pass = App.Services.GetRequiredService<PlexPass>().IsAvailable;
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(pass ? "Sonic Radio" : "Radio", "Radio", () => _ = actions.PlaySonicRadioAsync(item));
            // From this track to another, by sonic steps (Plex's sonic analysis: Plex Pass).
            if (pass && item is Track adventure) Add("Sonic Adventure…", "Route", () => _ = Views.SonicAdventureDialog.ShowAsync(adventure));
        }

        if (LibraryActions.CanPin(item))
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(actions.IsPinned(item) ? "Unpin from Home" : "Pin to Home", "Pin", () => actions.TogglePin(item));
            // Pins can be arranged: here, by dragging them in the sidebar, or from Home.
            if (actions.PinPosition(item) is { Index: >= 0 } place && place.Count > 1)
            {
                if (place.Index > 0) Add("Move Pin Earlier", "ChevronUp", () => actions.MovePin(item, -1));
                if (place.Index < place.Count - 1) Add("Move Pin Later", "ChevronDown", () => actions.MovePin(item, 1));
            }
        }

        switch (item)
        {
            case Track t:
                menu.Items.Add(new MenuFlyoutSeparator());
                Add("Song Info", "Info", () => _ = Views.SongInfoDialog.ShowAsync(t));
                if (t.AlbumRatingKey is not null) Add("Go to Album", "Disc3", () => actions.OpenAlbum(t));
                if (t.ArtistRatingKey is not null) Add("Go to Artist", "MicVocal", () => actions.OpenArtist(t.ArtistRatingKey, t.ArtistTitle, t.ArtistThumb));
                Add("Time Lyrics…", "Timer", () => actions.OpenLyricStudio(t));
                menu.Items.Add(new MenuFlyoutSeparator());
                AddMuteItems(t, t.ArtistRatingKey, t.ArtistTitle ?? t.DisplayArtist);
                break;
            case Album a:
                menu.Items.Add(new MenuFlyoutSeparator());
                Add("Open Album", "Disc3", () => actions.Open(a));
                if (a.ArtistRatingKey is not null) Add("Go to Artist", "MicVocal", () => actions.OpenArtist(a.ArtistRatingKey, a.ArtistTitle, a.ArtistThumb));
                AddEditItems(a);
                break;
            case Artist artist:
                menu.Items.Add(new MenuFlyoutSeparator());
                AddEditItems(artist);
                AddMuteItems(null, artist.RatingKey, artist.Title);
                break;
            case MusicCollection collection:
                menu.Items.Add(new MenuFlyoutSeparator());
                AddEditItems(collection);
                break;
            case Station { StationKind: StationKind.Meloday }:
            {
                var meloday = App.Services.GetRequiredService<MelodayService>();
                menu.Items.Add(new MenuFlyoutSeparator());
                Add("Make a New Meloday", "RefreshCw", () => _ = App.Services.GetRequiredService<ViewModels.MelodayViewModel>().LoadAsync(fresh: true));
                if (meloday.Current is { Tracks.Count: > 0 } mix)
                    Add("Save as a Playlist\u2026", "SavePlus", () => _ = Views.PlaylistDialogs.NewAsync(mix.Tracks, mix.Title, mix.Description, mix.CoverPath));
                break;
            }
            case Playlist p:
                menu.Items.Add(new MenuFlyoutSeparator());
                AddEditItems(p);
                if (p.IsSmart) Add("Edit Rules\u2026", "SlidersHorizontal", () => _ = Views.SmartPlaylistDialog.EditAsync(p));
                Danger(Add("Delete Playlist\u2026", "Trash2", () => _ = Views.PlaylistDialogs.DeleteAsync(p)));
                break;
        }
        return menu;
    }

    /// <summary>
    /// "Add to Playlist": a new playlist, or any of the listener's own playlists (most recently
    /// changed first). Within a track list, the selected tracks go together.
    /// </summary>
    private static MenuFlyoutSubItem AddToPlaylist(MediaItem item, IReadOnlyList<Track>? contextTracks)
    {
        var actions = App.Services.GetRequiredService<LibraryActions>();
        var catalog = App.Services.GetRequiredService<PlaylistCatalog>();
        _ = catalog.RefreshAsync();
        IReadOnlyList<MediaItem> items = [item];
        // From a playlist, its tracks go on to another one.
        var sub = new MenuFlyoutSubItem { Text = item is Playlist ? "Add to Another Playlist" : "Add to Playlist" };
        var create = new MenuFlyoutItem { Text = "New Playlist\u2026" };
        create.Click += (_, _) => _ = Views.PlaylistDialogs.NewAsync(items, item is Track ? null : item.Title);
        sub.Items.Add(create);
        var playlists = catalog.Editable.Where(p => p.RatingKey != item.RatingKey).Take(15).ToList();
        if (playlists.Count > 0) sub.Items.Add(new MenuFlyoutSeparator());
        foreach (var playlist in playlists)
        {
            var entry = new MenuFlyoutItem { Text = playlist.Title };
            entry.Click += (_, _) => _ = actions.AddToPlaylistAsync(items, playlist);
            sub.Items.Add(entry);
        }
        return sub;
    }

    /// <summary>A deleting item: bold red, so it stands apart from everything else in the menu.</summary>
    public static MenuFlyoutItem Danger(MenuFlyoutItem item)
    {
        var red = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["HumeDangerBrush"];
        item.Foreground = red;
        item.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
        foreach (var key in new[] { "MenuFlyoutItemForeground", "MenuFlyoutItemForegroundPointerOver", "MenuFlyoutItemForegroundPressed" })
            item.Resources[key] = red;
        return item;
    }

    /// <summary>"Add to Playlist" on its own, for buttons that open straight onto it.</summary>
    public static MenuFlyout AddToPlaylistFlyout(MediaItem item)
    {
        var sub = AddToPlaylist(item, null);
        var entries = sub.Items.ToList();
        sub.Items.Clear();
        var flyout = new MenuFlyout();
        foreach (var entry in entries) flyout.Items.Add(entry);
        return flyout;
    }

    public static void Show(FrameworkElement target, MediaItem item, IReadOnlyList<Track>? contextTracks = null, int contextIndex = -1, Windows.Foundation.Point? at = null)
    {
        var menu = Create(item, contextTracks, contextIndex);
        if (at is { } p) menu.ShowAt(target, new FlyoutShowOptions { Position = p });
        else menu.ShowAt(target, new FlyoutShowOptions { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight });
    }
}
