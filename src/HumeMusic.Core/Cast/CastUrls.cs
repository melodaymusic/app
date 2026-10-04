namespace HumeMusic.Core.Cast;

/// <summary>Addresses handed to a cast device.</summary>
public static class CastUrls
{
    /// <summary>
    /// The address with its Plex token replaced (the account's own token never leaves this PC: devices
    /// get a transient one). Addresses without a token are returned unchanged.
    /// </summary>
    public static Uri WithToken(Uri url, string token)
    {
        var builder = new UriBuilder(url);
        var query = builder.Query.TrimStart('?');
        if (query.Length == 0) return url;
        var parts = query.Split('&').Select(p =>
            p.StartsWith("X-Plex-Token=", StringComparison.OrdinalIgnoreCase) ? "X-Plex-Token=" + Uri.EscapeDataString(token) : p);
        builder.Query = string.Join('&', parts);
        return builder.Uri;
    }
}
