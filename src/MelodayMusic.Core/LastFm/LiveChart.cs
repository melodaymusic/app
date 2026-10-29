using System.Net;
using System.Text.RegularExpressions;

namespace MelodayMusic.Core.LastFm;

public sealed record LiveChart(IReadOnlyList<ChartTrack> Tracks, IReadOnlyList<string> Artists)
{
    public static LiveChart Parse(string html)
    {
        var tables = Regex.Matches(html, "<table\\b[^>]*class=[\"'][^\"']*\\bglobalchart\\b[^\"']*[\"'][^>]*>(.*?)</table>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (tables.Count < 2) throw new LastFmException("Last.fm's live chart is unavailable.");
        static string Cell(string row, string css)
        {
            var match = Regex.Match(row, "<td\\b[^>]*class=[\"'][^\"']*\\b" + css + "\\b[^\"']*[\"'][^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var link = Regex.Match(match.Groups[1].Value, "<a\\b[^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            return WebUtility.HtmlDecode(Regex.Replace(link.Groups[1].Value, "<[^>]*>", "")).Trim();
        }
        var tracks = new List<ChartTrack>();
        foreach (Match row in Regex.Matches(tables[0].Groups[1].Value, "<tr\\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var title = Cell(row.Value, "globalchart-name"); var artist = Cell(row.Value, "globalchart-track-artist-name");
            if (title.Length > 0 && artist.Length > 0) tracks.Add(new(title, artist, 0));
        }
        var artists = new List<string>();
        foreach (Match row in Regex.Matches(tables[1].Groups[1].Value, "<tr\\b[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
        {
            var name = Cell(row.Value, "globalchart-name"); if (name.Length > 0) artists.Add(name);
        }
        if (tracks.Count == 0 || artists.Count == 0) throw new LastFmException("Last.fm's live chart could not be read.");
        return new(tracks, artists);
    }
}
