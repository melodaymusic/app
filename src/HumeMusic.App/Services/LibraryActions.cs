using HumeMusic.Core.Models;
using HumeMusic.Core.Pins;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Views;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// Actions available on any music item from anywhere in the UI (play, queue, open, radio, rate).
/// Resolves albums, artists and playlists into track lists via Plex.
/// </summary>
public sealed class LibraryActions
{
    private readonly PlexSession _session;
    private readonly PlayerService _player;
    private readonly NavigationService _navigation;
    private readonly NotificationService _notifications;
    private readonly PinStore _pins;
    private readonly MixService _mixes;
    private readonly Dislikes _dislikes;
    private readonly PlexPass _pass;
    private readonly MelodayService _meloday;
    private readonly LocalRadio _localRadio;
    private readonly Core.Home.RecentStore _recents;
    private readonly StaticCovers _covers;
    private readonly LiveRadio _live;
    private Station? _lastLive;
    private readonly ILogger _log;

    public LibraryActions(PlexSession session, PlayerService player, NavigationService navigation, NotificationService notifications,
        PinStore pins, MixService mixes, Dislikes dislikes, PlexPass pass, MelodayService meloday, LocalRadio localRadio, Core.Home.RecentStore recents, StaticCovers covers, LiveRadio live, ILogger<LibraryActions> log)
    {
        _recents = recents;
        _covers = covers;
        _live = live;
        // Live radio stopped and played again is rejoined where it is by then.
        player.LiveRejoinRequested += (_, _) =>
        {
            if (_lastLive is { } station) _ = PlayMixAsync(station);
        };
        _localRadio = localRadio;
        _meloday = meloday;
        _pass = pass;
        _mixes = mixes;
        _dislikes = dislikes;
        _pins = pins;
        _session = session;
        _player = player;
        _navigation = navigation;
        _notifications = notifications;
        _log = log;
    }

    /// <summary>
    /// What a card stands for: a still cover of a station, mix or tag (in Pinned, Jump back in) is the thing
    /// itself, which is what opens, plays and gets a menu.
    /// </summary>
    public static MediaItem Real(MediaItem item) => item is PinnedCover cover ? cover.Target : item;

    public void Open(MediaItem item)
    {
        item = Real(item);
        switch (item)
        {
            case Artist a: _navigation.Navigate(typeof(ArtistPage), a); break;
            case Album a: _navigation.Navigate(typeof(AlbumPage), a); break;
            case Playlist p: _navigation.Navigate(typeof(PlaylistPage), p); break;
            case MusicCollection c: _navigation.Navigate(typeof(CollectionPage), c); break;
            case TagItem { IsFamily: true } family: _navigation.Navigate(typeof(TagHubPage), family); break;
            case TagItem t: _navigation.Navigate(typeof(TagPage), t); break;
            case Track t: _ = PlayAsync(t); break;
            case Station { IsBuilder: true } builder: _ = MixBuilderDialog.ShowAsync(builder.StationKind); break;
            case Station { StationKind: StationKind.Meloday }: _navigation.Navigate(typeof(MelodayPage)); break;
            case ArtistMix or TagMix or Station or BuiltMix: _ = PlayAsync(item); break;
        }
    }

    // ------------------------------------------------------------------ Home pins

    public static bool CanPin(MediaItem item) => PinnedItem.KindOf(item) is not null;

    public bool IsPinned(MediaItem item) =>
        _session.Server?.MachineIdentifier is { } server && _pins.IsPinned(server, item.RatingKey);

    /// <summary>Where a pinned item sits among the pins (index, count); index -1 when it isn't pinned.</summary>
    public (int Index, int Count) PinPosition(MediaItem item) =>
        _session.Server?.MachineIdentifier is { } server ? _pins.Position(server, item.RatingKey) : (-1, 0);

    /// <summary>Moves a pinned item earlier (negative) or later (positive) in the pins.</summary>
    public bool MovePin(MediaItem item, int offset) =>
        _session.Server?.MachineIdentifier is { } server && _pins.Move(server, item.RatingKey, offset);

    /// <summary>Arranges the pins in the order given (after dragging them in the sidebar).</summary>
    public void ReorderPins(IEnumerable<string> ratingKeys)
    {
        if (_session.Server?.MachineIdentifier is { } server) _pins.Reorder(server, ratingKeys);
    }

    /// <summary>Pins or unpins an artist, album or playlist on Home. Returns the new pinned state.</summary>
    public bool TogglePin(MediaItem item)
    {
        item = Real(item);
        if (!CanPin(item) || _session.Server?.MachineIdentifier is not { } server) return false;
        if (_pins.IsPinned(server, item.RatingKey))
        {
            _pins.Unpin(server, item.RatingKey);
            _notifications.ShowInfo("Unpinned from Home", item.Title);
            return false;
        }
        _pins.Pin(server, item);
        _notifications.ShowInfo("Pinned to Home", item.Title);
        // What moves is pinned as a still: its cover is drawn now and set on the pin when it is ready.
        if (StaticCovers.Applies(item)) _ = CoverPinAsync(server, item);
        return true;
    }

    private async Task CoverPinAsync(string server, MediaItem item)
    {
        if (await _covers.GetAsync(item).ConfigureAwait(false) is { } cover) _pins.SetThumb(server, item.RatingKey, cover);
    }

    /// <summary>Gives every pin that is a station, mix or tag its still cover (drawn if need be) and keeps Meloday's pin up to date.</summary>
    public async Task EnsurePinCoversAsync()
    {
        if (_session.Server?.MachineIdentifier is not { } server) return;
        foreach (var pin in _pins.GetPins(server).Where(p => ItemRefs.IsSynthetic(p.Kind)).ToList())
        {
            if (ItemRefs.ToItem(pin.Kind, pin.RatingKey, pin.Title, pin.Thumb, pin.Data) is not { } item || !StaticCovers.Applies(item)) continue;
            var cover = await _covers.GetAsync(item).ConfigureAwait(false);
            if (cover is not null && cover != pin.Thumb) _pins.SetThumb(server, pin.RatingKey, cover);
        }
    }

    public void OpenAlbum(Track track)
    {
        if (track.AlbumRatingKey is null) return;
        _navigation.Navigate(typeof(AlbumPage), new Album
        {
            RatingKey = track.AlbumRatingKey,
            Title = track.AlbumTitle ?? string.Empty,
            Thumb = track.AlbumThumb,
            ArtistTitle = track.ArtistTitle,
            ArtistRatingKey = track.ArtistRatingKey,
        });
    }

    public void OpenArtist(string? artistRatingKey, string? title, string? thumb = null)
    {
        if (artistRatingKey is null) return;
        _navigation.Navigate(typeof(ArtistPage), new Artist { RatingKey = artistRatingKey, Title = title ?? string.Empty, Thumb = thumb });
    }

    /// <summary>Plays an item: a track alone, or the full contents of an album/artist/playlist/collection.</summary>
    public async Task PlayAsync(MediaItem item, bool shuffle = false)
    {
        item = Real(item);
        if (item is Station { IsBuilder: true }) return;
        RecordPlayed(item);
        if (item is Station { StationKind: StationKind.Meloday })
        {
            await PlayMelodayAsync(shuffle);
            return;
        }
        if (item is ArtistMix or TagMix or Station or BuiltMix)
        {
            await PlayMixAsync(item);
            return;
        }
        var tracks = await ResolveTracksAsync(item);
        if (tracks.Count > 0) await _player.PlayTracksAsync(tracks, 0, shuffle);
    }

    /// <summary>Notes that the listener played something (Home's "Jump back in" shows the latest).</summary>
    public void RecordPlayed(MediaItem item)
    {
        if (_session.Server?.MachineIdentifier is { } server) _recents.Record(server, Real(item));
    }

    public async Task PlayNextAsync(MediaItem item)
    {
        item = Real(item);
        var tracks = await ResolveTracksAsync(item);
        _player.PlayNext(tracks);
    }

    public async Task AddToQueueAsync(MediaItem item)
    {
        item = Real(item);
        var tracks = await ResolveTracksAsync(item);
        _player.AddToQueue(tracks);
    }

    /// <summary>
    /// Plays a mix (an artist's radio station, or a genre or mood mix): a first helping now, then
    /// topped up as it plays. Disliked music is left out throughout.
    /// </summary>
    public async Task PlayMixAsync(MediaItem mix)
    {
        mix = Real(mix);
        try
        {
            // A live station is joined where it is by now, in the middle of a song.
            if (mix is Station { IsLive: true } station && await _live.TuneInAsync(station) is { } tuning)
            {
                _lastLive = station;
                await _player.PlayLiveAsync(station.Title, tuning);
                _notifications.ShowInfo("Live", $"Tuned in to {station.Title}");
                return;
            }
            var first = await _mixes.FirstTracksAsync(mix);
            if (first is null || first.Count == 0)
            {
                _notifications.ShowInfo("Nothing to play yet", $"There isn't enough music for {mix.Title} yet.");
                return;
            }
            if (_mixes.MoreTracks(mix) is { } more) await _player.PlayMixAsync(first, more);
            else await _player.PlayTracksAsync(first);
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't start the mix");
        }
    }

    /// <summary>Plays the Meloday for this part of the day, making it first when there isn't one yet.</summary>
    public async Task PlayMelodayAsync(bool shuffle = false)
    {
        if (!_meloday.IsMaking && _meloday.Current is null) _notifications.ShowInfo("Making your Meloday", "It will start in a moment.");
        var mix = await _meloday.GetAsync();
        if (mix is null || mix.Tracks.Count == 0)
        {
            _notifications.ShowInfo("No Meloday yet", "There isn't enough listening history to make one yet.");
            return;
        }
        await _player.PlayTracksAsync(mix.Tracks, 0, shuffle);
    }

    /// <summary>Starts a "sonic radio": the seed item followed by its sonically similar tracks.</summary>
    /// <summary>
    /// Sonic Adventure: the music travels from one track to another, each track a small sonic step
    /// from the last (Plex's computePath). A Plex Pass feature, like all of Plex's sonic analysis.
    /// </summary>
    public async Task PlaySonicAdventureAsync(Track start, Track end)
    {
        if (_session.Client is not { } client || _session.Section is not { } section || !_pass.IsAvailable) return;
        try
        {
            var path = await client.GetSonicAdventureAsync(section.Key, start.RatingKey, end.RatingKey);
            var disliked = await _dislikes.GetAsync();
            // The two ends as chosen; the steps between them, without anything rated one star or less.
            var tracks = new List<Track> { start };
            tracks.AddRange(path.Where(t => t.RatingKey != start.RatingKey && t.RatingKey != end.RatingKey && disliked.Eligible(t)).DistinctBy(t => t.RatingKey));
            tracks.Add(end);
            if (tracks.Count <= 2)
            {
                _notifications.ShowInfo("No way between them", "Plex couldn't find a sonic path between these two tracks. Try others, or wait until sonic analysis has finished.");
                return;
            }
            await _player.PlayTracksAsync(tracks);
            _notifications.ShowInfo("Sonic Adventure", $"{start.Title} to {end.Title}, in {tracks.Count} tracks");
        }
        catch (PlexException ex)
        {
            Report(ex, "Sonic Adventure didn't start");
        }
    }

    public async Task PlaySonicRadioAsync(MediaItem seed)
    {
        seed = Real(seed);
        if (_session.Client is not { } client) return;
        // Plex's sonic radio is a Plex Pass feature; without it, Meloday's own radio plays.
        if (!_pass.IsAvailable)
        {
            await PlayLocalRadioAsync(seed);
            return;
        }
        try
        {
            var similar = new List<Track>();
            switch (seed)
            {
                case Track t:
                    similar.Add(t);
                    similar.AddRange(await client.GetSonicallySimilarAsync<Track>(t.RatingKey, 50, 0.3));
                    break;
                default:
                    // For albums/artists, seed from their tracks' neighbours.
                    var seeds = (await ResolveTracksAsync(seed)).OrderBy(_ => Random.Shared.Next()).Take(3).ToList();
                    foreach (var s in seeds)
                        similar.AddRange(await client.GetSonicallySimilarAsync<Track>(s.RatingKey, 25, 0.3));
                    similar = seeds.Concat(similar.OrderBy(_ => Random.Shared.Next())).ToList();
                    break;
            }
            // Like every station and mix: nothing rated one star or less (the seed itself was chosen).
            var disliked = await _dislikes.GetAsync();
            var distinct = similar.DistinctBy(t => t.RatingKey).Where((t, i) => i == 0 && seed is Track || disliked.Eligible(t)).ToList();
            if (distinct.Count <= 1)
            {
                // Plex hasn't analysed this yet: Meloday's own radio plays instead.
                await PlayLocalRadioAsync(seed);
                return;
            }
            // From the song already playing: it plays on and the radio follows it.
            if (seed is Track playingSeed && _player.CurrentTrack?.RatingKey == playingSeed.RatingKey)
                _player.ContinueWith(distinct.Where(t => t.RatingKey != playingSeed.RatingKey).ToList());
            else
                await _player.PlayTracksAsync(distinct);
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't start sonic radio");
        }
    }

    /// <summary>
    /// Meloday's own radio from a song, album or artist (see <see cref="LocalRadio"/>): the song first
    /// when it's a song, then music near its artist, topped up as it plays.
    /// </summary>
    public async Task PlayLocalRadioAsync(MediaItem seed)
    {
        seed = Real(seed);
        try
        {
            // From the song already playing: it plays on, and the radio follows it.
            var playing = seed is Track t && _player.CurrentTrack?.RatingKey == t.RatingKey;
            var radio = await _localRadio.ForSeedAsync(seed, leadWithSeed: !playing);
            if (radio is null)
            {
                _notifications.ShowInfo("No radio for this", "A radio starts from a song, an album or an artist.");
                return;
            }
            var first = await radio.NextAsync(LocalRadio.BatchSize);
            if (playing) first = first.Where(t => t.RatingKey != seed.RatingKey).ToList();
            if (first.Count == 0)
            {
                _notifications.ShowInfo("Nothing to play yet", $"There isn't enough music near {seed.Title} for a radio yet.");
                return;
            }
            if (playing) _player.ContinueWith(first, ct => radio.NextAsync(LocalRadio.BatchSize, ct));
            else await _player.PlayMixAsync(first, ct => radio.NextAsync(LocalRadio.BatchSize, ct));
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't start the radio");
        }
    }

    // ------------------------------------------------------------------ playlists

    private const int PlaylistChunk = 200;

    /// <summary>A playlist was created, changed or deleted (lists showing playlists should reload).</summary>
    public event EventHandler? PlaylistsChanged;

    /// <summary>Opens Settings at the music the listener has muted.</summary>
    public void OpenMutedMusic() => _navigation.Navigate(typeof(Views.SettingsPage), "muting");

    /// <summary>Opens the Lyric Studio for a song, to time its lyrics by hand.</summary>
    public void OpenLyricStudio(Track track) => _navigation.Navigate(typeof(Views.LyricStudioPage), track);

    /// <summary>An item's artwork or details were changed on the server (from here): the pages showing it fetch it again.</summary>
    public event EventHandler<MediaItem>? ItemChanged;

    /// <summary>Tells the pages showing the item that it changed on the server.</summary>
    public Task NotifyItemChangedAsync(MediaItem item)
    {
        if (item is Playlist) PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        ItemChanged?.Invoke(this, item);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Changes an album's, artist's or collection's name and description on the server (a playlist has
    /// <see cref="EditPlaylistAsync"/>). The server needs the library section the item is in, which the item itself doesn't carry.
    /// </summary>
    public async Task<bool> EditDetailsAsync(MediaItem item, string title, string? summary)
    {
        if (_session.Client is not { } client) return false;
        var type = item switch { Album => PlexTypes.Album, Artist => PlexTypes.Artist, MusicCollection => PlexTypes.Collection, _ => 0 };
        if (type == 0) return false;
        try
        {
            var section = (await client.GetMetadataAsync(item.RatingKey))?.LibrarySectionID;
            if (string.IsNullOrEmpty(section)) throw new PlexException("Plex didn't say which library this is in.");
            await client.EditDetailsAsync(section, type, item.RatingKey, title, summary);
            await NotifyItemChangedAsync(item);
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't save the details");
            return false;
        }
    }

    /// <summary>What an item adds to a playlist: tracks, albums and artists directly; other items as their tracks.</summary>
    private async Task<IReadOnlyList<string>> PlaylistKeysAsync(MediaItem item) => item switch
    {
        Track or Album or Artist => [item.RatingKey],
        _ => (await ResolveTracksAsync(item)).Select(t => t.RatingKey).Take(1000).ToList(),
    };

    public async Task<bool> AddToPlaylistAsync(IReadOnlyList<MediaItem> items, Playlist playlist)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            var keys = new List<string>();
            foreach (var item in items) keys.AddRange(await PlaylistKeysAsync(item));
            foreach (var chunk in keys.Chunk(PlaylistChunk)) await client.AddToPlaylistAsync(playlist.RatingKey, chunk);
            _notifications.ShowSuccess("Added to playlist", items.Count == 1 ? $"{items[0].Title} → {playlist.Title}" : $"{items.Count} items → {playlist.Title}");
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't add to the playlist");
            return false;
        }
    }

    public async Task<Playlist?> CreatePlaylistAsync(string title, IReadOnlyList<MediaItem> items, string? description = null, string? coverPath = null)
    {
        if (_session.Client is not { } client) return null;
        try
        {
            var keys = new List<string>();
            foreach (var item in items) keys.AddRange(await PlaylistKeysAsync(item));
            if (keys.Count == 0) return null;
            // Long lists go in chunks, so the request address stays a sensible length.
            var created = await client.CreatePlaylistAsync(title, keys.Take(PlaylistChunk).ToList());
            if (created is not null)
                foreach (var chunk in keys.Skip(PlaylistChunk).Chunk(PlaylistChunk)) await client.AddToPlaylistAsync(created.RatingKey, chunk);
            if (created is not null) await DressPlaylistAsync(client, created.RatingKey, title, description, coverPath);
            _notifications.ShowSuccess("Playlist created", title);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            return created;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't create the playlist");
            return null;
        }
    }

    /// <summary>A new playlist's description and cover, where it has them (it exists either way if they can't be set).</summary>
    private async Task DressPlaylistAsync(PlexServerClient client, string key, string title, string? description, string? coverPath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(description)) await client.EditPlaylistAsync(key, title, description);
            if (coverPath is not null && File.Exists(coverPath)) await client.SetPlaylistCoverAsync(key, await File.ReadAllBytesAsync(coverPath));
        }
        catch (Exception ex) when (ex is PlexException or IOException)
        {
            _log.LogInformation("The new playlist's description or cover wasn't set: {Message}", ex.Message);
        }
    }

    /// <summary>Creates a smart playlist from rules (Plex fills it and keeps it up to date).</summary>
    public async Task<Playlist?> CreateSmartPlaylistAsync(string title, Core.Playlists.SmartFilter filter)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return null;
        try
        {
            var created = await client.CreateSmartPlaylistAsync(title, section.Key, filter);
            _notifications.ShowSuccess("Smart playlist created", title);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            return created;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't create the smart playlist");
            return null;
        }
    }

    /// <summary>Gives a smart playlist a new name and rules.</summary>
    public async Task<bool> UpdateSmartPlaylistAsync(Playlist playlist, string title, string sectionKey, Core.Playlists.SmartFilter filter)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            await client.UpdateSmartPlaylistAsync(playlist.RatingKey, sectionKey, filter);
            if (title != playlist.Title) await client.EditPlaylistAsync(playlist.RatingKey, title, playlist.Summary);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't save the smart playlist");
            return false;
        }
    }

    public async Task<bool> EditPlaylistAsync(Playlist playlist, string title, string? summary)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            await client.EditPlaylistAsync(playlist.RatingKey, title, summary);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't save the playlist");
            return false;
        }
    }

    /// <summary>Deletes a playlist (after the listener confirmed), unpins it, and leaves its page if open.</summary>
    public async Task<bool> DeletePlaylistAsync(Playlist playlist)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            await client.DeletePlaylistAsync(playlist.RatingKey);
            if (_session.Server?.MachineIdentifier is { } server && _pins.IsPinned(server, playlist.RatingKey)) _pins.Unpin(server, playlist.RatingKey);
            _notifications.ShowInfo("Playlist deleted", playlist.Title);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
            _navigation.LeaveIfShowing(typeof(PlaylistPage), playlist.RatingKey);
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't delete the playlist");
            return false;
        }
    }

    /// <summary>Saves a rating (0-10; -1 clears) to Plex. Returns whether it was saved.</summary>
    public async Task<bool> RateAsync(MediaItem item, double rating)
    {
        if (_session.Client is not { } client) return false;
        try
        {
            await client.RateAsync(item.RatingKey, rating);
            _dislikes.Invalidate();
            return true;
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't save rating");
            return false;
        }
    }

    public async Task<IReadOnlyList<Track>> ResolveTracksAsync(MediaItem item)
    {
        item = Real(item);
        if (_session.Client is not { } client) return [];
        try
        {
            switch (item)
            {
                // A pinned song is kept without its file's address: Plex is asked for the whole song.
                case Track { Key: null } kept: return await client.GetTrackAsync(kept.RatingKey) is { } full ? [full] : [];
                case Track t: return [t];
                case Station { StationKind: StationKind.Meloday }:
                    return (await _meloday.GetAsync())?.Tracks ?? [];
                case ArtistMix or TagMix or Station or BuiltMix:
                    return await _mixes.FirstTracksAsync(item, batches: 2) ?? [];
                case Album a: return await client.GetAlbumTracksAsync(a.RatingKey);
                case Artist a: return await client.GetArtistTracksAsync(a.RatingKey);
                case Playlist p:
                {
                    var all = new List<Track>();
                    const int page = 300;
                    for (var start = 0; start < 20000; start += page)
                    {
                        var result = await client.GetPlaylistItemsAsync(p.RatingKey, start, page);
                        all.AddRange(result.Items);
                        if (!result.HasMore || result.Items.Count == 0) break;
                    }
                    return all;
                }
                case MusicCollection c:
                {
                    var tracks = new List<Track>();
                    foreach (var child in await client.GetCollectionItemsAsync(c.RatingKey))
                    {
                        if (child is not MusicCollection) tracks.AddRange(await ResolveTracksAsync(child));
                    }
                    return tracks;
                }
                case TagItem tag when _session.Section is { } section:
                {
                    var result = await client.GetTracksAsync(section.Key, 0, 500, "random", default,
                        new KeyValuePair<string, string?>(PlexServerClient.TagField(tag.TagKind), tag.FilterValue));
                    return result.Items;
                }
            }
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't load tracks");
        }
        return [];
    }

    private void Report(PlexException ex, string title)
    {
        _log.LogWarning("{Title}: {Message}", title, ex.Message);
        _notifications.ShowError(title, ex is PlexUnreachableException
            ? "Your Plex server can't be reached right now."
            : ex.Message);
        if (ex is PlexUnauthorizedException) _ = _session.HandleUnauthorizedAsync();
    }
}
