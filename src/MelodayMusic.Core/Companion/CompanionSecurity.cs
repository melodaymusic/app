using System.Net;
using System.Text.RegularExpressions;

namespace MelodayMusic.Core.Companion;

public static partial class CompanionSecurity
{
    public static bool SameOrigin(Uri a, Uri b) => a.Scheme == b.Scheme &&
        string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase) && a.Port == b.Port;

    public static bool AllowsOrigin(string? value, Uri? plexServer)
    {
        if (value is null) return true;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) || !string.IsNullOrEmpty(origin.UserInfo) ||
            origin.AbsolutePath != "/" || origin.Query.Length > 0 || origin.Fragment.Length > 0) return false;
        return SameOrigin(origin, new Uri("https://app.plex.tv")) ||
            plexServer is not null && SameOrigin(origin, plexServer);
    }

    public static bool AllowsHost(string? value, IPEndPoint local)
    {
        if (value is null || !Uri.TryCreate("http://" + value, UriKind.Absolute, out var host) ||
            host.Port != local.Port || host.AbsolutePath != "/" || host.Query.Length > 0 ||
            host.Fragment.Length > 0 || host.UserInfo.Length > 0) return false;
        if (host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return IPAddress.IsLoopback(local.Address);
        return IPAddress.TryParse(host.DnsSafeHost, out var ip) && ip.Equals(local.Address);
    }

    public static bool IsPlayQueueKey(string key) => PlayQueuePattern().IsMatch(key);

    public static string CorsHeaders(string? requested) => string.Join(", ", (requested ??
        "X-Plex-Client-Identifier, X-Plex-Device-Name, X-Plex-Token, X-Plex-Target-Client-Identifier, Content-Type")
        .Split(',').Select(h => h.Trim()).Where(h => h.Length > 0 &&
            h.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') &&
            (h.StartsWith("X-Plex-", StringComparison.OrdinalIgnoreCase) ||
             h.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
             h.Equals("Accept", StringComparison.OrdinalIgnoreCase))));

    [GeneratedRegex(@"\A/playQueues/[0-9]+(?:\?[^#\r\n]*)?\z", RegexOptions.IgnoreCase)]
    private static partial Regex PlayQueuePattern();
}
