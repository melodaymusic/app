using MelodayMusic.Core.Models;
using Microsoft.Extensions.Logging;

namespace MelodayMusic.Core.Plex;

public sealed class ConnectionSelector
{
    private readonly PlexHttp _http;
    private readonly ILogger _log;

    public ConnectionSelector(PlexHttp http, ILogger<ConnectionSelector> log)
    {
        _http = http;
        _log = log;
    }

    public static IReadOnlyList<ServerConnection> Order(IEnumerable<ServerConnection> connections, Uri? lastGood = null,
        InsecureConnections insecure = InsecureConnections.Never)
    {
        static int Tier(ServerConnection c) => c.IsRelay ? 2 : c.IsLocal ? 0 : 1;
        return connections
            .Where(c => ConnectionSecurity.Allows(c, insecure))
            .DistinctBy(c => c.Uri.AbsoluteUri.TrimEnd('/'))
            .OrderBy(c => c.Uri.Scheme == Uri.UriSchemeHttps ? 0 : 1)
            .ThenByDescending(c => lastGood is not null && Same(c.Uri, lastGood))
            .ThenBy(Tier)
            .ThenBy(c => c.IsIPv6 ? 1 : 0)
            .ToList();
    }

    private static bool Same(Uri a, Uri b) =>
        string.Equals(a.AbsoluteUri.TrimEnd('/'), b.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    public async Task<ServerConnection?> SelectAsync(PlexServer server, Uri? lastGood, CancellationToken ct = default,
        InsecureConnections insecure = InsecureConnections.Never)
    {
        var ordered = Order(server.Connections, lastGood, insecure);
        if (ordered.Count == 0) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        foreach (var tier in ordered.GroupBy(c => c.Uri.Scheme))
        {
            var probes = tier.Select(c => (Connection: c, Task: ProbeAsync(c, server, cts.Token))).ToList();
            foreach (var (connection, task) in probes)
            {
                if (await task.ConfigureAwait(false))
                {
                    _log.LogInformation("Connected via {Kind} ({Protocol})", connection.Describe(), connection.Uri.Scheme);
                    cts.Cancel();
                    return connection;
                }
            }
        }
        _log.LogWarning("No reachable connection for server {Server} ({Count} candidates)", server.Name, ordered.Count);
        return null;
    }

    public async Task<bool> ProbeAsync(ServerConnection connection, PlexServer server, CancellationToken ct)
    {
        var timeout = connection.IsLocal ? TimeSpan.FromSeconds(4) : TimeSpan.FromSeconds(10);
        try
        {
            var identity = await _http.SendJsonAsync<Dto.PmsResponse>(HttpMethod.Get, new Uri(connection.Uri, "/identity"), server.AccessToken, ct, timeout).ConfigureAwait(false);
            var machineId = identity.MediaContainer?.MachineIdentifier;
            if (!string.IsNullOrEmpty(machineId) && !string.Equals(machineId, server.MachineIdentifier, StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("Connection {Uri} answered as a different server", connection.Uri.Host);
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is PlexException or OperationCanceledException)
        {
            _log.LogDebug("Probe failed for {Uri}: {Message}", connection.Uri.Host, ex.Message);
            return false;
        }
    }
}
