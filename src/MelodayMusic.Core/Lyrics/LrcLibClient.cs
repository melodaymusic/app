using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Lyrics;

public sealed partial class LrcLibClient
{
    private static readonly Uri Base = new("https://lrclib.net/api/");
    private readonly HttpClient _http;
    private readonly string _userAgent;

    public LrcLibClient(HttpClient http, string version)
    {
        _http = http;
        _userAgent = $"MelodayMusic/{version} (Plex music player for Windows)";
    }

    private sealed class Record
    {
        public string? TrackName { get; set; }
        public string? ArtistName { get; set; }
        public double? Duration { get; set; }
        public bool Instrumental { get; set; }
        public string? PlainLyrics { get; set; }
        public string? SyncedLyrics { get; set; }
    }

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public async Task<TrackLyrics?> GetAsync(string artist, string title, string? album, TimeSpan duration, CancellationToken ct = default)
    {
        var leadArtist = LeadArtist(artist);
        var plainTitle = FeatRegex().Replace(title, string.Empty).Trim();
        var tries = new List<(string Artist, string Title)> { (artist, title) };
        if (leadArtist != artist || plainTitle != title) tries.Add((leadArtist, plainTitle));
        foreach (var (a, t) in tries)
            if (await LookUpAsync(a, t, album, duration, ct).ConfigureAwait(false) is { } found) return found;
        return null;
    }

    public static string LeadArtist(string artist)
    {
        var cut = artist.IndexOfAny(['/', ',', ';']);
        var lead = cut > 0 ? artist[..cut] : artist;
        foreach (var joiner in new[] { " feat. ", " ft. ", " featuring ", " & ", " x ", " with " })
        {
            var at = lead.IndexOf(joiner, StringComparison.OrdinalIgnoreCase);
            if (at > 0) lead = lead[..at];
        }
        return lead.Trim();
    }

    private async Task<TrackLyrics?> LookUpAsync(string artist, string title, string? album, TimeSpan duration, CancellationToken ct)
    {
        var exact = await GetJsonAsync<Record>(
            $"get?artist_name={E(artist)}&track_name={E(title)}&album_name={E(album ?? string.Empty)}&duration={(int)Math.Round(duration.TotalSeconds)}", ct).ConfigureAwait(false);
        if (exact is not null && ToLyrics(exact) is { } found) return found;

        var results = await GetJsonAsync<List<Record>>($"search?artist_name={E(artist)}&track_name={E(title)}", ct).ConfigureAwait(false) ?? [];
        var best = results
            .Where(r => r.Duration is null || duration <= TimeSpan.Zero || Math.Abs(r.Duration.Value - duration.TotalSeconds) <= 3)
            .Where(r => Same(r.TrackName, title))
            .OrderByDescending(r => !string.IsNullOrWhiteSpace(r.SyncedLyrics))
            .ThenBy(r => r.Duration is { } d ? Math.Abs(d - duration.TotalSeconds) : 99)
            .FirstOrDefault();
        return best is null ? null : ToLyrics(best);
    }

    private static string E(string s) => Uri.EscapeDataString(s);

    private static bool Same(string? a, string b) =>
        a is not null && Normalise(a) == Normalise(b);

    private static string Normalise(string s)
    {
        s = FeatRegex().Replace(s, string.Empty);
        return new string(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    [GeneratedRegex(@"\s*[\(\[](feat|ft|with)\.?\s[^\)\]]*[\)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex FeatRegex();

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken ct) where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Base, path));
        request.Headers.UserAgent.ParseAdd(_userAgent);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(body, Json);
    }

    private static TrackLyrics? ToLyrics(Record r)
    {
        if (r.Instrumental) return TrackLyrics.Instrumental(LyricsSource.LrcLib);
        if (!string.IsNullOrWhiteSpace(r.SyncedLyrics) && ParseLrc(r.SyncedLyrics) is { Count: > 0 } synced)
            return new TrackLyrics(synced) { Source = LyricsSource.LrcLib };
        if (!string.IsNullOrWhiteSpace(r.PlainLyrics))
        {
            var lines = r.PlainLyrics.Replace("\r", string.Empty).Split('\n').Select(l => new LyricLine(null, null, l.Trim())).ToList();
            while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
            return lines.Count == 0 ? null : new TrackLyrics(lines) { Source = LyricsSource.LrcLib };
        }
        return null;
    }

    public static IReadOnlyList<LyricLine> ParseLrc(string lrc)
    {
        var timed = new List<(TimeSpan At, string Text)>();
        foreach (var raw in lrc.Replace("\r", string.Empty).Split('\n'))
        {
            var line = raw.Trim();
            var times = new List<TimeSpan>();
            var match = TimeRegex().Match(line);
            while (match.Success && match.Index == 0)
            {
                var minutes = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                var seconds = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                times.Add(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));
                line = line[match.Length..].TrimStart();
                match = TimeRegex().Match(line);
            }
            foreach (var at in times) timed.Add((at, line));
        }
        var ordered = timed.OrderBy(t => t.At).ToList();
        var lines = new List<LyricLine>();
        for (var i = 0; i < ordered.Count; i++)
        {
            TimeSpan? end = i + 1 < ordered.Count ? ordered[i + 1].At : null;
            lines.Add(new LyricLine(ordered[i].At, end, ordered[i].Text));
        }
        while (lines.Count > 0 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Text.Length == 0) lines.RemoveAt(0);
        return lines;
    }

    [GeneratedRegex(@"^\[(\d{1,3}):(\d{1,2}(?:\.\d{1,3})?)\]")]
    private static partial Regex TimeRegex();
}
