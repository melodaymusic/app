using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MelodayMusic.Core.LastFm;

public sealed class LastFmAccount
{
    public string ApiKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string? SessionKey { get; set; }
    public string? UserName { get; set; }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);
    public bool IsConnected => HasApiKey && !string.IsNullOrWhiteSpace(Secret) && !string.IsNullOrWhiteSpace(SessionKey);
}

public sealed record Scrobble(string Artist, string Title, string? Album, string? AlbumArtist, int? TrackNumber, TimeSpan Duration, DateTimeOffset StartedAt);

public sealed record ChartTrack(string Title, string Artist, int Listeners);

public sealed class LastFmException(string message, int code = 0) : Exception(message)
{
    public int Code { get; } = code;
}

public sealed class LastFmClient
{
    private static readonly Uri Endpoint = new("https://ws.audioscrobbler.com/2.0/");
    private readonly HttpClient _http;

    public LastFmClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GetTokenAsync(LastFmAccount account, CancellationToken ct = default)
    {
        var json = await CallAsync(account, "auth.getToken", [], post: false, ct).ConfigureAwait(false);
        return json.GetProperty("token").GetString() ?? throw new LastFmException("Last.fm didn't send a sign-in token.");
    }

    public static Uri AuthorizeUri(LastFmAccount account, string token) =>
        new($"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(account.ApiKey)}&token={Uri.EscapeDataString(token)}");

    public async Task<(string SessionKey, string UserName)> GetSessionAsync(LastFmAccount account, string token, CancellationToken ct = default)
    {
        var json = await CallAsync(account, "auth.getSession", [new("token", token)], post: false, ct).ConfigureAwait(false);
        var session = json.GetProperty("session");
        return (session.GetProperty("key").GetString()!, session.GetProperty("name").GetString() ?? string.Empty);
    }

    public async Task<IReadOnlyList<ChartTrack>> GetChartTopTracksAsync(LastFmAccount account, int limit = 100, CancellationToken ct = default, int page = 1)
    {
        var json = await CallAsync(account, "chart.getTopTracks", [new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)), new("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture))], post: false, ct).ConfigureAwait(false);
        return ParseChart(json);
    }

    public static IReadOnlyList<ChartTrack> ParseChart(JsonElement json)
    {
        if (!json.TryGetProperty("tracks", out var tracks) || !tracks.TryGetProperty("track", out var list)) return [];
        var items = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [list];
        var chart = new List<ChartTrack>();
        foreach (var item in items)
        {
            var title = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            var artist = item.TryGetProperty("artist", out var a) && a.TryGetProperty("name", out var an) ? an.GetString() : null;
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(artist)) continue;
            var listeners = 0;
            if (item.TryGetProperty("listeners", out var l)) _ = int.TryParse(l.ValueKind == JsonValueKind.Number ? l.GetRawText() : l.GetString(), out listeners);
            chart.Add(new ChartTrack(title.Trim(), artist.Trim(), listeners));
        }
        return chart;
    }

    public async Task<IReadOnlyList<string>> GetChartArtistsAsync(LastFmAccount account, int page, CancellationToken ct = default)
    {
        var json = await CallAsync(account, "chart.getTopArtists", [new("limit", "100"), new("page", page.ToString(System.Globalization.CultureInfo.InvariantCulture))], false, ct).ConfigureAwait(false);
        if (!json.TryGetProperty("artists", out var artists) || !artists.TryGetProperty("artist", out var list)) return [];
        var items = list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [list];
        return items.Select(i => i.GetProperty("name").GetString()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
    }

    public Task UpdateNowPlayingAsync(LastFmAccount account, Scrobble song, CancellationToken ct = default)
    {
        var p = new List<KeyValuePair<string, string>> { new("artist", song.Artist), new("track", song.Title), new("sk", account.SessionKey!) };
        AddDetails(p, song, null);
        return CallAsync(account, "track.updateNowPlaying", p, post: true, ct);
    }

    public Task ScrobbleAsync(LastFmAccount account, IReadOnlyList<Scrobble> songs, CancellationToken ct = default)
    {
        var p = new List<KeyValuePair<string, string>> { new("sk", account.SessionKey!) };
        for (var i = 0; i < Math.Min(songs.Count, 50); i++)
        {
            var s = songs[i];
            p.Add(new($"artist[{i}]", s.Artist));
            p.Add(new($"track[{i}]", s.Title));
            p.Add(new($"timestamp[{i}]", s.StartedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture)));
            AddDetails(p, s, i);
        }
        return CallAsync(account, "track.scrobble", p, post: true, ct);
    }

    private static void AddDetails(List<KeyValuePair<string, string>> p, Scrobble s, int? index)
    {
        string Key(string name) => index is { } i ? $"{name}[{i}]" : name;
        if (!string.IsNullOrEmpty(s.Album)) p.Add(new(Key("album"), s.Album));
        if (!string.IsNullOrEmpty(s.AlbumArtist) && s.AlbumArtist != s.Artist) p.Add(new(Key("albumArtist"), s.AlbumArtist));
        if (s.TrackNumber is > 0) p.Add(new(Key("trackNumber"), s.TrackNumber.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (s.Duration > TimeSpan.Zero) p.Add(new(Key("duration"), ((int)s.Duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static string Sign(IEnumerable<KeyValuePair<string, string>> parameters, string secret)
    {
        var text = new StringBuilder();
        foreach (var (k, v) in parameters.Where(p => p.Key is not ("format" or "callback")).OrderBy(p => p.Key, StringComparer.Ordinal))
            text.Append(k).Append(v);
        text.Append(secret);
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private async Task<JsonElement> CallAsync(LastFmAccount account, string method, List<KeyValuePair<string, string>> parameters, bool post, CancellationToken ct)
    {
        var p = new List<KeyValuePair<string, string>>(parameters) { new("method", method), new("api_key", account.ApiKey) };
        if (!string.IsNullOrWhiteSpace(account.Secret)) p.Add(new("api_sig", Sign(p, account.Secret)));
        p.Add(new("format", "json"));
        using var request = post
            ? new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new FormUrlEncodedContent(p) }
            : new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint, "?" + string.Join("&", p.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)))));
        request.Headers.UserAgent.ParseAdd("MelodayMusic/1.0");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        JsonElement json;
        try
        {
            json = JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new LastFmException($"Last.fm answered {(int)response.StatusCode}.");
        }
        if (json.TryGetProperty("error", out var error))
            throw new LastFmException(json.TryGetProperty("message", out var m) ? m.GetString() ?? "Last.fm refused the request." : "Last.fm refused the request.", error.GetInt32());
        return json;
    }

    public static bool ShouldScrobble(TimeSpan duration, TimeSpan played) =>
        duration > TimeSpan.FromSeconds(30) && played >= TimeSpan.FromTicks(Math.Min(duration.Ticks / 2, TimeSpan.FromMinutes(4).Ticks));
}
