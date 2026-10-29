using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Playlists;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed record EssentialsList(Playlist Playlist, IReadOnlyList<Track> Tracks, string? CoverPath);

public sealed class EssentialsService
{
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(6);
    private readonly PlexSession _session;
    private readonly ILogger<EssentialsService> _log;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, EssentialsList List)> _lists = new();
    public EssentialsService(PlexSession session, ILogger<EssentialsService> log)
    {
        _session = session;
        _log = log;
    }

    public static bool IsEssentials(MediaItem? item) => item is Playlist p && Essentials.IsKey(p.RatingKey);

    public async Task<EssentialsList?> GetAsync(Artist artist, bool force = false, CancellationToken ct = default)
    {
        if (_session.Client is not { } client || _session.Section is not { } section) return null;
        var scope = Scope(artist.RatingKey);
        if (!force && _lists.TryGetValue(scope, out var known) && DateTimeOffset.UtcNow - known.At < Fresh) return known.List;
        try
        {
            var ranked = await client.GetPopularTracksAsync(section.Key, artist.RatingKey, Essentials.Size * 2, ct).ConfigureAwait(false);
            if (ranked.Count == 0)
            {
                var hubs = await client.GetItemHubsAsync(artist.RatingKey, Essentials.Size * 2, ct).ConfigureAwait(false);
                ranked = hubs.FirstOrDefault(h => h.Identifier == "artist.mostpopulartracks")?.Items.OfType<Track>().ToList() ?? [];
            }
            var tracks = Essentials.Pick(ranked);
            if (tracks.Count < Essentials.Smallest)
            {
                _lists.TryRemove(scope, out _);
                return null;
            }
            byte[]? backdrop = null;
            try
            {
                if ((artist.Art ?? artist.Thumb) is { Length: > 0 } art) backdrop = await client.GetImageBytesAsync(art, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
            {
                _log.LogInformation("The backdrop for {Artist}'s Essentials cover could not be read: {Message}", artist.Title, ex.Message);
            }
            var cover = await EssentialsCover.RenderAsync(scope, artist.Title, backdrop, _log).ConfigureAwait(false);
            var list = new EssentialsList(new Playlist
            {
                RatingKey = Essentials.KeyFor(artist.RatingKey),
                Title = Essentials.TitleFor(artist.Title),
                Summary = $"{artist.Title}'s most popular songs, most popular first, by Plex's popularity score. It follows the score as it changes.",
                TrackCount = tracks.Count,
                DurationMs = tracks.Sum(t => t.DurationMs),
                Thumb = cover,
            }, tracks, cover);
            _lists[scope] = (DateTimeOffset.UtcNow, list);
            return list;
        }
        catch (Exception ex) when (ex is PlexException or HttpRequestException or TaskCanceledException)
        {
            _log.LogInformation("Essentials for {Artist} unavailable: {Message}", artist.Title, ex.Message);
            return _lists.TryGetValue(scope, out var old) ? old.List : null;
        }
    }

    public async Task<IReadOnlyList<Track>> TracksAsync(Playlist essentials)
    {
        if (Essentials.ArtistOf(essentials.RatingKey) is not { } key || _session.Client is not { } client) return [];
        if (_lists.TryGetValue(Scope(key), out var known)) return known.List.Tracks;
        var artist = await client.GetArtistAsync(key).ConfigureAwait(false);
        return artist is null ? [] : (await GetAsync(artist).ConfigureAwait(false))?.Tracks ?? [];
    }

    private string Scope(string artistKey) => (_session.Server?.MachineIdentifier ?? "none") + ":" + artistKey;
}
