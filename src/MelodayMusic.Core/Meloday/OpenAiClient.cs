using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MelodayMusic.Core.Meloday;

public enum OpenAiResult
{
    Ok,
    InvalidKey,
    Unavailable,
}

public sealed class OpenAiClient(HttpClient http)
{
    public const string Model = "gpt-4.1-mini";

    private static readonly Uri Responses = new("https://api.openai.com/v1/responses");
    private static readonly Uri Models = new("https://api.openai.com/v1/models");

    public async Task<OpenAiResult> CheckKeyAsync(string apiKey, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Models);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            return Result(response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return OpenAiResult.Unavailable;
        }
    }

    public async Task<(OpenAiResult Result, string? Text)> AnswerAsync(string apiKey, string input, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Responses)
        {
            Content = new StringContent(JsonSerializer.Serialize(new { model = Model, input }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            var result = Result(response.StatusCode);
            if (result != OpenAiResult.Ok) return (result, null);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (OpenAiResult.Ok, OutputText(body));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return (OpenAiResult.Unavailable, null);
        }
    }

    private static OpenAiResult Result(HttpStatusCode status) => status switch
    {
        HttpStatusCode.OK => OpenAiResult.Ok,
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => OpenAiResult.InvalidKey,
        _ => OpenAiResult.Unavailable,
    };

    public static string? OutputText(string json)
    {
        var root = JsonNode.Parse(json);
        if (root?["output_text"]?.GetValue<string>() is { Length: > 0 } direct) return direct;
        var parts = new List<string>();
        foreach (var item in root?["output"]?.AsArray() ?? [])
        {
            if (item?["type"]?.GetValue<string>() != "message") continue;
            foreach (var content in item["content"]?.AsArray() ?? [])
                if (content?["type"]?.GetValue<string>() == "output_text" && content["text"]?.GetValue<string>() is { } text) parts.Add(text);
        }
        return parts.Count > 0 ? string.Concat(parts) : null;
    }
}
