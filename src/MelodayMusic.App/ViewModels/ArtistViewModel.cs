using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using MelodayMusic.Helpers;
using MelodayMusic.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.ViewModels;

public sealed class AlbumGroup
{
    public required string Title { get; init; }
    public required IReadOnlyList<Album> Albums { get; init; }
}

public sealed record ArtistFact(string Icon, string Label, string Value);

public sealed class RankedTrack
{
    public Track Track { get; init; } = null!;
    public int Rank { get; init; }
}

public sealed partial class ArtistViewModel : ViewModelBase
{
    private readonly PlaylistArtistIndex _playlists;
    private readonly ArtistRanks _ranks;
    private readonly EssentialsService _essentials;

    public ArtistViewModel(PlexSession session, PlaylistArtistIndex playlists, ArtistRanks ranks, EssentialsService essentials, ILogger<ArtistViewModel> log) : base(session, log)
    {
        _playlists = playlists;
        _ranks = ranks;
        _essentials = essentials;
    }

    [ObservableProperty] public partial Artist? Artist { get; private set; }
    public bool HasFullArtist { get; private set; }
    public ObservableCollection<RankedTrack> TopSongs { get; } = [];
    public ObservableCollection<AlbumGroup> AlbumGroups { get; } = [];
    public ObservableCollection<Artist> SimilarArtists { get; } = [];
    public ObservableCollection<Playlist> FeaturedPlaylists { get; } = [];
    public ObservableCollection<ArtistFact> Facts { get; } = [];
    [ObservableProperty] public partial string RankText { get; private set; } = string.Empty;
    [ObservableProperty] public partial bool HasAbout { get; private set; }
    [ObservableProperty] public partial Album? LatestRelease { get; private set; }
    [ObservableProperty] public partial string LatestReleaseKind { get; private set; } = "Album";
    [ObservableProperty] public partial string LatestReleaseMeta { get; private set; } = string.Empty;

    public async Task LoadAsync(Artist seed)
    {
        CancelCatalogue();
        NewReleases.Clear(); MissingReleases.Clear(); CatalogueVisible = false;
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

            IReadOnlyList<Track> top = HubTracks(hubs, "artist.mostpopulartracks");
            if (top.Count == 0) top = HubTracks(hubs, "artist.mostplayedtracks");
            if (top.Count == 0) top = await Optional(client.GetPopularTracksAsync(SectionKey, seed.RatingKey, 25)) ?? [];
            if (top.Count == 0)
            {
                var own = await client.GetTracksAsync(SectionKey, 0, 25, "viewCount:desc", default,
                    new KeyValuePair<string, string?>("artist.id", seed.RatingKey));
                top = own.Items;
            }
            TopSongs.Clear();
            foreach (var (t, i) in top.DistinctBy(t => t.Title.ToLowerInvariant()).Take(10).Select((t, i) => (t, i)))
                TopSongs.Add(new RankedTrack { Track = t, Rank = i + 1 });

            var groups = await ReleaseGroupsAsync(client, albums, related);
            var releases = groups.SelectMany(g => g.Albums).ToList();
            _catalogueAlbums = releases;
            var appearances = await AppearsOnAsync(client, artist, releases);
            if (appearances.Count > 0) groups = [.. groups, new AlbumGroup { Title = "Appears On", Albums = appearances }];
            AlbumGroups.Clear();
            foreach (var group in groups) AlbumGroups.Add(group);

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

            SimilarArtists.Clear();
            foreach (var a in (App.Services.GetRequiredService<Services.MusicIntelligence>().IsMeloday ? Array.Empty<Artist>() : HubArtists(related, "artist.similar").Concat(HubArtists(related, "external.artist.similar.sonically")))
                         .Where(a => a.RatingKey != seed.RatingKey).DistinctBy(a => a.RatingKey).Take(16))
                SimilarArtists.Add(a);
            if (SimilarArtists.Count == 0)
            {
                foreach (var a in (await Optional(App.Services.GetRequiredService<Services.MusicIntelligence>().SimilarAsync<Artist>(seed.RatingKey, 16, 0.35)) ?? [])
                             .Where(a => a.RatingKey != seed.RatingKey).Take(16))
                    SimilarArtists.Add(a);
            }

            BuildFacts(artist, releases);
            UpdateHasAbout();
        });
        _ = LoadFeaturedPlaylistsAsync(seed);
        _ = LoadRankAsync(seed);
        _ = RefreshCatalogueAsync();
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

    private async Task LoadFeaturedPlaylistsAsync(Artist seed)
    {
        try
        {
            var artist = Artist ?? seed;
            var essentials = await _essentials.GetAsync(artist);
            if (Artist?.RatingKey != seed.RatingKey) return;
            FeaturedPlaylists.Clear();
            if (essentials is not null) FeaturedPlaylists.Add(essentials.Playlist);
            var featured = await _playlists.GetFeaturingAsync(artist);
            if (Artist?.RatingKey != seed.RatingKey) return;
            foreach (var f in featured.Take(24)) FeaturedPlaylists.Add(f.Playlist);
        }
        catch (Exception ex) when (ex is PlexException or OperationCanceledException or IOException)
        {
            Log.LogInformation("Featured playlists unavailable: {Message}", ex.Message);
        }
    }

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

    private async Task<IReadOnlyList<AlbumGroup>> ReleaseGroupsAsync(PlexServerClient client, IReadOnlyList<Album> albums, IReadOnlyList<Hub> related)
    {
        var hubs = related.Where(h => h.Identifier.StartsWith("artist.albums.", StringComparison.Ordinal) && h.Items.Count > 0).ToList();
        var loads = hubs.Select(async hub =>
        {
            if (hub.Key is { } key && (hub.More || hub.Size > hub.Items.Count))
            {
                var all = await Optional(client.GetHubItemsAsync(key, 0, 300));
                if (all is { Count: > 0 }) return all.OfType<Album>().ToList();
            }
            return hub.Items.OfType<Album>().ToList();
        }).ToList();
        var results = await Task.WhenAll(loads);

        var found = new Dictionary<string, (Album Album, string? Hub)>(StringComparer.Ordinal);
        foreach (var a in albums) found.TryAdd(a.RatingKey, (a, null));
        for (var i = 0; i < hubs.Count; i++)
            foreach (var a in results[i]) found.TryAdd(a.RatingKey, (a, hubs[i].Identifier));

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
