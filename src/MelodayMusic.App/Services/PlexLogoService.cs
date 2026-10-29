using System.Collections.Concurrent;
using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class PlexLogoService(PlexSession session, ILogger<PlexLogoService> log)
{
    private readonly ConcurrentDictionary<string, Task<PreparedLogo?>> _loads = new();

    public Task<PreparedLogo?> GetAsync(MediaItem item)
    {
        if (item.Logo is not { Length: > 0 } path || session.Client is not { } client) return Task.FromResult<PreparedLogo?>(null);
        var key = $"plexlogo-{client.Server.MachineIdentifier[..Math.Min(8, client.Server.MachineIdentifier.Length)]}-{item.RatingKey}";
        var memo = key + "|" + path;
        var load = _loads.GetOrAdd(memo, _ => LoadAsync(client, path, key, item.Title));
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
