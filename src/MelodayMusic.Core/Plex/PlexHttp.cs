using System.Net;
using System.Text.Json;
using MelodayMusic.Core.Plex.Json;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Plex;

public sealed partial class PlexHttp
{
    private readonly HttpClient _http;
    private readonly PlexClientIdentity _identity;
    private readonly ILogger _log;

    public PlexHttp(HttpClient http, PlexClientIdentity identity, ILogger log)
    {
        _http = http;
        _identity = identity;
        _log = log;
    }

    public PlexClientIdentity Identity => _identity;

    public async Task<T> SendJsonAsync<T>(HttpMethod method, Uri uri, string? token, CancellationToken ct, TimeSpan? timeout = null, HttpContent? content = null)
    {
        using var response = await SendAsync(method, uri, token, ct, timeout, content).ConfigureAwait(false);
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var result = await JsonSerializer.DeserializeAsync<T>(body, PlexJson.Options, ct).ConfigureAwait(false);
            return result ?? throw new PlexApiException($"Empty response from {Redact(uri)}");
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "Malformed JSON from {Uri}", Redact(uri));
            throw new PlexApiException("Plex returned a response Meloday Music could not read.", response.StatusCode, ex);
        }
    }

    public async Task SendAndForgetBodyAsync(HttpMethod method, Uri uri, string? token, CancellationToken ct, TimeSpan? timeout = null, HttpContent? content = null)
    {
        using var response = await SendAsync(method, uri, token, ct, timeout, content).ConfigureAwait(false);
    }

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, string? token, CancellationToken ct, TimeSpan? timeout = null, HttpContent? content = null)
    {
        using var request = new HttpRequestMessage(method, uri) { Content = content };
        _identity.Apply(request.Headers, token);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));

        HttpResponseMessage response;
        var started = Environment.TickCount64;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("Timed out: {Method} {Uri}", method, Redact(uri));
            throw new PlexUnreachableException($"Timed out contacting {uri.Host}.");
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("Request failed: {Method} {Uri}: {Message}", method, Redact(uri), ex.Message);
            throw new PlexUnreachableException($"Could not reach {uri.Host}.", ex);
        }

        _log.LogDebug("{Method} {Uri} -> {Status} in {Ms} ms", method, Redact(uri), (int)response.StatusCode, Environment.TickCount64 - started);

        if (response.IsSuccessStatusCode) return response;

        var status = response.StatusCode;
        string snippet = string.Empty;
        try { snippet = (await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim(); }
        catch (Exception ex) when (ex is HttpRequestException or IOException) { }
        snippet = Redact(snippet);
        if (snippet.Length > 300) snippet = snippet[..300];
        response.Dispose();

        _log.LogWarning("{Method} {Uri} failed with {Status}: {Body}", method, Redact(uri), (int)status, Redact(snippet));

        if (status == HttpStatusCode.Unauthorized)
            throw new PlexUnauthorizedException("Your Plex authorization is no longer valid.");
        throw new PlexApiException($"Plex returned {(int)status} {status}.", status);
    }

    public static string Redact(object? value)
    {
        var s = value?.ToString() ?? string.Empty;
        return Logging.LogRedaction.Redact(s);
    }
}
