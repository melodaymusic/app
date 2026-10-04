using HumeMusic.Core.Models;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Plex;

/// <summary>
/// Chooses the best reachable address for a Plex Media Server. Local connections are preferred,
/// then direct remote connections, then Plex Relay; HTTPS is preferred over HTTP at each tier
/// (mirroring MyPlexResource.preferred_connections in the Plex API reference).
/// </summary>
public sealed class ConnectionSelector
{
    private readonly PlexHttp _http;
    private readonly ILogger _log;

    public ConnectionSelector(PlexHttp http, ILogger<ConnectionSelector> log)
    {
        _http = http;
        _log = log;
    }

    /// <summary>Candidates in preference order. Pure function, unit tested.</summary>
    public static IReadOnlyList<ServerConnection> Order(IEnumerable<ServerConnection> connections, Uri? lastGood = null)
    {
        static int Tier(ServerConnection c) => c.IsRelay ? 2 : c.IsLocal ? 0 : 1;
        return connections
            .DistinctBy(c => c.Uri.AbsoluteUri.TrimEnd('/'))
            .OrderByDescending(c => lastGood is not null && Same(c.Uri, lastGood))
            .ThenBy(Tier)
            .ThenBy(c => c.IsIPv6 ? 1 : 0)
            .ThenBy(c => c.Uri.Scheme == Uri.UriSchemeHttps ? 0 : 1)
            .ToList();
    }

    private static bool Same(Uri a, Uri b) =>
        string.Equals(a.AbsoluteUri.TrimEnd('/'), b.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Probes all candidates in parallel and returns the most preferred one that answered.
    /// Lower-priority results are only used when every higher-priority probe has failed.
    /// </summary>
    public async Task<ServerConnection?> SelectAsync(PlexServer server, Uri? lastGood, CancellationToken ct = default)
    {
        var ordered = Order(server.Connections, lastGood);
        if (ordered.Count == 0) return null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var probes = ordered.Select(c => (Connection: c, Task: ProbeAsync(c, server, cts.Token))).ToList();

        foreach (var (connection, task) in probes)
        {
            if (await task.ConfigureAwait(false))
            {
                _log.LogInformation("Connected to {Server} via {Kind} {Host}", server.Name, connection.Describe(), connection.Uri.Host);
                cts.Cancel();
                return connection;
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
