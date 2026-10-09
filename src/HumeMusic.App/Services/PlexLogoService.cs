using System.Collections.Concurrent;
using HumeMusic.Core.Models;
using HumeMusic.Core.Plex;
using HumeMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Services;

/// <summary>
/// The logos the Plex server keeps for its items (a playlist's, an album's, a collection's, an artist's): a transparent picture
/// of the item's name, fetched from the server as Plex lists it and trimmed to its edges (see <see cref="ArtistLogoService"/>),
/// then remembered for the session. A logo is set on the server (Artwork, here or in any Plex app); there is nothing here to configure.
/// </summary>
public sealed class PlexLogoService(PlexSession session, ILogger<PlexLogoService> log)
{
    private readonly ConcurrentDictionary<string, Task<PreparedLogo?>> _loads = new();

    /// <summary>The item's logo from the server, ready to show, or null when the server has none for it (or it couldn't be read).</summary>
    public Task<PreparedLogo?> GetAsync(MediaItem item)
    {
        if (item.Logo is not { Length: > 0 } path || session.Client is not { } client) return Task.FromResult<PreparedLogo?>(null);
        // The path ends in a stamp that changes whenever the logo does, so a new logo is never mistaken for the old one.
        var key = $"plexlogo-{client.Server.MachineIdentifier[..Math.Min(8, client.Server.MachineIdentifier.Length)]}-{item.RatingKey}";
        var memo = key + "|" + path;
        var load = _loads.GetOrAdd(memo, _ => LoadAsync(client, path, key, item.Title));
        // A failed attempt is tried again on the next visit.
        if (load.IsCompleted && load.Result is null) _loads.TryRemove(memo, out _);
        return load;
    }

    private async Task<PreparedLogo?> LoadAsync(PlexServerClient client, string path, string key, string title)
    {
        try
        {
            var bytes = await client.GetImageBytesAsync(path).ConfigureAwait(false);
            return await ArtistLogoService.PrepareAsync(bytes, key, path, requireTransparency: false).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is PlexException or IOException or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            log.LogInformation("The server's logo for {Item} couldn't be read: {Message}", title, ex.Message);
            return null;
        }
    }
}
