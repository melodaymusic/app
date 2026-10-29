using System.Net;
using MelodayMusic.Core.Models;

namespace MelodayMusic.Core.Plex;

public enum InsecureConnections
{
    Never,
    OnSameNetwork,
    Always,
}

public static class ConnectionSecurity
{
    public static bool IsPrivateAddress(Uri uri) =>
        IPAddress.TryParse(uri.DnsSafeHost, out var address) && Companion.CompanionServer.IsLocal(address);

    public static bool Allows(ServerConnection connection, InsecureConnections policy)
    {
        var uri = connection.Uri;
        if (!string.IsNullOrEmpty(uri.UserInfo)) return false;
        if (uri.Scheme == Uri.UriSchemeHttps) return true;
        if (uri.Scheme != Uri.UriSchemeHttp) return false;
        return policy == InsecureConnections.Always ||
            policy == InsecureConnections.OnSameNetwork && connection.IsLocal && !connection.IsRelay;
    }
}
