using MelodayMusic.Core.Models;
using MelodayMusic.Core.Plex;
using MelodayMusic.Core.Services;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Services;

public sealed class PlaylistCatalog
{
    private static readonly TimeSpan Stale = TimeSpan.FromMinutes(2);

    private readonly PlexSession _session;
    private readonly ILogger _log;
    private DateTime _loadedAt;
    private Task? _refresh;

    public PlaylistCatalog(PlexSession session, LibraryActions actions, ILogger<PlaylistCatalog> log)
    {
        _session = session;
        _log = log;
        actions.PlaylistsChanged += (_, _) => _ = RefreshAsync(force: true);
    }

    public IReadOnlyList<Playlist> Editable { get; private set; } = [];

    public Task RefreshAsync(bool force = false)
    {
        if (!force && _refresh is { IsCompleted: false } running) return running;
        if (!force && DateTime.UtcNow - _loadedAt < Stale && Editable.Count > 0) return Task.CompletedTask;
        _loadedAt = DateTime.UtcNow;
        return _refresh = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_session.Client is not { } client) return;
        try
        {
            var page = await client.GetPlaylistsAsync(0, 200, default, "updatedAt:desc").ConfigureAwait(false);
            Editable = page.Items.Where(p => !p.IsSmart).ToList();
        }
        catch (PlexException ex)
        {
            _log.LogInformation("Playlists for menus unavailable: {Message}", ex.Message);
        }
    }
}
