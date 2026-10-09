using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using HumeMusic.Helpers;
using HumeMusic.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.ViewModels;

/// <summary>A labelled group of albums on an artist page (Albums, Singles &amp; EPs, ...).</summary>
public sealed class AlbumGroup
{
    public required string Title { get; init; }
    public required IReadOnlyList<Album> Albums { get; init; }
}

/// <summary>A labelled fact in the artist About section ("From: Sweden").</summary>
public sealed record ArtistFact(string Icon, string Label, string Value);

/// <summary>A numbered track in an artist's Top Songs.</summary>
public sealed class RankedTrack
{
    public Track Track { get; init; } = null!;
    public int Rank { get; init; }
}

/// <summary>Artist page: hero, top songs, latest release, discography, similar artists and About.</summary>
public sealed partial class ArtistViewModel : ViewModelBase
{
    private readonly PlaylistArtistIndex _playlists;
    private readonly ArtistRanks _ranks;

    public ArtistViewModel(PlexSession session, PlaylistArtistIndex playlists, ArtistRanks ranks, ILogger<ArtistViewModel> log) : base(session, log)
    {
        _playlists = playlists;
        _ranks = ranks;
    }

    [ObservableProperty] public partial Artist? Artist { get; private set; }
    /// <summary>The artist's full metadata has loaded (the seed from a list may lack its backdrop).</summary>
    public bool HasFullArtist { get; private set; }
    public ObservableCollection<RankedTrack> TopSongs { get; } = [];
    public ObservableCollection<AlbumGroup> AlbumGroups { get; } = [];
    public ObservableCollection<Artist> SimilarArtists { get; } = [];
    /// <summary>The listener's playlists this artist is on, most of their songs first.</summary>
    public ObservableCollection<Playlist> FeaturedPlaylists { get; } = [];
    public ObservableCollection<ArtistFact> Facts { get; } = [];
    /// <summary>"#3 in your library", when the listener's setting shows this artist's place.</summary>
    [ObservableProperty] public partial string RankText { get; private set; } = string.Empty;
    /// <summary>The About section has something to say (a biography or a rank).</summary>
    [ObservableProperty] public partial bool HasAbout { get; private set; }
    [ObservableProperty] public partial Album? LatestRelease { get; private set; }
    [ObservableProperty] public partial string LatestReleaseKind { get; private set; } = "Album";
    [ObservableProperty] public partial string LatestReleaseMeta { get; private set; } = string.Empty;


    public async Task LoadAsync(Artist seed)
    {
        Artist = seed;
        await RunAsync(async () =>
        {
            var client = Client;
            var artistTask = client.GetArtistAsync(seed.RatingKey);
            var albumsTask = client.GetArtistAlbumsAsync(seed.RatingKey);
            var hubsTask = Optional(client.GetItemHubsAsync(seed.RatingKey, 25));
            var relatedTask = Optional(client.GetRelatedHubsAsync(seed.RatingKey, 16));
            await Task.WhenAll(artistTask, albumsTask, hubsTask, relatedTask);

            var artist = artistTask.Result ?? seed;
            HasFullArtist = true;
            Artist = artist;
            var albums = albumsTask.Result;
            var hubs = hubsTask.Result ?? [];
            var related = relatedTask.Result ?? [];

            // Top songs: Plex's popularity ranking, then the listener's own most played.
            IReadOnlyList<Track> top = HubTracks(hubs, "artist.mostpopulartracks");
            if (top.Count == 0) top = HubTracks(hubs, "artist.mostplayedtracks");
            if (top.Count == 0) top = await Optional(client.GetPopularTracksAsync(SectionKey, seed.RatingKey, 25)) ?? [];
            if (top.Count == 0)
            {
                var own = await client.GetTracksAsync(SectionKey, 0, 25, "viewCount:desc", default,
                    new KeyValuePair<string, string?>("artist.id", seed.RatingKey));
                top = own.Items;
            }
            // The artist's top 10.
            TopSongs.Clear();
            foreach (var (t, i) in top.DistinctBy(t => t.Title.ToLowerInvariant()).Take(10).Select((t, i) => (t, i)))
                TopSongs.Add(new RankedTrack { Track = t, Rank = i + 1 });

            // The full catalogue: Plex's artist "children" are the main albums only; singles, EPs, live
            // albums, compilations and so on come as the artist's release hubs. Every release is then
            // sorted by its own type, and the releases of others the artist appears on come last.
            var groups = await ReleaseGroupsAsync(client, albums, related);
            var releases = groups.SelectMany(g => g.Albums).ToList();
            var appearances = await AppearsOnAsync(client, artist, releases);
            if (appearances.Count > 0) groups = [.. groups, new AlbumGroup { Title = "Appears On", Albums = appearances }];
            AlbumGroups.Clear();
            foreach (var group in groups) AlbumGroups.Add(group);

            // Latest release: newest by release date or year, then by when it was added.
            var latest = releases.OrderByDescending(ReleaseSortKey).ThenByDescending(a => a.AddedAt).FirstOrDefault();
            if (latest is not null)
            {
                var full = await client.GetAlbumAsync(latest.RatingKey) ?? latest;
                LatestRelease = full;
                LatestReleaseKind = Releases.Label(Releases.Classify(full));
                var bits = new List<string>();
                if (full.Year is > 0) bits.Add(full.Year.Value.ToString());
                if (full.TrackCount is > 0) bits.Add(Format.Count(full.TrackCount, "song", "songs"));
                LatestReleaseMeta = string.Join(" · ", bits);
            }
            else
            {
                LatestRelease = null;
            }

            // Similar artists: library matches first, then sonic neighbours.
            SimilarArtists.Clear();
            foreach (var a in HubArtists(related, "artist.similar").Concat(HubArtists(related, "external.artist.similar.sonically"))
                         .Where(a => a.RatingKey != seed.RatingKey).DistinctBy(a => a.RatingKey).Take(16))
                SimilarArtists.Add(a);
            if (SimilarArtists.Count == 0)
            {
                foreach (var a in (await Optional(client.GetSonicallySimilarAsync<Artist>(seed.RatingKey, 16, 0.35)) ?? [])
                             .Where(a => a.RatingKey != seed.RatingKey).Take(16))
                    SimilarArtists.Add(a);
            }

            BuildFacts(artist, releases);
            UpdateHasAbout();
        });
        _ = LoadFeaturedPlaylistsAsync(seed);
        _ = LoadRankAsync(seed);
    }

    private async Task LoadRankAsync(Artist seed)
    {
        try
        {
            var rank = await _ranks.GetAsync(seed.RatingKey);
            if (Artist?.RatingKey != seed.RatingKey) return;
            RankText = rank is { } r ? LibraryRank.Describe(r) : string.Empty;
            UpdateHasAbout();
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Artist rank unavailable: {Message}", ex.Message);
        }
    }

    private void UpdateHasAbout() =>
        HasAbout = !string.IsNullOrWhiteSpace(Artist?.Summary) || RankText.Length > 0;

    /// <summary>
    /// Releases by others the artist is credited on ("feat." and the like): Plex's search finds their
    /// songs through the track artist credits; each album appears once, newest first.
    /// </summary>
    private async Task<IReadOnlyList<Album>> AppearsOnAsync(PlexServerClient client, Artist artist, IReadOnlyList<Album> own)
    {
        SearchResults? found;
        try
        {
            found = await client.SearchAsync(artist.Title, SectionKey, 200);
        }
        catch (PlexException ex)
        {
            Log.LogDebug("Appearances unavailable: {Message}", ex.Message);
            return [];
        }
        var mine = own.Select(a => a.RatingKey).ToHashSet(StringComparer.Ordinal);
        return found.Tracks
            .Where(t => t.AlbumRatingKey is not null && !mine.Contains(t.AlbumRatingKey)
                        && t.ArtistRatingKey != artist.RatingKey
                        && ArtistCredit.Mentions(t.DisplayArtist, artist.Title))
            .GroupBy(t => t.AlbumRatingKey!)
            .Select(g => g.First())
            .Select(t => new Album
            {
                RatingKey = t.AlbumRatingKey!,
                Title = t.AlbumTitle ?? t.Title,
                Thumb = t.AlbumThumb,
                ArtistTitle = t.ArtistTitle,
                ArtistRatingKey = t.ArtistRatingKey,
                Year = t.Year,
            })
            .OrderByDescending(ReleaseSortKey)
            .ToList();
    }

    /// <summary>Fetches the artist again (after their photo, backdrop, logo or details changed), without reloading the page.</summary>
    public async Task RefreshArtistAsync()
    {
        if (Artist is not { } current || Session.Client is not { } client) return;
        try
        {
            if (await client.GetArtistAsync(current.RatingKey) is { } fresh) Artist = fresh;
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Artist not refreshed: {Message}", ex.Message);
        }
    }

    /// <summary>The playlists row comes from a background index, so it never holds up the page.</summary>
    private async Task LoadFeaturedPlaylistsAsync(Artist seed)
    {
        try
        {
            var featured = await _playlists.GetFeaturingAsync(Artist ?? seed);
            if (Artist?.RatingKey != seed.RatingKey) return;
            FeaturedPlaylists.Clear();
            foreach (var f in featured.Take(24)) FeaturedPlaylists.Add(f.Playlist);
        }
        catch (Exception ex) when (ex is PlexException or OperationCanceledException or IOException)
        {
            Log.LogInformation("Featured playlists unavailable: {Message}", ex.Message);
        }
    }

    /// <summary>Facts Plex actually knows; anything missing is left out rather than invented.</summary>
    private void BuildFacts(Artist artist, IReadOnlyList<Album> albums)
    {
        Facts.Clear();
        void Add(string icon, string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) Facts.Add(new ArtistFact(icon, label, value));
        }
        Add("Globe", "From", string.Join(", ", artist.Countries.Take(2)));
        Add("Tags", "Genres", string.Join(", ", artist.Genres.Concat(artist.Styles).Distinct(StringComparer.OrdinalIgnoreCase).Take(3)));
        Add("Smile", "Mood", string.Join(", ", artist.Moods.Take(3)));
        var years = albums.Select(a => a.Year ?? 0).Where(y => y > 0).ToList();
        if (years.Count > 0) Add("Calendar", "Releases", years.Min() == years.Max() ? $"{years.Min()}" : $"{years.Min()} – {years.Max()}");
        var label = albums.Select(a => a.Studio).Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x!, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key;
        Add("Disc3", "Label", label);
        Add("Library", "In your library", albums.Count > 0 ? Format.Count(albums.Count, "release", "releases") : null);
        if (artist.ViewCount > 0) Add("History", "Your plays", Format.Plays(artist.ViewCount));
        Add("Clock", "Last listened", artist.LastViewedAt is { } last ? Format.Ago(last) : null);
    }

    /// <summary>
    /// Every release: the artist's albums and each of their release hubs (Singles &amp; EPs, Live,
    /// Compilations, Soundtracks, Remixes, Demos) loaded in full, then read in full from Plex so each
    /// can be sorted by its own release type (Album, EP, Single, Remix...), newest first in each row.
    /// </summary>
    private async Task<IReadOnlyList<AlbumGroup>> ReleaseGroupsAsync(PlexServerClient client, IReadOnlyList<Album> albums, IReadOnlyList<Hub> related)
    {
        var hubs = related.Where(h => h.Identifier.StartsWith("artist.albums.", StringComparison.Ordinal) && h.Items.Count > 0).ToList();
        var loads = hubs.Select(async hub =>
        {
            // A hub carries only its first few items; fetch the rest when there are more.
            if (hub.Key is { } key && (hub.More || hub.Size > hub.Items.Count))
            {
                var all = await Optional(client.GetHubItemsAsync(key, 0, 300));
                if (all is { Count: > 0 }) return all.OfType<Album>().ToList();
            }
            return hub.Items.OfType<Album>().ToList();
        }).ToList();
        var results = await Task.WhenAll(loads);

        // Each release once, remembering the hub it came from (a fallback for its type).
        var found = new Dictionary<string, (Album Album, string? Hub)>(StringComparer.Ordinal);
        foreach (var a in albums) found.TryAdd(a.RatingKey, (a, null));
        for (var i = 0; i < hubs.Count; i++)
            foreach (var a in results[i]) found.TryAdd(a.RatingKey, (a, hubs[i].Identifier));

        // Lists carry no release types: read the releases themselves, a hundred at a time.
        var full = new Dictionary<string, Album>(StringComparer.Ordinal);
        foreach (var chunk in found.Keys.Chunk(100))
            foreach (var a in (await Optional(client.GetItemsAsync(chunk)) ?? []).OfType<Album>()) full[a.RatingKey] = a;

        var sorted = found.Values
            .Select(f => (Album: full.GetValueOrDefault(f.Album.RatingKey) ?? f.Album, f.Hub))
            .GroupBy(f => Releases.Classify(f.Album, f.Hub))
            .ToDictionary(g => g.Key, g => g.Select(f => f.Album).OrderByDescending(ReleaseSortKey).ToList());
        return Releases.Order
            .Where(sorted.ContainsKey)
            .Select(kind => new AlbumGroup { Title = Releases.GroupTitle(kind), Albums = sorted[kind] })
            .ToList();
    }

    private static DateTimeOffset ReleaseSortKey(Album a) =>
        a.ReleaseDate ?? (a.Year is > 0 ? new DateTimeOffset(a.Year.Value, 1, 1, 0, 0, 0, TimeSpan.Zero) : DateTimeOffset.MinValue);

    private static IReadOnlyList<Track> HubTracks(IReadOnlyList<Hub> hubs, string id) =>
        hubs.FirstOrDefault(h => h.Identifier == id)?.Items.OfType<Track>().ToList() ?? [];

    private static IEnumerable<Artist> HubArtists(IReadOnlyList<Hub> hubs, string id) =>
        hubs.FirstOrDefault(h => h.Identifier == id)?.Items.OfType<Artist>() ?? [];

    private async Task<IReadOnlyList<T>?> Optional<T>(Task<IReadOnlyList<T>> task)
    {
        try { return await task; }
        catch (PlexException ex)
        {
            Log.LogDebug("Optional artist data unavailable: {Message}", ex.Message);
            return null;
        }
    }
}

/// <summary>A row in an album's track list, possibly preceded by a disc header.</summary>
public sealed class AlbumTrackRow
{
    public required Track Track { get; init; }
    public string? DiscHeader { get; init; }
    public bool ShowTrackArtist { get; init; }
    public bool HasDiscHeader => DiscHeader is not null;
}

public sealed partial class AlbumViewModel : ViewModelBase
{
    public AlbumViewModel(PlexSession session, ILogger<AlbumViewModel> log) : base(session, log) { }

    [ObservableProperty] public partial Album? Album { get; private set; }
    public ObservableCollection<AlbumTrackRow> Tracks { get; } = [];
    public ObservableCollection<Album> MoreByArtist { get; } = [];
    public ObservableCollection<Album> SimilarAlbums { get; } = [];
    [ObservableProperty] public partial string MetaText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string FormatText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string TagsText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string ReleaseText { get; private set; } = string.Empty;
    [ObservableProperty] public partial string FooterText { get; private set; } = string.Empty;

    public IReadOnlyList<Track> TrackList => Tracks.Select(r => r.Track).ToList();

    /// <summary>Fetches the album again (after its cover, backdrop, logo or details changed), without reloading the page.</summary>
    public async Task RefreshAlbumAsync()
    {
        if (Album is not { } current || Session.Client is not { } client) return;
        try
        {
            if (await client.GetAlbumAsync(current.RatingKey) is { } fresh) Album = fresh;
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Album not refreshed: {Message}", ex.Message);
        }
    }

    public async Task LoadAsync(Album seed)
    {
        Album = seed;
        await RunAsync(async () =>
        {
            var client = Client;
            var albumTask = client.GetAlbumAsync(seed.RatingKey);
            var tracksTask = client.GetAlbumTracksAsync(seed.RatingKey);
            await Task.WhenAll(albumTask, tracksTask);
            var album = albumTask.Result ?? seed;
            Album = album;
            var tracks = tracksTask.Result;

            var discs = tracks.Select(t => t.DiscNumber ?? 1).Distinct().Count();
            var albumArtist = album.ArtistTitle;
            Tracks.Clear();
            int? lastDisc = null;
            foreach (var t in tracks.OrderBy(t => t.DiscNumber ?? 1).ThenBy(t => t.TrackNumber ?? int.MaxValue))
            {
                var disc = t.DiscNumber ?? 1;
                Tracks.Add(new AlbumTrackRow
                {
                    Track = t,
                    DiscHeader = discs > 1 && disc != lastDisc ? $"Disc {disc}" : null,
                    ShowTrackArtist = !string.IsNullOrEmpty(t.TrackArtist) && !string.Equals(t.TrackArtist, albumArtist, StringComparison.OrdinalIgnoreCase),
                });
                lastDisc = disc;
            }

            var total = TimeSpan.FromMilliseconds(tracks.Sum(t => t.DurationMs));
            // Header: genre and year only. Everything else lives in the footer under the track list.
            var meta = new List<string>();
            if (album.Genres.Count > 0) meta.Add(album.Genres[0]);
            if (album.Year is > 0) meta.Add(album.Year.Value.ToString());
            MetaText = string.Join(" · ", meta);

            ReleaseText = album.ReleaseDate is { } released ? $"Released {released:d MMMM yyyy}" : string.Empty;
            var footer = $"{Format.Count(tracks.Count, "track", "tracks")}, {Format.LongDuration(total)}";
            if (album.ViewCount > 0) footer += $" · {Format.Plays(album.ViewCount)}";
            FooterText = footer;
            var fmt = tracks.FirstOrDefault()?.Media?.Describe() ?? string.Empty;
            FormatText = string.IsNullOrEmpty(album.Studio) ? fmt : string.IsNullOrEmpty(fmt) ? album.Studio! : $"{album.Studio} · {fmt}";
            TagsText = string.Join("  ·  ", album.Genres.Concat(album.Styles).Concat(album.Moods).Distinct().Take(8));
            IsEmpty = Tracks.Count == 0;
        });

        // Secondary rows load after the main content so the page appears quickly.
        _ = LoadRelatedAsync(seed);
    }

    private async Task LoadRelatedAsync(Album seed)
    {
        try
        {
            var client = Client;
            var artistKey = Album?.ArtistRatingKey ?? seed.ArtistRatingKey;
            var moreTask = artistKey is null ? Task.FromResult<IReadOnlyList<Album>>([]) : client.GetArtistAlbumsAsync(artistKey);
            var similarTask = client.GetSonicallySimilarAsync<Album>(seed.RatingKey, 16, 0.3);
            try { await Task.WhenAll(moreTask, similarTask); } catch (PlexException) { }

            MoreByArtist.Clear();
            if (moreTask.IsCompletedSuccessfully)
                foreach (var a in moreTask.Result.Where(a => a.RatingKey != seed.RatingKey).OrderByDescending(a => a.Year).Take(16)) MoreByArtist.Add(a);
            SimilarAlbums.Clear();
            if (similarTask.IsCompletedSuccessfully)
                foreach (var a in similarTask.Result.Where(a => a.RatingKey != seed.RatingKey).Take(16)) SimilarAlbums.Add(a);
        }
        catch (PlexException ex)
        {
            Log.LogDebug("Related albums unavailable: {Message}", ex.Message);
        }
    }
}

public sealed partial class PlaylistViewModel : ViewModelBase
{
    private List<Track>? _all;
    private int _viewVersion;

    public PlaylistViewModel(PlexSession session, ILogger<PlaylistViewModel> log) : base(session, log)
    {
        SortOptions = Core.Playlists.PlaylistView.Sorts.Select(s => new SortOption(s.Label, s.Key)).ToList();
        SelectedSort = SortOptions[0];
    }

    [ObservableProperty] public partial Playlist? Playlist { get; private set; }
    [ObservableProperty] public partial IncrementalCollection<Track>? Tracks { get; private set; }
    [ObservableProperty] public partial string MetaText { get; private set; } = string.Empty;

    /// <summary>The playlist's backdrop on the Plex server (set from Edit Artwork, here or in any Plex app), or null.</summary>
    [ObservableProperty] public partial string? Backdrop { get; private set; }

    /// <summary>The orders the songs can be viewed in (the playlist's own first); a view only, the playlist itself isn't changed.</summary>
    public IReadOnlyList<SortOption> SortOptions { get; }

    [ObservableProperty] public partial SortOption SelectedSort { get; set; }

    /// <summary>What was typed in the page's own search (empty: all the songs).</summary>
    [ObservableProperty] public partial string SearchText { get; set; } = string.Empty;

    /// <summary>The songs the page shows: the playlist as it loads, or, sorted or searched, all of them in that order.</summary>
    [ObservableProperty] public partial System.Collections.IList? Shown { get; private set; }

    /// <summary>The search matched nothing in a playlist that has songs.</summary>
    [ObservableProperty] public partial bool NoMatches { get; private set; }

    /// <summary>Shown as the playlist keeps it: its own order and no search.</summary>
    private bool IsPlain => SelectedSort.Value == Core.Playlists.PlaylistView.Order && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>Tracks can be dragged into a new order (not in smart playlists, which Plex fills by rules, nor in a sorted or searched view).</summary>
    public bool CanReorder => Playlist is { IsSmart: false } && IsPlain;

    /// <summary>Removing songs, for the track list: none at all for a smart playlist (Plex chooses its songs).</summary>
    public System.Windows.Input.ICommand? RemoveCommand => Playlist is { IsSmart: false } ? RemoveTrackCommand : null;

    partial void OnPlaylistChanged(Playlist? value)
    {
        OnPropertyChanged(nameof(CanReorder));
        OnPropertyChanged(nameof(RemoveCommand));
        RemoveTrackCommand.NotifyCanExecuteChanged();
        Backdrop = value?.Art;
    }

    partial void OnSelectedSortChanged(SortOption value) => ViewChanged();

    partial void OnSearchTextChanged(string value) => ViewChanged();

    partial void OnTracksChanged(IncrementalCollection<Track>? value)
    {
        _all = null;
        ViewChanged();
    }

    private void ViewChanged()
    {
        OnPropertyChanged(nameof(CanReorder));
        _ = ApplyViewAsync();
    }

    /// <summary>
    /// Shows the songs in the chosen order and narrowed by the search. Sorting and searching need every song, so the rest
    /// of the playlist is read the first time one is asked for (a playlist is small beside a library); in the playlist's
    /// own order and with no search, the page shows the playlist as it loads.
    /// </summary>
    private async Task ApplyViewAsync()
    {
        var version = ++_viewVersion;
        var tracks = Tracks;
        if (IsPlain || tracks is null || Playlist is null)
        {
            Shown = tracks;
            NoMatches = false;
            return;
        }
        List<Track> all;
        try
        {
            all = await AllTracksAsync(Playlist.RatingKey);
        }
        catch (PlexException ex)
        {
            Log.LogInformation("The whole playlist couldn't be read to sort or search it: {Message}", ex.Message);
            return;
        }
        if (version != _viewVersion || !ReferenceEquals(tracks, Tracks)) return;
        var view = Core.Playlists.PlaylistView.Sort(all.Where(t => Core.Playlists.PlaylistView.Matches(t, SearchText)), SelectedSort.Value).ToList();
        Shown = new System.Collections.ObjectModel.ObservableCollection<Track>(view);
        NoMatches = view.Count == 0 && all.Count > 0;
    }

    private async Task<List<Track>> AllTracksAsync(string playlistKey)
    {
        if (_all is { } known) return known;
        var list = new List<Track>();
        PagedResult<Track> page;
        do
        {
            page = await Client.GetPlaylistItemsAsync(playlistKey, list.Count, 500);
            list.AddRange(page.Items);
        }
        while (page.Items.Count > 0 && list.Count < (page.TotalSize ?? int.MaxValue));
        return _all = list;
    }

    /// <summary>Removes a track from the playlist (normal playlists only) and from Plex.</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand(CanExecute = nameof(CanRemoveTrack))]
    private async Task RemoveTrackAsync(Track? track)
    {
        if (track is null || Playlist is not { } playlist || Shown is not { } shown || track.PlaylistItemId is not { } id) return;
        var index = shown.IndexOf(track);
        if (index < 0) return;
        shown.RemoveAt(index);
        // The same song, wherever else the page keeps it (the playlist as it loads, and the whole of it).
        if (!ReferenceEquals(shown, Tracks)) Tracks?.Remove(track);
        _all?.Remove(track);
        IsEmpty = (Tracks?.Count ?? 0) == 0;
        try
        {
            await Client.RemovePlaylistItemAsync(playlist.RatingKey, id);
        }
        catch (PlexException ex)
        {
            Log.LogWarning("Couldn't remove {Track} from {Playlist}: {Message}", track.Title, playlist.Title, ex.Message);
            ErrorMessage = null;
            App.Services.GetRequiredService<Services.NotificationService>().ShowError("Couldn't remove the track", "Plex didn't accept the change.", autoHide: true);
            // Plex still has it: show the playlist as Plex has it.
            await LoadAsync(playlist);
        }
    }

    private bool CanRemoveTrack(Track? track) => Playlist is { IsSmart: false } && track?.PlaylistItemId is not null;

    /// <summary>
    /// Saves a drag reorder to Plex: the moved track now follows the track before it in the list.
    /// Returns false (and reloads the order from Plex) when the server refuses.
    /// </summary>
    public async Task<bool> SaveMoveAsync(Track moved)
    {
        if (Playlist is not { } playlist || Tracks is not { } tracks || moved.PlaylistItemId is not { } id) return false;
        var index = tracks.IndexOf(moved);
        var after = index > 0 ? tracks[index - 1].PlaylistItemId : null;
        try
        {
            await Client.MovePlaylistItemAsync(playlist.RatingKey, id, after);
            return true;
        }
        catch (PlexException ex)
        {
            Log.LogWarning("Couldn't reorder {Playlist}: {Message}", playlist.Title, ex.Message);
            await LoadAsync(playlist);
            return false;
        }
    }

    public async Task LoadAsync(Playlist seed)
    {
        Playlist = seed;
        UpdateMeta(seed);
        var tracks = new IncrementalCollection<Track>((start, size, ct) => Client.GetPlaylistItemsAsync(seed.RatingKey, start, size, ct), 150);
        tracks.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
        Tracks = tracks;
        await RunAsync(async () =>
        {
            var fullTask = Client.GetPlaylistAsync(seed.RatingKey);
            await Task.WhenAll(fullTask, tracks.LoadFirstPageAsync());
            if (fullTask.Result is { } full)
            {
                Playlist = full;
                UpdateMeta(full);
            }
            IsEmpty = tracks.Count == 0;
        });
    }

    /// <summary>
    /// Checks Plex for changes to the playlist without showing a loading state; the track list is only
    /// replaced when its contents actually changed.
    /// </summary>
    public async Task RefreshAsync()
    {
        if (Playlist is not { } current || Session.Client is not { } client) return;
        try
        {
            // Only this playlist is asked for fresh; everything else kept stays as it is.
            var fresh = new IncrementalCollection<Track>((start, size, ct) =>
            {
                using (Core.Caching.ResponseCache.FetchFresh())
                    return client.GetPlaylistItemsAsync(current.RatingKey, start, size, ct);
            }, 150);
            Task<Playlist?> fullTask;
            using (Core.Caching.ResponseCache.FetchFresh())
            {
                fullTask = client.GetPlaylistAsync(current.RatingKey);
                await Task.WhenAll(fullTask, fresh.LoadFirstPageAsync());
            }
            if (!ReferenceEquals(current, Playlist)) return;
            if (fullTask.Result is { } full)
            {
                Playlist = full;
                UpdateMeta(full);
            }
            var loaded = Tracks?.Take(fresh.Count).Select(t => (t.RatingKey, t.PlaylistItemId, t.UserRating)) ?? [];
            var countChanged = fullTask.Result is { } f && f.TrackCount != current.TrackCount;
            if (countChanged || !loaded.SequenceEqual(fresh.Select(t => (t.RatingKey, t.PlaylistItemId, t.UserRating))) || (Tracks?.Count ?? 0) < fresh.Count)
            {
                fresh.LoadFailed += (_, ex) => ErrorMessage = Describe(ex);
                Tracks = fresh;
                IsEmpty = fresh.Count == 0;
            }
        }
        catch (PlexException ex)
        {
            Log.LogInformation("Playlist refresh skipped: {Message}", ex.Message);
        }
    }

    private void UpdateMeta(Playlist p)
    {
        var bits = new List<string>();
        if (p.IsSmart) bits.Add("Smart playlist");
        if (p.TrackCount is > 0) bits.Add(Format.Count(p.TrackCount, "track", "tracks"));
        if (p.DurationMs > 0) bits.Add(Format.LongDuration(TimeSpan.FromMilliseconds(p.DurationMs)));
        MetaText = string.Join(" · ", bits);
    }
}
