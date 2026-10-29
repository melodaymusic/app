using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;

namespace MelodayMusic.Services;

public static class PlexPlaylistSync
{
    public static async Task<string?> SyncAsync(PlexServerClient client, string? savedKey, string title, string description,
        IReadOnlyList<Track> tracks, string? coverPath, CancellationToken ct = default)
    {
        var keys = tracks.Select(t => t.RatingKey).ToList();
        Playlist? playlist = null;
        if (savedKey is not null)
        {
            try { playlist = await client.GetPlaylistAsync(savedKey, ct).ConfigureAwait(false); }
            catch (PlexApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
        }
        if (playlist is null)
        {
            if (keys.Count == 0) return null;
            playlist = await client.CreatePlaylistAsync(title, keys, ct).ConfigureAwait(false)
                ?? throw new IOException("Plex did not create the playlist.");
        }
        else
        {
            var existing = new List<Track>();
            for (var start = 0; ; start += 200)
            {
                var page = await client.GetPlaylistItemsAsync(playlist.RatingKey, start, 200, ct).ConfigureAwait(false);
                existing.AddRange(page.Items);
                if (page.Items.Count < 200) break;
            }
            if (!existing.Select(t => t.RatingKey).SequenceEqual(keys))
            {
                var have = existing.Select(t => t.RatingKey).ToHashSet();
                var missing = keys.Where(k => !have.Contains(k)).ToList();
                if (missing.Count > 0) await client.AddToPlaylistAsync(playlist.RatingKey, missing, ct).ConfigureAwait(false);
                var wanted = keys.ToHashSet();
                var kept = new HashSet<string>();
                foreach (var item in existing)
                    if ((!wanted.Contains(item.RatingKey) || !kept.Add(item.RatingKey)) && item.PlaylistItemId is { } id)
                        await client.RemovePlaylistItemAsync(playlist.RatingKey, id, ct).ConfigureAwait(false);
                var updated = await client.GetPlaylistItemsAsync(playlist.RatingKey, 0, 200, ct).ConfigureAwait(false);
                var ids = updated.Items.Where(t => t.PlaylistItemId is not null).GroupBy(t => t.RatingKey).ToDictionary(g => g.Key, g => g.First().PlaylistItemId!);
                string? after = null;
                foreach (var key in keys)
                {
                    if (!ids.TryGetValue(key, out var id)) throw new IOException("Plex did not return all playlist items.");
                    await client.MovePlaylistItemAsync(playlist.RatingKey, id, after, ct).ConfigureAwait(false);
                    after = id;
                }
            }
        }
        await client.EditPlaylistAsync(playlist.RatingKey, title, description, ct).ConfigureAwait(false);
        if (coverPath is not null && File.Exists(coverPath))
            await client.SetPlaylistCoverAsync(playlist.RatingKey, await File.ReadAllBytesAsync(coverPath, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        return playlist.RatingKey;
    }
}
