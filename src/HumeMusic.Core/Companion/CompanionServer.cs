using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace HumeMusic.Core.Companion;

/// <summary>What the player is and does, for <see cref="CompanionServer"/>.</summary>
public interface ICompanionPlayer
{
    /// <summary>The player's timeline now (called from any thread).</summary>
    CompanionTimeline Timeline { get; }

    /// <summary>
    /// Carries out a playback command (/player/playback/...): its last path segment ("play",
    /// "playMedia", "seekTo"...) and the request. False when it isn't one the player knows.
    /// </summary>
    Task<bool> ExecuteAsync(string command, CompanionRequest request);
}

/// <summary>
/// The player side of Plex's remote-control protocol ("Plex Companion"): a small HTTP server the Plex
/// apps send commands to, and the timeline reports they follow the player by, sent to subscribed
/// controllers or answered to their polls. Commands are only taken from the local network.
/// </summary>
public sealed class CompanionServer : IAsyncDisposable
{
    public const int PreferredPort = 32500;

    private readonly ICompanionPlayer _player;
    private readonly string _clientId;
    private readonly string _name;
    private readonly string _version;
    private readonly ILogger _log;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(4) };
    private readonly ConcurrentDictionary<string, Subscriber> _subscribers = new();
    private readonly CancellationTokenSource _stop = new();
    private TcpListener? _listener;
    private CompanionTimeline _lastSent = CompanionTimeline.Stopped;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _lastCommandId;

    public CompanionServer(ICompanionPlayer player, string clientId, string name, string version, ILogger log)
    {
        _player = player;
        _clientId = clientId;
        _name = name;
        _version = version;
        _log = log;
    }

    /// <summary>The port the server listens on, once started.</summary>
    public int Port { get; private set; }

    /// <summary>How many controllers follow the player now.</summary>
    public int SubscriberCount => _subscribers.Count;

    /// <summary>Starts listening on 32500 (Plex players' usual port) or the next free one.</summary>
    public void Start()
    {
        for (var port = PreferredPort; port < PreferredPort + 10; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                _listener = listener;
                Port = port;
                break;
            }
            catch (SocketException)
            {
                // Taken (another Plex player on this PC): try the next.
            }
        }
        if (_listener is null) throw new InvalidOperationException("No free port for remote control");
        _log.LogInformation("Remote control listening on port {Port}", Port);
        _ = AcceptLoopAsync(_stop.Token);
        _ = ReportLoopAsync(_stop.Token);
    }

    /// <summary>The player changed (a new song, paused, a seek): controllers hear about it now.</summary>
    public void NotifyChanged()
    {
        Interlocked.Exchange(ref _changed, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException ex)
            {
                _log.LogDebug("Remote control accept failed: {Message}", ex.Message);
                continue;
            }
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using var _ = client;
        try
        {
            var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
            if (remote is not null && !IsLocal(remote))
            {
                // Remote control is for this network only.
                return;
            }
            client.ReceiveTimeout = 5000;
            var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, remote, ct).ConfigureAwait(false);
            if (request is null) return;
            var (status, body) = await RouteAsync(request, ct).ConfigureAwait(false);
            await WriteResponseAsync(stream, request, status, body, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            // A controller that went away mid-request.
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Remote control request failed");
        }
    }

    /// <summary>Private, link-local and loopback addresses: the ones on this network.</summary>
    public static bool IsLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal;
        var b = address.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    private static async Task<CompanionRequest?> ReadRequestAsync(NetworkStream stream, IPAddress? remote, CancellationToken ct)
    {
        var buffer = new byte[16384];
        var length = 0;
        int headerEnd;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while ((headerEnd = IndexOf(buffer, length, "\r\n\r\n"u8)) < 0)
        {
            if (length == buffer.Length) return null;
            var read = await stream.ReadAsync(buffer.AsMemory(length), timeout.Token).ConfigureAwait(false);
            if (read == 0) return null;
            length += read;
        }
        var head = Encoding.ASCII.GetString(buffer, 0, headerEnd).Split("\r\n");
        var line = head[0].Split(' ');
        if (line.Length < 2) return null;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in head.Skip(1))
        {
            var colon = h.IndexOf(':');
            if (colon > 0) headers[h[..colon].Trim()] = h[(colon + 1)..].Trim();
        }
        // A body (rare: commands are GETs) is read and set aside.
        if (headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var bodyLength) && bodyLength > 0)
        {
            var have = length - headerEnd - 4;
            var scratch = new byte[4096];
            while (have < bodyLength)
            {
                var read = await stream.ReadAsync(scratch.AsMemory(0, Math.Min(scratch.Length, bodyLength - have)), timeout.Token).ConfigureAwait(false);
                if (read == 0) break;
                have += read;
            }
        }
        var (path, query) = CompanionRequest.ParseTarget(line[1]);
        return new CompanionRequest(line[0].ToUpperInvariant(), path, query, headers, remote);
    }

    private static int IndexOf(byte[] buffer, int length, ReadOnlySpan<byte> pattern) =>
        buffer.AsSpan(0, length).IndexOf(pattern);

    private async Task<(int Status, XDocument? Body)> RouteAsync(CompanionRequest request, CancellationToken ct)
    {
        if (request.Method == "OPTIONS") return (200, null);
        var path = request.Path;
        if (request.CommandId > 0) _lastCommandId = request.CommandId;
        Touch(request.Header("X-Plex-Client-Identifier"));

        if (path.Equals("/resources", StringComparison.OrdinalIgnoreCase)) return (200, Resources());

        if (path.StartsWith("/player/timeline/", StringComparison.OrdinalIgnoreCase))
        {
            var action = path["/player/timeline/".Length..].ToLowerInvariant();
            switch (action)
            {
                case "subscribe":
                    Subscribe(request);
                    return (200, Ok());
                case "unsubscribe":
                    if (request.Header("X-Plex-Client-Identifier") is { } id && _subscribers.TryRemove(id, out _))
                        _log.LogInformation("A Plex app stopped following this player");
                    return (200, Ok());
                case "poll":
                    if (request["wait"] == "1")
                    {
                        // Held until something changes (or half a minute), as the apps expect.
                        var changed = _changed.Task;
                        await Task.WhenAny(changed, Task.Delay(TimeSpan.FromSeconds(25), ct)).ConfigureAwait(false);
                    }
                    return (200, _player.Timeline.ToXml(request.CommandId));
            }
            return (404, null);
        }

        if (path.StartsWith("/player/playback/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/player/application/", StringComparison.OrdinalIgnoreCase))
        {
            var command = path[(path.LastIndexOf('/') + 1)..];
            _log.LogInformation("Remote control: {Command}", command);
            var handled = await _player.ExecuteAsync(command, request).ConfigureAwait(false);
            NotifyChanged();
            return handled ? (200, Ok()) : (400, null);
        }

        // Mirroring and navigation aren't for a music player; say so politely.
        if (path.StartsWith("/player/", StringComparison.OrdinalIgnoreCase)) return (200, Ok());
        return (404, null);
    }

    private XDocument Resources() => new(new XDeclaration("1.0", "utf-8", null),
        new XElement("MediaContainer", new XAttribute("size", 1),
            new XElement("Player",
                new XAttribute("title", _name),
                new XAttribute("machineIdentifier", _clientId),
                new XAttribute("product", Plex.PlexClientIdentity.ProductName),
                new XAttribute("version", _version),
                new XAttribute("platform", "Windows"),
                new XAttribute("protocol", "plex"),
                new XAttribute("protocolVersion", "1"),
                new XAttribute("protocolCapabilities", GdmAdvertiser.Capabilities),
                new XAttribute("deviceClass", "pc"))));

    private static XDocument Ok() => new(new XDeclaration("1.0", "utf-8", null),
        new XElement("Response", new XAttribute("code", 200), new XAttribute("status", "OK")));

    private async Task WriteResponseAsync(NetworkStream stream, CompanionRequest request, int status, XDocument? body, CancellationToken ct)
    {
        var bytes = body is null ? [] : Encoding.UTF8.GetBytes(body.Declaration + "\n" + body.Root!.ToString(SaveOptions.DisableFormatting));
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var head = new StringBuilder()
            .Append("HTTP/1.1 ").Append(status).Append(' ').Append(reason).Append("\r\n")
            .Append("Content-Type: text/xml; charset=utf-8\r\n")
            .Append("Content-Length: ").Append(bytes.Length).Append("\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Methods: GET, POST, PUT, OPTIONS\r\n")
            .Append("Access-Control-Allow-Headers: ").Append(request.Header("Access-Control-Request-Headers") ?? "*").Append("\r\n")
            .Append("Access-Control-Expose-Headers: X-Plex-Client-Identifier\r\n")
            .Append("X-Plex-Client-Identifier: ").Append(_clientId).Append("\r\n")
            .Append("Connection: close\r\n\r\n");
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()), ct).ConfigureAwait(false);
        if (bytes.Length > 0) await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------ subscribers

    private sealed class Subscriber
    {
        public required string Id { get; init; }
        public required Uri Endpoint { get; init; }
        public int CommandId { get; set; }
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
        public int Failures { get; set; }
    }

    private void Subscribe(CompanionRequest request)
    {
        var id = request.Header("X-Plex-Client-Identifier") ?? request.Remote?.ToString() ?? "controller";
        var protocol = request["protocol"] is "https" ? "https" : "http";
        if (!int.TryParse(request["port"], out var port) || request.Remote is null) return;
        var host = request.Remote.IsIPv4MappedToIPv6 ? request.Remote.MapToIPv4() : request.Remote;
        var endpoint = new Uri($"{protocol}://{(host.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{host}]" : host.ToString())}:{port}/:/timeline");
        var isNew = !_subscribers.ContainsKey(id);
        _subscribers[id] = new Subscriber { Id = id, Endpoint = endpoint, CommandId = request.CommandId };
        if (isNew) _log.LogInformation("A Plex app ({Device}) is following this player", request.Header("X-Plex-Device-Name") ?? id);
        NotifyChanged();
    }

    /// <summary>
    /// Sends subscribers the timeline: every second while a song plays, at once when something
    /// changes, and every few seconds otherwise so they know the player is still here.
    /// </summary>
    private async Task ReportLoopAsync(CancellationToken ct)
    {
        var lastBeat = DateTime.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var changed = _changed.Task;
            try
            {
                await Task.WhenAny(changed, Task.Delay(1000, ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            if (_subscribers.IsEmpty) continue;
            var timeline = _player.Timeline;
            var due = timeline.State == "playing" || timeline.DiffersFrom(_lastSent) || changed.IsCompleted ||
                      DateTime.UtcNow - lastBeat > TimeSpan.FromSeconds(5);
            if (!due) continue;
            _lastSent = timeline;
            lastBeat = DateTime.UtcNow;
            foreach (var subscriber in _subscribers.Values)
            {
                // Subscriptions lapse unless renewed (controllers renew every half minute or so).
                if (DateTime.UtcNow - subscriber.LastSeen > TimeSpan.FromSeconds(90) || subscriber.Failures > 5)
                {
                    _subscribers.TryRemove(subscriber.Id, out _);
                    continue;
                }
                await SendAsync(subscriber, timeline, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task SendAsync(Subscriber subscriber, CompanionTimeline timeline, CancellationToken ct)
    {
        try
        {
            var xml = timeline.ToXml(Math.Max(subscriber.CommandId, _lastCommandId));
            using var request = new HttpRequestMessage(HttpMethod.Post, subscriber.Endpoint)
            {
                Content = new StringContent(xml.Declaration + "\n" + xml.Root!.ToString(SaveOptions.DisableFormatting), Encoding.UTF8, "text/xml"),
            };
            request.Headers.TryAddWithoutValidation("X-Plex-Client-Identifier", _clientId);
            request.Headers.TryAddWithoutValidation("X-Plex-Device-Name", _name);
            request.Headers.TryAddWithoutValidation("X-Plex-Product", Plex.PlexClientIdentity.ProductName);
            request.Headers.TryAddWithoutValidation("X-Plex-Platform", "Windows");
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            subscriber.Failures = response.IsSuccessStatusCode ? 0 : subscriber.Failures + 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            subscriber.Failures++;
        }
    }

    /// <summary>A subscription renewed (or any command from that controller): it stays subscribed.</summary>
    public void Touch(string? clientId)
    {
        if (clientId is not null && _subscribers.TryGetValue(clientId, out var s)) s.LastSeen = DateTime.UtcNow;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        // Controllers are told the player stopped.
        var stopped = CompanionTimeline.Stopped;
        foreach (var subscriber in _subscribers.Values)
            await SendAsync(subscriber, stopped, CancellationToken.None).ConfigureAwait(false);
        _subscribers.Clear();
        _listener?.Stop();
        _http.Dispose();
    }
}
