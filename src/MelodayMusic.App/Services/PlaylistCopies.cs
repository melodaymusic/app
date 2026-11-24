using MelodayMusic.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MelodayMusic.Services;

// Playlists Meloday keeps on Plex (the Meloday mix, Made by Meloday, the Last.fm chart) already show as
// Meloday's own pages, so the server copies stay out of playlist lists to avoid showing each one twice.
public static class PlaylistCopies
{
    public static bool AnyKept
    {
        get
        {
            var (trending, made, meloday) = Services();
            return trending.IsOn || made.ManagedPlexKeys.Count > 0 || meloday.HasPlexCopy;
        }
    }

    public static IReadOnlyList<Playlist> Hide(IEnumerable<Playlist> playlists)
    {
        var (trending, made, meloday) = Services();
        return made.WithoutCopies(playlists.Where(p => !meloday.IsPlexCopy(p) && !trending.IsPlexCopy(p)));
    }

    // For playlist shelves and pages: hides the copies and adds the Last.fm chart when it's on.
    public static IReadOnlyList<Playlist> ForDisplay(IEnumerable<Playlist> playlists) =>
        App.Services.GetRequiredService<TrendingService>().ForDisplay(Hide(playlists));

    private static (TrendingService, MadePlaylistService, MelodayService) Services() => (
        App.Services.GetRequiredService<TrendingService>(),
        App.Services.GetRequiredService<MadePlaylistService>(),
        App.Services.GetRequiredService<MelodayService>());
}
