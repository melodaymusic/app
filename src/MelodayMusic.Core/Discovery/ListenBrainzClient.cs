using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MelodayMusic.Core.LastFm;

namespace MelodayMusic.Core.Discovery;

public sealed class ListenBrainzClient(HttpClient http)
{
    public async Task<string?> ValidateAsync(string token, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "validate-token", token, null, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.TryGetProperty("valid", out var valid) && valid.ValueKind == JsonValueKind.True
            ? json.GetProperty("user_name").GetString() : null;
    }

    public async Task SubmitAsync(string token, IReadOnlyList<Scrobble> listens, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "submit-listens", token, new
        {
            listen_type = "import",
            payload = listens.Select(s => new
            {
                listened_at = s.StartedAt.ToUnixTimeSeconds(),
                track_metadata = new
                {
                    artist_name = s.Artist, track_name = s.Title, release_name = s.Album,
                    additional_info = new { media_player = "Meloday Music", submission_client = "Meloday Music", duration_ms = (long)s.Duration.TotalMilliseconds }
                }
            }).ToArray()
        }, ct);
    }

    public async Task<long> CountAsync(string user, CancellationToken ct = default)
    {
        using var response = await SendAsync(HttpMethod.Get, $"user/{Uri.EscapeDataString(user)}/listen-count", null, null, ct);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("payload").GetProperty("count").GetInt64();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? token, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://api.listenbrainz.org/1/" + path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var response = await http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"ListenBrainz returned {(int)status}.", null, status);
        }
        return response;
    }
}
