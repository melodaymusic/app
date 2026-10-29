using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Discovery;

public sealed class CatalogueItem : MediaItem
{
    public CatalogueRelease Release { get; }
    public CatalogueItem(CatalogueRelease release)
    { Release = release; RatingKey = "musicbrainz:" + release.Id; Title = release.Title; Thumb = release.CoverUrl; }
    public override MediaKind Kind => MediaKind.Album;
    public override string Subtitle => Release.Details;
}

public sealed record CatalogueRelease(string Id, string Title, string Type, string Date)
{
    public string Details => string.Join(" · ", new[] { Date, Type }.Where(s => s.Length > 0));
    public string Url => $"https://musicbrainz.org/release-group/{Id}";
    public string CoverUrl => $"https://coverartarchive.org/release-group/{Id}/front-250";
    public bool IsRecent(DateOnly today) => MusicBrainzClient.ReleaseDate(Date) is { } date
        && date <= today && date >= today.AddMonths(-6);
}

public sealed partial class MusicBrainzClient(HttpClient http, string version)
{
    private static readonly SemaphoreSlim Requests = new(1, 1);
    private static DateTimeOffset _nextRequest;

    public async Task<string?> FindArtistAsync(string name, CancellationToken ct = default)
    {
        var query = Uri.EscapeDataString("artist:\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"");
        var json = await GetAsync($"artist?query={query}&limit=25&fmt=json", ct);
        var matches = json.GetProperty("artists").EnumerateArray()
            .Where(a => Normalize(a.GetProperty("name").GetString() ?? "") == Normalize(name)
                && a.TryGetProperty("score", out var score) && score.GetInt32() >= 95)
            .Select(a => a.GetProperty("id").GetString()).Distinct().ToList();
        return matches.Count == 1 && Guid.TryParse(matches[0], out var id) ? id.ToString() : null;
    }

    public async Task<IReadOnlyList<CatalogueRelease>> ReleasesAsync(string artistId, CancellationToken ct = default)
    {
        if (!Guid.TryParse(artistId, out var id)) return [];
        var releases = new List<CatalogueRelease>();
        for (var offset = 0; ;)
        {
            var json = await GetAsync($"release-group?artist={id}&release-group-status=website-default&limit=100&offset={offset}&fmt=json", ct);
            var page = ParseReleases(json);
            releases.AddRange(page);
            var fetched = json.GetProperty("release-groups").GetArrayLength();
            offset += fetched;
            if (fetched == 0 || offset >= json.GetProperty("release-group-count").GetInt32()) break;
        }
        return releases.DistinctBy(r => r.Id).OrderByDescending(r => r.Date, StringComparer.Ordinal).ThenBy(r => r.Title).ToList();
    }

    private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
    {
        await Requests.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var delay = _nextRequest - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
            _nextRequest = DateTimeOffset.UtcNow.AddSeconds(1.1);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://musicbrainz.org/ws/2/" + path);
            request.Headers.UserAgent.ParseAdd($"MelodayMusic/{version} (https://github.com/myplexscripts/melodaymusic)");
            using var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token).ConfigureAwait(false);
        }
        finally { Requests.Release(); }
    }

    public static IReadOnlyList<CatalogueRelease> ParseReleases(JsonElement json)
    {
        if (!json.TryGetProperty("release-groups", out var groups)) return [];
        var result = new List<CatalogueRelease>();
        foreach (var group in groups.EnumerateArray())
        {
            var id = group.GetProperty("id").GetString();
            if (!Guid.TryParse(id, out _)) continue;
            var type = group.TryGetProperty("primary-type", out var t) ? t.GetString() ?? "Release" : "Release";
            if (type is not ("Album" or "EP" or "Single")) continue;
            if (group.TryGetProperty("secondary-types", out var secondary) && secondary.GetArrayLength() > 0)
                type = string.Join(" / ", secondary.EnumerateArray().Select(s => s.GetString()).Prepend(type));
            result.Add(new(id!, group.GetProperty("title").GetString() ?? "", type,
                group.TryGetProperty("first-release-date", out var d) ? d.GetString() ?? "" : ""));
        }
        return result;
    }

    public static bool InLibrary(CatalogueRelease release, IEnumerable<Album> albums) => albums.Any(a =>
        Normalize(Edition().Replace(a.Title, "")) == Normalize(Edition().Replace(release.Title, ""))
        && (a.Formats.Count == 0 || release.Type.Split(" / ")[0] ==
            (a.Formats.Any(f => f.Equals("ep", StringComparison.OrdinalIgnoreCase)) ? "EP" :
             a.Formats.Any(f => f.Contains("single", StringComparison.OrdinalIgnoreCase)) ? "Single" : "Album")));

    public static DateOnly? ReleaseDate(string date)
    {
        if (date.Length == 7) date += "-01";
        return DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
    }

    private static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
        .Select(char.ToLowerInvariant).ToArray());

    [GeneratedRegex(@"\s*[\[(](?:[^\])]*(?:deluxe|expanded|remaster(?:ed)?|anniversary|bonus|special edition)[^\])]*)[\])]", RegexOptions.IgnoreCase)]
    private static partial Regex Edition();
}
