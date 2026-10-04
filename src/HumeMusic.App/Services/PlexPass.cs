using HumeMusic.Core.Services;

namespace HumeMusic.Services;

/// <summary>
/// Whether the listener has Plex Pass. Plex Pass features (Plex's stations, artist mixes, sonic
/// radio) are simply left out of the app without it; Settings says which features they are.
/// </summary>
public sealed class PlexPass
{
    public static readonly Uri LearnMore = new("https://www.plex.tv/plex-pass/");

    private readonly PlexSession _session;

    public PlexPass(PlexSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Plex Pass features can be used: the account has Plex Pass, or plex.tv couldn't say (then
    /// Plex itself decides). MELODAY_ASSUME_NO_PLEX_PASS=1 shows the app as it is without it.
    /// </summary>
    public bool IsAvailable =>
        Environment.GetEnvironmentVariable("MELODAY_ASSUME_NO_PLEX_PASS") != "1" && _session.Account?.HasPlexPass != false;

    /// <summary>"Active · Lifetime", "Not active" or "Unknown", for Settings.</summary>
    public string Status => _session.Account?.HasPlexPass switch
    {
        true => _session.Account.PlexPassPlan is { Length: > 0 } plan ? $"Active · {char.ToUpperInvariant(plan[0])}{plan[1..]}" : "Active",
        false => "Not active",
        null => "Unknown",
    };
}
