using System.Net;
using System.Text.Json;

namespace MelodayMusic.Core.Logos;

public sealed record FanartLogo(string Url, bool IsHd, int Likes, string? Language);

public enum FanartResult
{
    Found,
    NotFound,
    InvalidKey,
    Unavailable,
}

public sealed class FanartTv
{
    private readonly HttpClient _http;

    public FanartTv(HttpClient http) => _http = http;

    public async Task<(FanartResult Result, IReadOnlyList<FanartLogo> Logos)> GetLogosAsync(string musicBrainzId, string apiKey, CancellationToken ct = default)
    {
        var uri = new Uri($"https://webservice.fanart.tv/v3/music/{Uri.EscapeDataString(musicBrainzId)}?api_key={Uri.EscapeDataString(apiKey)}");
        try
        {
            using var response = await _http.GetAsync(uri, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound) return (FanartResult.NotFound, []);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) return (FanartResult.InvalidKey, []);
            if (!response.IsSuccessStatusCode) return (FanartResult.Unavailable, []);
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var logos = ParseLogos(json);
            return (logos.Count > 0 ? FanartResult.Found : FanartResult.NotFound, logos);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (FanartResult.Unavailable, []);
        }
    }

    public static IReadOnlyList<FanartLogo> ParseLogos(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var logos = new List<FanartLogo>();
        foreach (var (property, hd) in new[] { ("hdmusiclogo", true), ("musiclogo", false) })
        {
            if (!doc.RootElement.TryGetProperty(property, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in list.EnumerateArray())
            {
                if (!item.TryGetProperty("url", out var url) || url.GetString() is not { Length: > 0 } u) continue;
                var likes = item.TryGetProperty("likes", out var l) && int.TryParse(l.ValueKind == JsonValueKind.Number ? l.GetRawText() : l.GetString(), out var n) ? n : 0;
                var lang = item.TryGetProperty("lang", out var g) ? g.GetString() : null;
                logos.Add(new FanartLogo(u, hd, likes, lang));
            }
        }
        return logos
            .OrderByDescending(x => x.IsHd)
            .ThenByDescending(x => x.Language is null or "" or "en" or "00")
            .ThenByDescending(x => x.Likes)
            .ToList();
    }
}
