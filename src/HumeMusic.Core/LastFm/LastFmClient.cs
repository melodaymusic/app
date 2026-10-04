using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HumeMusic.Core.LastFm;

/// <summary>The listener's Last.fm connection: their API account's key and secret, and the session once approved.</summary>
public sealed class LastFmAccount
{
    public string ApiKey { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string? SessionKey { get; set; }
    public string? UserName { get; set; }

    public bool IsConnected => !string.IsNullOrEmpty(SessionKey);
}

/// <summary>A song to report to Last.fm.</summary>
public sealed record Scrobble(string Artist, string Title, string? Album, string? AlbumArtist, int? TrackNumber, TimeSpan Duration, DateTimeOffset StartedAt);

public sealed class LastFmException(string message, int code = 0) : Exception(message)
{
    /// <summary>Last.fm's error code (9: the session is no longer valid; 11, 16: try again later).</summary>
    public int Code { get; } = code;
}

/// <summary>
/// Last.fm's API (ws.audioscrobbler.com 2.0): the desktop sign-in (a token the listener approves on
/// last.fm, exchanged for a session), Now Playing, and scrobbles. Calls are signed with the listener's
/// own API secret, as Last.fm requires.
/// </summary>
public sealed class LastFmClient
{
    private static readonly Uri Endpoint = new("https://ws.audioscrobbler.com/2.0/");
    private readonly HttpClient _http;

    public LastFmClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>A request token, the first step of signing in.</summary>
    public async Task<string> GetTokenAsync(LastFmAccount account, CancellationToken ct = default)
    {
        var json = await CallAsync(account, "auth.getToken", [], post: false, ct).ConfigureAwait(false);
        return json.GetProperty("token").GetString() ?? throw new LastFmException("Last.fm didn't send a sign-in token.");
    }

    /// <summary>The page where the listener approves this app for their Last.fm account.</summary>
    public static Uri AuthorizeUri(LastFmAccount account, string token) =>
        new($"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(account.ApiKey)}&token={Uri.EscapeDataString(token)}");

    /// <summary>The session once the listener has approved the token (an error until they have).</summary>
    public async Task<(string SessionKey, string UserName)> GetSessionAsync(LastFmAccount account, string token, CancellationToken ct = default)
    {
        var json = await CallAsync(account, "auth.getSession", [new("token", token)], post: false, ct).ConfigureAwait(false);
        var session = json.GetProperty("session");
        return (session.GetProperty("key").GetString()!, session.GetProperty("name").GetString() ?? string.Empty);
    }

    public Task UpdateNowPlayingAsync(LastFmAccount account, Scrobble song, CancellationToken ct = default)
    {
        var p = new List<KeyValuePair<string, string>> { new("artist", song.Artist), new("track", song.Title), new("sk", account.SessionKey!) };
        AddDetails(p, song, null);
        return CallAsync(account, "track.updateNowPlaying", p, post: true, ct);
    }

    /// <summary>Reports up to 50 songs at once.</summary>
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

    /// <summary>
    /// Last.fm's signature: every parameter (but format and callback) sorted by name, names and values
    /// run together, the secret appended, and the whole hashed with MD5.
    /// </summary>
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
        p.Add(new("api_sig", Sign(p, account.Secret)));
        p.Add(new("format", "json"));
        using var request = post
            ? new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new FormUrlEncodedContent(p) }
            : new HttpRequestMessage(HttpMethod.Get, new Uri(Endpoint, "?" + string.Join("&", p.Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)))));
        request.Headers.UserAgent.ParseAdd("MelodayMusic/1.0");
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
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

    /// <summary>
    /// Last.fm's rule for a scrobble: a song longer than 30 seconds, played for half its length or four
    /// minutes, whichever comes first.
    /// </summary>
    public static bool ShouldScrobble(TimeSpan duration, TimeSpan played) =>
        duration > TimeSpan.FromSeconds(30) && played >= TimeSpan.FromTicks(Math.Min(duration.Ticks / 2, TimeSpan.FromMinutes(4).Ticks));
}
