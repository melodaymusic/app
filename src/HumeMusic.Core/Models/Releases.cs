using System.Text.RegularExpressions;

namespace HumeMusic.Core.Models;

/// <summary>What kind of release an album is, as an artist page groups them.</summary>
public enum ReleaseKind
{
    Album,
    EP,
    Single,
    Remix,
    Live,
    Compilation,
    Soundtrack,
    Mixtape,
    DjMix,
    Demo,
}

/// <summary>
/// Sorting an artist's releases into Albums, EPs, Singles, Remixes and so on: first from the album's
/// own Format and Subformat tags (Plex's release type and secondary types), then from the release hub
/// Plex listed it in, then from its title.
/// </summary>
public static partial class Releases
{
    /// <summary>The order an artist page shows its release rows in.</summary>
    public static IReadOnlyList<ReleaseKind> Order { get; } =
    [
        ReleaseKind.Album, ReleaseKind.EP, ReleaseKind.Single, ReleaseKind.Remix, ReleaseKind.Live,
        ReleaseKind.Compilation, ReleaseKind.Soundtrack, ReleaseKind.Mixtape, ReleaseKind.DjMix, ReleaseKind.Demo,
    ];

    [GeneratedRegex(@"\bremix(es|ed)?\b|\brmx\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemixTitle();

    /// <param name="hub">The artist release hub it came from (e.g. "artist.albums.singles"), if any.</param>
    public static ReleaseKind Classify(Album album, string? hub = null)
    {
        var sub = album.Subformats.Select(s => s.ToLowerInvariant()).ToList();
        var format = album.Formats.Select(s => s.ToLowerInvariant()).ToList();
        bool Has(List<string> tags, string word) => tags.Any(t => t.Contains(word, StringComparison.Ordinal));

        if (Has(sub, "remix")) return ReleaseKind.Remix;
        if (Has(sub, "live")) return ReleaseKind.Live;
        if (Has(sub, "soundtrack")) return ReleaseKind.Soundtrack;
        if (Has(sub, "mixtape") || Has(format, "mixtape")) return ReleaseKind.Mixtape;
        if (Has(sub, "dj-mix") || Has(sub, "dj mix")) return ReleaseKind.DjMix;
        if (Has(sub, "demo")) return ReleaseKind.Demo;
        if (Has(sub, "compilation") || Has(format, "compilation")) return ReleaseKind.Compilation;
        // Remix releases are often tagged only as singles or EPs; their titles say what they are.
        if (RemixTitle().IsMatch(album.Title) && (format.Count == 0 || Has(format, "single") || Has(format, "ep"))) return ReleaseKind.Remix;
        if (format.Contains("ep")) return ReleaseKind.EP;
        if (Has(format, "single")) return ReleaseKind.Single;
        if (format.Count > 0) return ReleaseKind.Album;

        return hub switch
        {
            "artist.albums.singles" => album.TrackCount is > 3 ? ReleaseKind.EP : ReleaseKind.Single,
            "artist.albums.live" => ReleaseKind.Live,
            "artist.albums.compilation" => ReleaseKind.Compilation,
            "artist.albums.soundtrack" => ReleaseKind.Soundtrack,
            "artist.albums.demo" => ReleaseKind.Demo,
            "artist.albums.remix" => ReleaseKind.Remix,
            _ => RemixTitle().IsMatch(album.Title) ? ReleaseKind.Remix : ReleaseKind.Album,
        };
    }

    /// <summary>The row heading for a kind of release.</summary>
    public static string GroupTitle(ReleaseKind kind) => kind switch
    {
        ReleaseKind.Album => "Albums",
        ReleaseKind.EP => "EPs",
        ReleaseKind.Single => "Singles",
        ReleaseKind.Remix => "Remixes",
        ReleaseKind.Live => "Live Albums",
        ReleaseKind.Compilation => "Compilations",
        ReleaseKind.Soundtrack => "Soundtracks",
        ReleaseKind.Mixtape => "Mixtapes",
        ReleaseKind.DjMix => "DJ Mixes",
        _ => "Demos",
    };

    /// <summary>A single release's kind, in words ("Latest release: EP").</summary>
    public static string Label(ReleaseKind kind) => kind switch
    {
        ReleaseKind.Album => "Album",
        ReleaseKind.EP => "EP",
        ReleaseKind.Single => "Single",
        ReleaseKind.Remix => "Remix",
        ReleaseKind.Live => "Live album",
        ReleaseKind.Compilation => "Compilation",
        ReleaseKind.Soundtrack => "Soundtrack",
        ReleaseKind.Mixtape => "Mixtape",
        ReleaseKind.DjMix => "DJ mix",
        _ => "Demo",
    };
}

/// <summary>
/// Where an artist stands in the listener's library by plays: most played first, artists with the
/// same number of plays sharing a place (1, 2, 2, 4), and artists never played not ranked at all.
/// </summary>
public static class LibraryRank
{
    public static Dictionary<string, int> Rank(IEnumerable<(string Key, int Plays)> artists)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var place = 0;
        var previous = -1;
        var position = 0;
        foreach (var (key, plays) in artists.Where(a => a.Plays > 0).OrderByDescending(a => a.Plays))
        {
            position++;
            if (plays != previous) place = position;
            previous = plays;
            ranks.TryAdd(key, place);
        }
        return ranks;
    }

    /// <summary>"#3 in your library".</summary>
    public static string Describe(int rank) => $"#{rank:N0} in your library";

    /// <summary>Whether a rank is shown under the listener's setting (0: none, negative: all, otherwise the top N).</summary>
    public static bool IsShown(int rank, int limit) => limit != 0 && (limit < 0 || rank <= limit);
}
