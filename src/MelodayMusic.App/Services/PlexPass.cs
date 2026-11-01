using MelodayMusic.Core.Services;

namespace MelodayMusic.Services;

public sealed class PlexPass
{
    private readonly PlexSession _session;

    public PlexPass(PlexSession session)
    {
        _session = session;
    }

    public bool IsAvailable =>
        Environment.GetEnvironmentVariable("MELODAY_ASSUME_NO_PLEX_PASS") != "1" && (_session.Owner ?? _session.Account)?.HasPlexPass != false;
}
