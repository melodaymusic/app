using HumeMusic.Core.Models;
using HumeMusic.Core.Mutes;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// What may not go into what Meloday makes: the tracks, albums and artists the listener has rated one star or less, and the songs
/// and artists they have muted for a while (those are asked live, so a mute counts the moment it is made and stops when its time
/// is up, without this set being read again).
/// </summary>
public sealed class DislikedSet
{
    public static DislikedSet Empty { get; } = new([], [], []);

    private readonly MuteStore? _mutes;
    private readonly Core.Meloday.DupeStore? _dupes;
    private readonly string? _server;

    public DislikedSet(IEnumerable<string> tracks, IEnumerable<string> albums, IEnumerable<string> artists, MuteStore? mutes = null, string? server = null,
        Core.Meloday.DupeStore? dupes = null)
    {
        Tracks = tracks.ToHashSet();
        Albums = albums.ToHashSet();
        Artists = artists.ToHashSet();
        _mutes = mutes;
        _server = server;
        _dupes = dupes;
    }

    public IReadOnlySet<string> Tracks { get; }
    public IReadOnlySet<string> Albums { get; }
    public IReadOnlySet<string> Artists { get; }

    /// <summary>One star is a rating of 2 out of 10; unrated is 0 or missing.</summary>
    public static bool IsDisliked(double? rating) => rating is > 0 and <= 2;

    /// <summary>Whether the song is a dupe of another (the listener said they are the same song): the other is the one that plays.</summary>
    public bool IsDupe(Track t) => _dupes is not null && _server is not null && _dupes.IsDupe(_server, t.RatingKey);

    /// <summary>
    /// Whether a song may be put in what Meloday makes for the listener, anywhere in the app (the mixes, the live stations, the
    /// radios, what Made for You is drawn from): it <see cref="Allows"/> it (nothing rated one star or less, nothing muted) and it
    /// is not a dupe of another song. (The Meloday keeps the keeper in a dupe's place instead, so it asks <see cref="Allows"/>.)
    /// </summary>
    public bool Eligible(Track t) => Allows(t) && !IsDupe(t);

    /// <summary>Whether the song, or its artist, is muted for now.</summary>
    public bool IsMuted(Track t) => _mutes is not null && _server is not null && _mutes.IsMuted(_server, t.RatingKey, t.ArtistRatingKey);

    /// <summary>Whether an artist is kept out of mixes: disliked, or muted for now.</summary>
    public bool BlocksArtist(string artistKey) =>
        Artists.Contains(artistKey) || (_mutes is not null && _server is not null && _mutes.IsArtistMuted(_server, artistKey));

    /// <summary>Whether a track may play in a mix: neither it, its album nor its artist is disliked, and neither it nor its artist is muted.</summary>
    public bool Allows(Track t) =>
        !IsDisliked(t.UserRating)
        && !Tracks.Contains(t.RatingKey)
        && (t.AlbumRatingKey is null || !Albums.Contains(t.AlbumRatingKey))
        && (t.ArtistRatingKey is null || !Artists.Contains(t.ArtistRatingKey))
        && !IsMuted(t);
}

/// <summary>
/// Keeps disliked music (rated one star or less) out of mixes. The set is read from Plex and kept for
/// a few minutes; rating something in the app refreshes it straight away.
/// </summary>
public sealed class Dislikes
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly PlexSession _session;
    private readonly MuteStore _mutes;
    private readonly ILogger<Dislikes> _log;
    private Task<DislikedSet>? _load;
    private DateTime _loadedAt;

    private readonly Core.Meloday.DupeStore _dupes;

    public Dislikes(PlexSession session, MuteStore mutes, Core.Meloday.DupeStore dupes, ILogger<Dislikes> log)
    {
        _session = session;
        _mutes = mutes;
        _dupes = dupes;
        _log = log;
    }

    public Task<DislikedSet> GetAsync()
    {
        if (_load is null || DateTime.UtcNow - _loadedAt > Lifetime || _load.IsFaulted)
        {
            _loadedAt = DateTime.UtcNow;
            _load = LoadAsync();
        }
        return _load;
    }

    /// <summary>Forgets the set (a rating changed, or the library did).</summary>
    public void Invalidate() => _load = null;

    private async Task<DislikedSet> LoadAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return DislikedSet.Empty;
        // "userRating<<=3" is Plex's "rating below 3" (one star or less); ">>=0" leaves out unrated.
        KeyValuePair<string, string?>[] low = [new("userRating<<", "3"), new("userRating>>", "0")];
        try
        {
            var tracks = client.GetTracksAsync(section.Key, 0, 2000, "titleSort", default, low);
            var albums = client.GetAlbumsAsync(section.Key, 0, 2000, "titleSort", default, low);
            var artists = client.GetArtistsAsync(section.Key, 0, 2000, "titleSort", default, low);
            await Task.WhenAll(tracks, albums, artists).ConfigureAwait(false);
            return new DislikedSet(
                tracks.Result.Items.Select(t => t.RatingKey),
                albums.Result.Items.Select(a => a.RatingKey),
                artists.Result.Items.Select(a => a.RatingKey),
                _mutes, client.Server.MachineIdentifier, _dupes);
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Disliked music unavailable: {Message}", ex.Message);
            _load = null;
            return DislikedSet.Empty;
        }
    }
}
