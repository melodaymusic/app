using MelodayMusic.Core.Services;

namespace MelodayMusic.Services;

public sealed class PlexPass
{
    public static readonly Uri LearnMore = new("https://www.plex.tv/plex-pass/");

    private readonly PlexSession _session;

    public PlexPass(PlexSession session)
    {
        _session = session;
    }

    public bool IsAvailable =>
        Environment.GetEnvironmentVariable("MELODAY_ASSUME_NO_PLEX_PASS") != "1" && (_session.Owner ?? _session.Account)?.HasPlexPass != false;

    public string Status => (_session.Owner ?? _session.Account)?.HasPlexPass switch
    {
        true => (_session.Owner ?? _session.Account)!.PlexPassPlan is { Length: > 0 } plan ? $"Active · {char.ToUpperInvariant(plan[0])}{plan[1..]}" : "Active",
        false => "Not active",
        null => "Unknown",
    };
}
