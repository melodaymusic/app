using System.Net;

namespace HumeMusic.Core.Companion;

/// <summary>An HTTP request to the player from a Plex app: its path, query and headers.</summary>
public sealed class CompanionRequest
{
    public CompanionRequest(string method, string path, IReadOnlyDictionary<string, string> query, IReadOnlyDictionary<string, string> headers, IPAddress? remote)
    {
        Method = method;
        Path = path;
        Query = query;
        Headers = headers;
        Remote = remote;
    }

    public string Method { get; }
    public string Path { get; }
    public IReadOnlyDictionary<string, string> Query { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }
    public IPAddress? Remote { get; }

    public string? this[string name] => Query.TryGetValue(name, out var v) ? v : null;

    public string? Header(string name) => Headers.TryGetValue(name, out var v) ? v : null;

    public int CommandId => int.TryParse(this["commandID"], out var id) ? id : 0;

    /// <summary>Parses an HTTP request line's target ("/player/playback/play?commandID=3").</summary>
    public static (string Path, Dictionary<string, string> Query) ParseTarget(string target)
    {
        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var q = target.IndexOf('?');
        var path = q < 0 ? target : target[..q];
        if (q >= 0)
        {
            foreach (var pair in target[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = pair.IndexOf('=');
                var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
                var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
                query[key] = value;
            }
        }
        return (Uri.UnescapeDataString(path).TrimEnd('/') is { Length: > 0 } p ? p : "/", query);
    }
}
