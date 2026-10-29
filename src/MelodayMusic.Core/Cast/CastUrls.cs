namespace MelodayMusic.Core.Cast;

public static class CastUrls
{
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
