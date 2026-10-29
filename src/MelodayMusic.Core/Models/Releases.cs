using System.Text.RegularExpressions;

namespace MelodayMusic.Core.Models;

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

public static partial class Releases
{
    public static IReadOnlyList<ReleaseKind> Order { get; } =
    [
        ReleaseKind.Album, ReleaseKind.EP, ReleaseKind.Single, ReleaseKind.Remix, ReleaseKind.Live,
        ReleaseKind.Compilation, ReleaseKind.Soundtrack, ReleaseKind.Mixtape, ReleaseKind.DjMix, ReleaseKind.Demo,
    ];

    [GeneratedRegex(@"\bremix(es|ed)?\b|\brmx\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemixTitle();

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

    public static string Describe(int rank) => $"#{rank:N0} in your library";

    public static bool IsShown(int rank, int limit) => limit != 0 && (limit < 0 || rank <= limit);
}
