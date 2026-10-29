using System.Collections.Concurrent;
using MelodayMusic.Core.Meloday;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;

namespace MelodayMusic.Services;

public sealed class FeaturedArtistService(PlexSession session)
{
    private const int Most = 3;
    private readonly ConcurrentDictionary<string, Task<Artist?>> _found = new();

    public async Task<IReadOnlyList<Artist>> GetAsync(Track track, CancellationToken ct = default)
    {
        if (session.Client is not { } client || session.Section is not { } section) return [];
        var groups = ArtistCredit.Others(track.TrackArtist, track.Title, track.ArtistTitle);
        if (groups.Count == 0) return [];
        var artists = new List<Artist>();
        foreach (var group in groups)
        {
            if (artists.Count >= Most) break;
            if (await FindAsync(client, section, group[0], ct).ConfigureAwait(false) is { } whole)
            {
                Add(artists, whole);
                continue;
            }
            foreach (var part in group.Skip(1))
                if (await FindAsync(client, section, part, ct).ConfigureAwait(false) is { } found) Add(artists, found);
        }
        return artists.Take(Most).ToList();
    }

    private static void Add(List<Artist> artists, Artist artist)
    {
        if (artists.All(a => a.RatingKey != artist.RatingKey)) artists.Add(artist);
    }

    private async Task<Artist?> FindAsync(PlexServerClient client, MusicSection section, string name, CancellationToken ct)
    {
        var key = client.Server.MachineIdentifier + "|" + section.Key + "|" + MelodayText.NormalizeArtist(name);
        var task = _found.GetOrAdd(key, _ => LookUpAsync(client, section, name, ct));
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlexException or OperationCanceledException or HttpRequestException)
        {
            _found.TryRemove(key, out _);
            return null;
        }
    }

    private static async Task<Artist?> LookUpAsync(PlexServerClient client, MusicSection section, string name, CancellationToken ct)
    {
        var wanted = MelodayText.NormalizeArtist(name);
        if (wanted.Length == 0) return null;
        var results = await client.SearchAsync(name, section.Key, 8, ct).ConfigureAwait(false);
        return results.Artists.FirstOrDefault(a => MelodayText.NormalizeArtist(a.Title) == wanted);
    }
}
