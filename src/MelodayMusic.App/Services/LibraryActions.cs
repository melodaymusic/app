using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Pins;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class LibraryActions
{
    private readonly PlexSession _session;
    private readonly PlayerService _player;
    private readonly NavigationService _navigation;
    private readonly NotificationService _notifications;
    private readonly PinStore _pins;
    private readonly MixService _mixes;
    private readonly Dislikes _dislikes;
    private readonly MusicIntelligence _intelligence;
    private readonly MelodayService _meloday;
    private readonly LocalRadio _localRadio;
    private readonly Core.Home.RecentStore _recents;
    private readonly StaticCovers _covers;
    private readonly LiveRadio _live;
    private Station? _lastLive;
    private readonly ILogger _log;

    public LibraryActions(PlexSession session, PlayerService player, NavigationService navigation, NotificationService notifications,
        PinStore pins, MixService mixes, Dislikes dislikes, MusicIntelligence intelligence, MelodayService meloday, LocalRadio localRadio, Core.Home.RecentStore recents, StaticCovers covers, LiveRadio live, ILogger<LibraryActions> log)
    {
        _recents = recents;
        _covers = covers;
        _live = live;
        player.LiveRejoinRequested += (_, _) =>
        {
            if ((player.LiveStationSource ?? _lastLive) is { } station) _ = PlayMixAsync(station);
        };
        _localRadio = localRadio;
        _meloday = meloday;
        _intelligence = intelligence;
        _mixes = mixes;
        _dislikes = dislikes;
        _pins = pins;
        _session = session;
        _player = player;
        _navigation = navigation;
        _notifications = notifications;
        _log = log;
    }

    public static MediaItem Real(MediaItem item) => item is PinnedCover cover ? cover.Target : item;

    public void Open(MediaItem item)
    {
        item = Real(item);
        switch (item)
        {
            case Core.Discovery.CatalogueItem catalogue: _ = Windows.System.Launcher.LaunchUriAsync(new Uri(catalogue.Release.Url)); break;
            case Artist a: _navigation.Navigate(typeof(ArtistPage), a); break;
            case Album a: _navigation.Navigate(typeof(AlbumPage), a); break;
            case Playlist p when p.RatingKey == TrendingService.PlaylistKey: _navigation.Navigate(typeof(Views.TrendingPage)); break;
            case Playlist p when EssentialsService.IsEssentials(p): _navigation.Navigate(typeof(Views.EssentialsPage), p); break;
            case Playlist p when MadePlaylistService.IsMade(p): _navigation.Navigate(typeof(Views.MadePlaylistPage), p); break;
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

    public static bool CanPin(MediaItem item) => item.RatingKey != TrendingService.PlaylistKey && !EssentialsService.IsEssentials(item)
        && !MadePlaylistService.IsMade(item) && PinnedItem.KindOf(item) is not null;

    public bool IsPinned(MediaItem item) =>
        _session.Server?.MachineIdentifier is { } server && _pins.IsPinned(server, item.RatingKey);

    public (int Index, int Count) PinPosition(MediaItem item) =>
        _session.Server?.MachineIdentifier is { } server ? _pins.Position(server, item.RatingKey) : (-1, 0);

    public bool MovePin(MediaItem item, int offset) =>
        _session.Server?.MachineIdentifier is { } server && _pins.Move(server, item.RatingKey, offset);

    public void ReorderPins(IEnumerable<string> ratingKeys)
    {
        if (_session.Server?.MachineIdentifier is { } server) _pins.Reorder(server, ratingKeys);
    }

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
        if (StaticCovers.Applies(item)) _ = CoverPinAsync(server, item);
        return true;
    }

    private async Task CoverPinAsync(string server, MediaItem item)
    {
        if (await _covers.GetAsync(item).ConfigureAwait(false) is { } cover) _pins.SetThumb(server, item.RatingKey, cover);
    }

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

    public async Task PlayAsync(MediaItem item, bool shuffle = false)
    {
        item = Real(item);
        if (item is Station { IsBuilder: true }) return;
        RecordPlayed(item);
        _player.From(item);
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

    public async Task PlayMixAsync(MediaItem mix)
    {
        mix = Real(mix);
        _player.From(mix);
        try
        {
            if (mix is Station { IsLive: true } station)
            {
                if (await _live.TuneInAsync(station) is not { } tuning)
                {
                    _notifications.ShowInfo("Station unavailable", $"{station.Title} couldn't be filled. Try again when your library is connected.");
                    return;
                }
                _lastLive = station;
                await _player.PlayLiveAsync(station.Title, tuning);
                _notifications.ShowInfo("Live", $"Tuned in to {station.Title}");
                return;
            }
            if (mix is BuiltMix built && built.RatingKey != "mix:discovery")
            {
                var radio = _localRadio.ForBuilt(built);
                var opening = await radio.NextAsync(LocalRadio.BatchSize);
                if (opening.Count == 0)
                {
                    _notifications.ShowInfo("Nothing to play yet", "The chosen artists or albums have no available tracks.");
                    return;
                }
                await _player.PlayMixAsync(opening, ct => radio.NextAsync(LocalRadio.BatchSize, ct));
                _player.TrackRadio(radio.Played);
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

    public async Task PlaySonicAdventureAsync(Track start, Track end)
    {
        if (_session.Client is not { } client || _session.Section is not { } section || !_intelligence.IsAvailable) return;
        try
        {
            var disliked = await _dislikes.GetAsync();
            var path = await _intelligence.AdventureAsync(start, end, allowed: disliked.Eligible);
            var songs = new SongSet([start, end]);
            var tracks = new List<Track> { start };
            tracks.AddRange(path.Where(t => disliked.Eligible(t) && songs.Add(t)));
            tracks.Add(end);
            if (tracks.Count <= 2)
            {
                _notifications.ShowInfo("No way between them", _intelligence.IsMeloday ? "The Meloday Engine hasn't analysed enough songs between these two yet. It will analyse them next, so try again soon." : "Plex could not find a sonic path between these tracks.");
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
        if (_intelligence.IsMeloday)
        {
            await PlayLocalRadioAsync(seed);
            return;
        }
        if (!_intelligence.IsAvailable)
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
                    similar.AddRange(await _intelligence.SimilarAsync<Track>(t.RatingKey, 50, 0.3));
                    break;
                default:
                    var seeds = (await ResolveTracksAsync(seed)).OrderBy(_ => Random.Shared.Next()).Take(3).ToList();
                    foreach (var s in seeds)
                        similar.AddRange(await _intelligence.SimilarAsync<Track>(s.RatingKey, 25, 0.3));
                    similar = seeds.Concat(similar.OrderBy(_ => Random.Shared.Next())).ToList();
                    break;
            }
            var disliked = await _dislikes.GetAsync();
            var songs = new SongSet();
            var distinct = similar.Where((t, i) => (i == 0 && seed is Track || disliked.Eligible(t)) && songs.Add(t)).ToList();
            if (distinct.Count <= 1)
            {
                _notifications.ShowInfo(_intelligence.Name, "Sound analysis is still building for this selection. Playing library-based radio for now.");
                await PlayLocalRadioAsync(seed);
                return;
            }
            async Task<IReadOnlyList<Track>> More(CancellationToken ct)
            {
                var last = _player.Queue.Items.LastOrDefault()?.Track ?? distinct[^1];
                var next = await _intelligence.SimilarAsync<Track>(last.RatingKey, 30, 0.3);
                var allowed = await _dislikes.GetAsync();
                return next.Where(allowed.Eligible).ToList();
            }
            if (seed is Track playingSeed && _player.CurrentTrack?.RatingKey == playingSeed.RatingKey)
                _player.ContinueWith(distinct.Where(t => t.RatingKey != playingSeed.RatingKey).ToList(), More);
            else
                await _player.PlayMixAsync(distinct, More);
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't start sonic radio");
        }
    }

    public async Task PlayLocalRadioAsync(MediaItem seed)
    {
        seed = Real(seed);
        try
        {
            var playing = seed is Track t && _player.CurrentTrack?.RatingKey == t.RatingKey;
            var radio = await _localRadio.ForSeedAsync(seed, leadWithSeed: !playing);
            if (radio is null)
            {
                _notifications.ShowInfo("No radio for this", "A radio starts from a song, an album or an artist.");
                return;
            }
            var first = await radio.NextAsync(LocalRadio.BatchSize);
            if (playing && seed is Track playingSong)
            {
                var now = new SongSet([playingSong]);
                first = first.Where(t => !now.Contains(t)).ToList();
            }
            if (first.Count == 0)
            {
                _notifications.ShowInfo("Nothing to play yet", $"There isn't enough music near {seed.Title} for a radio yet.");
                return;
            }
            if (playing) _player.ContinueWith(first, ct => radio.NextAsync(LocalRadio.BatchSize, ct));
            else await _player.PlayMixAsync(first, ct => radio.NextAsync(LocalRadio.BatchSize, ct));
            _player.TrackRadio(radio.Played);
        }
        catch (PlexException ex)
        {
            Report(ex, "Couldn't start the radio");
        }
    }

    private const int PlaylistChunk = 200;

    public event EventHandler? PlaylistsChanged;

    public void OpenMutedMusic() => _navigation.Navigate(typeof(Views.SettingsPage), "muting");

    public void OpenLyricStudio(Track track) => _navigation.Navigate(typeof(Views.LyricStudioPage), track);

    public event EventHandler<MediaItem>? ItemChanged;

    public Task NotifyItemChangedAsync(MediaItem item)
    {
        if (item is Playlist) PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        ItemChanged?.Invoke(this, item);
        return Task.CompletedTask;
    }

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

    private async Task DressPlaylistAsync(PlexServerClient client, string key, string title, string? description, string? coverPath)
    {
        try
        {
            coverPath ??= await PlaylistCover.RenderAsync(client.Server.MachineIdentifier + ":" + key, title, _log);
            if (!string.IsNullOrWhiteSpace(description)) await client.EditPlaylistAsync(key, title, description);
            if (coverPath is not null && File.Exists(coverPath)) await client.SetPlaylistCoverAsync(key, await File.ReadAllBytesAsync(coverPath));
        }
        catch (Exception ex) when (ex is PlexException or IOException)
        {
            _log.LogInformation("The new playlist's description or cover wasn't set: {Message}", ex.Message);
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
                case Track { Key: null } kept: return await client.GetTrackAsync(kept.RatingKey) is { } full ? [full] : [];
                case Track t: return [t];
                case Station { StationKind: StationKind.Meloday }:
                    return (await _meloday.GetAsync())?.Tracks ?? [];
                case ArtistMix or TagMix or Station or BuiltMix:
                    return await _mixes.FirstTracksAsync(item, batches: 2) ?? [];
                case Album a: return await client.GetAlbumTracksAsync(a.RatingKey);
                case Artist a: return await client.GetArtistTracksAsync(a.RatingKey);
                case Playlist p when p.RatingKey == TrendingService.PlaylistKey:
                    return (await App.Services.GetRequiredService<TrendingService>().GetAsync())?.InLibrary ?? [];
                case Playlist p when EssentialsService.IsEssentials(p):
                    return await App.Services.GetRequiredService<EssentialsService>().TracksAsync(p);
                case Playlist p when MadePlaylistService.IsMade(p):
                    return await App.Services.GetRequiredService<MadePlaylistService>().TracksAsync(p);
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
