using MelodayMusic.Core.Models;
using MelodayMusic.Core.Mutes;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

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

    public static bool IsDisliked(double? rating) => rating is > 0 and <= 2;

    public bool IsDupe(Track t) => _dupes is not null && _server is not null && _dupes.IsDupe(_server, t.RatingKey);

    public bool Eligible(Track t) => Allows(t) && !IsDupe(t);

    public bool IsMuted(Track t) => _mutes is not null && _server is not null && _mutes.IsMuted(_server, t.RatingKey, t.ArtistRatingKey);

    public bool BlocksArtist(string artistKey) =>
        Artists.Contains(artistKey) || (_mutes is not null && _server is not null && _mutes.IsArtistMuted(_server, artistKey));

    public bool Allows(Track t) =>
        !IsDisliked(t.UserRating)
        && !Tracks.Contains(t.RatingKey)
        && (t.AlbumRatingKey is null || !Albums.Contains(t.AlbumRatingKey))
        && (t.ArtistRatingKey is null || !Artists.Contains(t.ArtistRatingKey))
        && !IsMuted(t);
}

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

    public void Invalidate() => _load = null;

    private async Task<DislikedSet> LoadAsync()
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return DislikedSet.Empty;
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
