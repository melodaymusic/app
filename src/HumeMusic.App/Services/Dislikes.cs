using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>The tracks, albums and artists the listener has rated one star or less.</summary>
public sealed class DislikedSet
{
    public static DislikedSet Empty { get; } = new([], [], []);

    public DislikedSet(IEnumerable<string> tracks, IEnumerable<string> albums, IEnumerable<string> artists)
    {
        Tracks = tracks.ToHashSet();
        Albums = albums.ToHashSet();
        Artists = artists.ToHashSet();
    }

    public IReadOnlySet<string> Tracks { get; }
    public IReadOnlySet<string> Albums { get; }
    public IReadOnlySet<string> Artists { get; }

    /// <summary>One star is a rating of 2 out of 10; unrated is 0 or missing.</summary>
    public static bool IsDisliked(double? rating) => rating is > 0 and <= 2;

    /// <summary>Whether a track may play in a mix: neither it, its album nor its artist is disliked.</summary>
    public bool Allows(Track t) =>
        !IsDisliked(t.UserRating)
        && !Tracks.Contains(t.RatingKey)
        && (t.AlbumRatingKey is null || !Albums.Contains(t.AlbumRatingKey))
        && (t.ArtistRatingKey is null || !Artists.Contains(t.ArtistRatingKey));
}

/// <summary>
/// Keeps disliked music (rated one star or less) out of mixes. The set is read from Plex and kept for
/// a few minutes; rating something in the app refreshes it straight away.
/// </summary>
public sealed class Dislikes
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly PlexSession _session;
    private readonly ILogger<Dislikes> _log;
    private Task<DislikedSet>? _load;
    private DateTime _loadedAt;

    public Dislikes(PlexSession session, ILogger<Dislikes> log)
    {
        _session = session;
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
                artists.Result.Items.Select(a => a.RatingKey));
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Disliked music unavailable: {Message}", ex.Message);
            _load = null;
            return DislikedSet.Empty;
        }
    }
}
