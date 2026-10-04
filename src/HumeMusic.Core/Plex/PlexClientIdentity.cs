using System.Net.Http.Headers;
using System.Text;

namespace HumeMusic.Core.Plex;

/// <summary>
/// Identifies this installation of Meloday Music to Plex. The client identifier is generated once
/// per installation and persisted, as Plex associates authorisations and sessions with it.
/// </summary>
public sealed class PlexClientIdentity
{
    public const string ProductName = "Meloday Music";

    public PlexClientIdentity(string clientIdentifier, string version, string deviceName, string platformVersion)
    {
        ClientIdentifier = clientIdentifier;
        Version = version;
        DeviceName = deviceName;
        PlatformVersion = platformVersion;
    }

    public string ClientIdentifier { get; }
    public string Product => ProductName;
    public string Version { get; }
    public string Platform => "Windows";
    public string PlatformVersion { get; }
    public string Device => "Windows";
    public string DeviceName { get; }

    /// <summary>The X-Plex-* identification headers, without a token.</summary>
    public IEnumerable<KeyValuePair<string, string>> Headers()
    {
        yield return new("X-Plex-Client-Identifier", ClientIdentifier);
        yield return new("X-Plex-Product", Product);
        yield return new("X-Plex-Version", Version);
        yield return new("X-Plex-Platform", Platform);
        yield return new("X-Plex-Platform-Version", PlatformVersion);
        yield return new("X-Plex-Device", Device);
        yield return new("X-Plex-Device-Name", DeviceName);
        yield return new("X-Plex-Model", "PC");
        yield return new("X-Plex-Provides", "player");
    }

    public void Apply(HttpRequestHeaders headers, string? token)
    {
        foreach (var (k, v) in Headers()) headers.TryAddWithoutValidation(k, v);
        if (!string.IsNullOrEmpty(token)) headers.TryAddWithoutValidation("X-Plex-Token", token);
        headers.Accept.Clear();
        headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>
    /// Identification as query-string parameters, for URLs consumed by components that cannot send
    /// custom headers (the media player and the image pipeline).
    /// </summary>
    public string ToQueryString(string? token)
    {
        var sb = new StringBuilder();
        foreach (var (k, v) in Headers())
        {
            if (k == "X-Plex-Provides" || k == "X-Plex-Model") continue;
            if (sb.Length > 0) sb.Append('&');
            sb.Append(k).Append('=').Append(Uri.EscapeDataString(v));
        }
        if (!string.IsNullOrEmpty(token)) sb.Append("&X-Plex-Token=").Append(Uri.EscapeDataString(token));
        return sb.ToString();
    }
}
